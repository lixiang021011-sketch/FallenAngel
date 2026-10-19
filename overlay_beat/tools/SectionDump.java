import com.fallenangel.overlaybeat.localai.*;
import java.nio.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.MessageDigest;
import java.util.*;

/** Offline section timelines and deterministic regression; no structural ground truth implied. */
public final class SectionDump {
    private static final String[] FIXTURES = {"hitchcock", "local_chiharu", "local_feiniao",
            "local_light", "local_tada_kimi_ni_hare", "silence", "quiet_noise", "joined"};
    private static final String HEADER = "stream,section,boundary_s,detected_s,label,previous_label,kind,confidence,novelty,similarity";

    public static void main(String[] args) throws Exception {
        if (args.length >= 3 && args[0].equals("--regression")) {
            regression(Paths.get(args[1]), Paths.get(args[2]), args.length > 3 ? Paths.get(args[3]) : null);
        } else if (args.length == 2) {
            Result result = analyse(decode(Files.readAllBytes(Paths.get(args[0]))), 2048);
            save(Paths.get(args[1]), result.rows);
            System.out.println("Sections: " + result.sections.size() + "; " + result.structure());
        } else throw new IllegalArgumentException("SectionDump <song.raw> <out.csv> OR --regression <raw-dir> <output-dir> [baseline.csv]");
    }

    private static final class Result {
        List<String> rows = new ArrayList<>();
        List<SectionObservation> sections = new ArrayList<>();
        double seconds;
        String hash;
        String structure() {
            StringJoiner out = new StringJoiner("-");
            for (SectionObservation s : sections) out.add(s.label);
            return out.toString();
        }
    }

    private static short[] decode(byte[] bytes) {
        if (bytes.length % 2 != 0) throw new IllegalArgumentException("Truncated PCM16");
        short[] pcm = new short[bytes.length / 2];
        ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).asShortBuffer().get(pcm);
        return pcm;
    }

    private static Result analyse(short[] pcm, int chunk) throws Exception {
        Result result = new Result();
        result.rows.add(HEADER);
        long started = System.nanoTime(), lastBoundary = -1;
        try (MusicSectionEngine engine = new MusicSectionEngine()) {
            for (int offset = 0; offset < pcm.length; offset += chunk) {
                int count = Math.min(chunk, pcm.length - offset);
                engine.pushPcm(pcm, offset, count);
                for (SectionObservation s : engine.drainSections()) {
                    if (s.boundarySample < 0 || s.boundarySample <= lastBoundary
                            || s.detectedAtSample > offset + count || s.detectedAtSample < s.boundarySample
                            || !Double.isFinite(s.novelty) || s.confidence < 0 || s.confidence > 1) {
                        throw new AssertionError("invalid boundary");
                    }
                    if (s.kind != SectionObservation.Kind.START && (s.boundarySample - lastBoundary < 8L * 44100
                            || s.detectedAtSample - s.boundarySample != 5L * 44100)) {
                        throw new AssertionError("invalid duration or confirmation delay");
                    }
                    lastBoundary = s.boundarySample;
                    result.sections.add(s);
                    result.rows.add(String.format(Locale.ROOT, "%d,%d,%.3f,%.3f,%s,%s,%s,%.6f,%.6f,%.6f",
                            s.streamId, s.sectionIndex, s.boundarySeconds(), s.detectedAtSeconds(), s.label, s.previousLabel,
                            s.kind, s.confidence, s.novelty, s.similarity));
                }
            }
            if (engine.droppedSections() != 0) throw new AssertionError("lost sections");
        }
        result.seconds = (System.nanoTime() - started) / 1e9;
        result.hash = hash(String.join("\n", result.rows).getBytes(StandardCharsets.UTF_8));
        return result;
    }

    private static void regression(Path raw, Path output, Path baseline) throws Exception {
        Files.createDirectories(output);
        Map<String, String> expected = new HashMap<>();
        if (baseline != null) {
            for (String row : Files.readAllLines(baseline, StandardCharsets.UTF_8)) {
                if (row.startsWith("file,")) continue;
                String[] parts = row.split(",", -1);
                if (parts.length != 7 || expected.put(parts[0], String.join(",", Arrays.copyOf(parts, 5))) != null) {
                    throw new IllegalArgumentException("invalid baseline");
                }
            }
        }
        List<String> summary = new ArrayList<>();
        summary.add("file,duration_s,sections,event_sha256,pcm_sha256,analysis_s,structure");
        for (String name : FIXTURES) {
            byte[] bytes = Files.readAllBytes(raw.resolve(name + ".raw"));
            short[] pcm = decode(bytes);
            Result result = analyse(pcm, 2048), other = analyse(pcm, 137);
            if (!result.hash.equals(other.hash)) throw new AssertionError(name + " changes with chunk size");
            boolean control = name.equals("silence") || name.equals("quiet_noise");
            if (control != result.sections.isEmpty()) throw new AssertionError(name + " silence/start invariant");
            String stable = String.format(Locale.ROOT, "%s,%.6f,%d,%s,%s", name,
                    pcm.length / 44100.0, result.sections.size(), result.hash, hash(bytes));
            if (baseline != null && !stable.equals(expected.remove(name))) {
                throw new AssertionError(name + " changed from section baseline");
            }
            save(output.resolve(name + "-sections.csv"), result.rows);
            summary.add(String.format(Locale.ROOT, "%s,%.6f,%s", stable, result.seconds, result.structure()));
            System.out.printf(Locale.ROOT, "PASS %s %.1fs: %s (%d sections, %.2fs analysis)%n", name,
                    pcm.length / 44100.0, result.structure(), result.sections.size(), result.seconds);
        }
        if (!expected.isEmpty()) throw new AssertionError("extra baseline rows");
        save(output.resolve("section-regression.csv"), summary);
    }

    private static void save(Path path, List<String> rows) throws Exception {
        Files.createDirectories(path.toAbsolutePath().getParent());
        Files.write(path, rows, StandardCharsets.UTF_8);
    }

    private static String hash(byte[] bytes) throws Exception {
        StringBuilder out = new StringBuilder();
        for (byte b : MessageDigest.getInstance("SHA-256").digest(bytes)) out.append(String.format(Locale.ROOT, "%02x", b & 255));
        return out.toString();
    }
}
