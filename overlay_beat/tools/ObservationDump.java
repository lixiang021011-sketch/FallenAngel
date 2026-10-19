import com.fallenangel.overlaybeat.localai.*;
import java.io.*;
import java.nio.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.MessageDigest;
import java.util.*;

/** Offline PCM16LE/mono/44100 dump and deterministic regression. No Android or ML runtime needed. */
public final class ObservationDump {
    private static final String[] FIXTURES = {"hitchcock", "local_chiharu", "local_feiniao",
            "local_light", "local_tada_kimi_ni_hare", "silence", "quiet_noise", "joined"};

    public static void main(String[] args) throws Exception {
        if (args.length >= 3 && "--regression".equals(args[0])) {
            regression(Paths.get(args[1]), Paths.get(args[2]), args.length > 3 ? Paths.get(args[3]) : null);
        } else if (args.length == 2) {
            byte[] bytes = Files.readAllBytes(Paths.get(args[0]));
            short[] pcm = decode(bytes);
            Path output = Paths.get(args[1]);
            createParent(output);
            try (PrintWriter csv = new PrintWriter(Files.newBufferedWriter(output, StandardCharsets.UTF_8))) {
                csv.println("sample_end,audio_time_s,segment,type,confidence,rms,kick_energy,snare_energy,kick_onset,snare_onset,model");
                Result result = analyse(pcm, 2048, csv);
                System.out.println("observations=" + Arrays.toString(result.counts) + " sha256=" + result.hash);
            }
        } else {
            throw new IllegalArgumentException("Usage: ObservationDump <song.raw> <out.csv> OR "
                    + "--regression <tools/raw> <report.csv> [baseline.csv]");
        }
    }

    private static short[] decode(byte[] bytes) {
        if (bytes.length % 2 != 0) throw new IllegalArgumentException("Truncated PCM16 input");
        short[] pcm = new short[bytes.length / 2];
        ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).asShortBuffer().get(pcm);
        return pcm;
    }

    private static final class Result {
        final long[] counts = new long[MusicObservation.Type.values().length];
        final List<Double> onsets = new ArrayList<>();
        String hash;
        double seconds;
    }

    private static Result analyse(short[] pcm, int chunk, PrintWriter csv) throws Exception {
        Result result = new Result();
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        long started = System.nanoTime(), previousSample = -1;
        try (LocalInferenceEngine engine = new StreamingLocalInferenceEngine()) {
            for (int offset = 0; offset < pcm.length; offset += chunk) {
                int count = Math.min(chunk, pcm.length - offset);
                engine.pushPcm(pcm, offset, count);
                for (MusicObservation o : engine.drainObservations()) {
                    AudioFeatures f = o.features;
                    if (f.endSample < previousSample || f.endSample > offset + count
                            || !Double.isFinite(o.confidence) || o.confidence < 0 || o.confidence > 1) {
                        throw new AssertionError("Invalid observation timestamp or score");
                    }
                    previousSample = f.endSample;
                    result.counts[o.type.ordinal()]++;
                    if (o.type == MusicObservation.Type.ONSET) result.onsets.add(o.audioTimeSeconds);
                    String row = String.format(Locale.ROOT,
                            "%d,%.9f,%d,%s,%.6f,%.9f,%.9f,%.9f,%.9f,%.9f,%s",
                            f.endSample, o.audioTimeSeconds, o.segmentId, o.type, o.confidence,
                            f.rms, f.kickEnergy, f.snareEnergy, f.kickOnset, f.snareOnset, o.modelId);
                    digest.update((row + "\n").getBytes(StandardCharsets.UTF_8));
                    if (csv != null) csv.println(row);
                }
            }
            if (engine.droppedObservations() != 0) throw new AssertionError("Consumer lost observations");
        }
        result.seconds = (System.nanoTime() - started) / 1e9;
        result.hash = hex(digest.digest());
        return result;
    }

    private static void regression(Path raw, Path report, Path baseline) throws Exception {
        Map<String, String[]> expected = new HashMap<>();
        if (baseline != null) {
            for (String line : Files.readAllLines(baseline, StandardCharsets.UTF_8)) {
                if (line.startsWith("file,")) continue;
                String[] fields = line.split(",");
                if (fields.length != 12 || expected.put(fields[0], fields) != null) {
                    throw new IllegalArgumentException("Malformed/duplicate baseline row");
                }
            }
        }
        createParent(report);
        try (PrintWriter csv = new PrintWriter(Files.newBufferedWriter(report, StandardCharsets.UTF_8))) {
            csv.println("file,duration_s,onset,kick,snare,novelty,event_sha256,pcm_sha256,legacy_notes,matched_70ms,elapsed_s,realtime_factor");
            for (String name : FIXTURES) {
                byte[] bytes = Files.readAllBytes(raw.resolve(name + ".raw"));
                short[] pcm = decode(bytes);
                Result a = analyse(pcm, 2048, null), b = analyse(pcm, 137, null);
                if (!a.hash.equals(b.hash) || !Arrays.equals(a.counts, b.counts)) {
                    throw new AssertionError(name + ": chunk size changed the result");
                }
                if (name.equals("silence") || name.equals("quiet_noise")) {
                    if (Arrays.stream(a.counts).sum() != 0) throw new AssertionError(name + ": false positive");
                } else if (a.counts[MusicObservation.Type.ONSET.ordinal()] == 0) {
                    throw new AssertionError(name + ": no onsets");
                }
                Path legacy = raw.resolve(name + "_notes.txt");
                List<Double> reference = new ArrayList<>();
                if (Files.exists(legacy)) {
                    for (String line : Files.readAllLines(legacy, StandardCharsets.UTF_8)) {
                        try { reference.add(Double.parseDouble(line.trim())); }
                        catch (NumberFormatException ignored) { }
                    }
                    if (reference.isEmpty()) throw new AssertionError("Empty reference " + legacy);
                }
                Collections.sort(reference);
                int matched = match(a.onsets, reference, 0.07);
                String row = String.format(Locale.ROOT,
                        "%s,%.6f,%d,%d,%d,%d,%s,%s,%d,%d,%.6f,%.2f",
                        name, pcm.length / 44100.0, a.counts[0], a.counts[1], a.counts[2], a.counts[3],
                        a.hash, hex(MessageDigest.getInstance("SHA-256").digest(bytes)),
                        reference.size(), matched, a.seconds, pcm.length / 44100.0 / a.seconds);
                if (baseline != null) {
                    String[] wanted = expected.remove(name), actual = row.split(",");
                    // Timings are machine-dependent; all fixture and event fields are stable.
                    if (wanted == null || !Arrays.equals(Arrays.copyOf(wanted, 10), Arrays.copyOf(actual, 10))) {
                        throw new AssertionError(name + ": changed from reviewed baseline");
                    }
                }
                csv.println(row);
                csv.flush();
                System.out.printf(Locale.ROOT, "PASS %s %.1fs counts=%s %.1fx realtime; legacy agreement=%d/%d (not accuracy)%n",
                        name, pcm.length / 44100.0, Arrays.toString(a.counts),
                        pcm.length / 44100.0 / a.seconds, matched, reference.size());
            }
        }
        if (!expected.isEmpty()) throw new AssertionError("Extra baseline fixtures: " + expected.keySet());
    }

    private static int match(List<Double> detected, List<Double> reference, double tolerance) {
        int i = 0, j = 0, matched = 0;
        while (i < detected.size() && j < reference.size()) {
            double delta = detected.get(i) - reference.get(j);
            if (Math.abs(delta) <= tolerance) { matched++; i++; j++; }
            else if (delta < 0) i++;
            else j++;
        }
        return matched;
    }

    private static String hex(byte[] bytes) {
        StringBuilder out = new StringBuilder();
        for (byte b : bytes) out.append(String.format(Locale.ROOT, "%02x", b & 255));
        return out.toString();
    }

    private static void createParent(Path path) throws IOException {
        if (path.toAbsolutePath().getParent() != null) Files.createDirectories(path.toAbsolutePath().getParent());
    }
}
