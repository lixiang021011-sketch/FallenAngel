// Offline check of the pause question: pretend the listener stopped at a given
// second of a real song and print what the system would ask, and why.
//
//   javac -d out app/src/main/java/com/fallenangel/listeningai/*.java \
//              app/src/main/java/com/fallenangel/listeningai/localai/*.java \
//              app/src/main/java/com/fallenangel/listeningai/listening/*.java \
//              tools/ListeningDump.java
//   java -cp out ListeningDump tools/raw/song.raw 60 90
//
// The grid comes from the same live detector the service uses, fed the whole
// file: what a question says here is what it would say on the phone.
import com.fallenangel.listeningai.BeatDetector;
import com.fallenangel.listeningai.OnsetCurves;
import com.fallenangel.listeningai.listening.ListeningAnalyzer;
import com.fallenangel.listeningai.listening.PauseQuestion;
import com.fallenangel.listeningai.localai.MusicSectionEngine;
import com.fallenangel.listeningai.localai.SectionObservation;

import java.io.File;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.ShortBuffer;
import java.nio.file.Files;
import java.util.List;
import java.util.Locale;

public class ListeningDump {
    /** How much audio the system looks back over when the listener pauses. */
    private static final double WINDOW_SECONDS = 8;

    public static void main(String[] args) throws Exception {
        if (args.length == 0) {
            System.out.println("usage: ListeningDump <song.raw> [pauseSeconds...]");
            return;
        }
        byte[] bytes = Files.readAllBytes(new File(args[0]).toPath());
        ShortBuffer shorts = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).asShortBuffer();
        short[] pcm = new short[shorts.remaining()];
        shorts.get(pcm);
        double duration = pcm.length / (double) OnsetCurves.SAMPLE_RATE;

        List<SectionObservation> sections = sections(pcm);

        System.out.printf(Locale.US, "%s: %.1fs, %d sections found%n",
                new File(args[0]).getName(), duration, sections.size());

        double[] pauses = new double[Math.max(0, args.length - 1)];
        for (int i = 1; i < args.length; i++) pauses[i - 1] = Double.parseDouble(args[i]);
        if (pauses.length == 0) {
            int count = (int) (duration / 30) - 1;
            pauses = new double[Math.max(1, count)];
            for (int i = 0; i < pauses.length; i++) pauses[i] = 30 + i * 30;
        }

        int offered = 0;
        for (double pause : pauses) {
            if (pause < WINDOW_SECONDS + 1 || pause > duration) continue;
            int end = (int) (pause * OnsetCurves.SAMPLE_RATE);
            int start = end - (int) (WINDOW_SECONDS * OnsetCurves.SAMPLE_RATE);
            long boundary = -1;
            String label = "";
            for (SectionObservation section : sections) {
                if (section.boundarySeconds() <= pause) {
                    label = section.label;
                    boundary = Math.round(section.boundarySeconds() * OnsetCurves.SAMPLE_RATE);
                }
            }
            // The grid is read as it stood at the pause, not as it ended up: a
            // song that fades out leaves the detector unlocked, and asking the
            // finished analysis where bar 21 was would answer nothing.
            BeatDetector grid = gridAt(pcm, end);
            PauseQuestion question = ListeningAnalyzer.analyse(pcm, start, end - start, start,
                    gridFor(grid), label, boundary);
            System.out.printf(Locale.US,
                    "%n  pause at %.1fs -> target %.2fs  bar %d beat %d %s  section %s%n",
                    pause, question.targetSeconds(), question.bar + 1, question.beat,
                    question.subdivisionName(), label.isEmpty() ? "-" : label);
            System.out.println("  " + question.question);
            int real = 0;
            for (PauseQuestion.Candidate candidate : question.candidates) {
                if (!"none".equals(candidate.id)) real++;
                System.out.printf(Locale.US, "    %-26s %.2f  %s%n",
                        candidate.label, candidate.confidence, candidate.evidence);
            }
            if (real > 0) offered++;
        }
        System.out.printf(Locale.US, "%n%d of %d pauses had something to offer%n",
                offered, pauses.length);
    }

    private static List<SectionObservation> sections(short[] pcm) {
        MusicSectionEngine engine = new MusicSectionEngine();
        engine.pushPcm(pcm, 0, pcm.length);
        List<SectionObservation> out = engine.drainSections();
        engine.close();
        return out;
    }

    /** The detector as it stood at a given moment, fed everything up to it. */
    private static BeatDetector gridAt(short[] pcm, int endSample) {
        BeatDetector grid = new BeatDetector();
        for (int offset = 0; offset < endSample; offset += 4096) {
            int count = Math.min(4096, endSample - offset);
            short[] chunk = new short[count];
            System.arraycopy(pcm, offset, chunk, 0, count);
            grid.push(chunk, count);
            grid.drainBeats();
        }
        return grid;
    }

    /** The song's beat grid, as the detector finishes with it. */
    private static ListeningAnalyzer.Grid gridFor(BeatDetector grid) {
        return new ListeningAnalyzer.Grid() {
            @Override public int positionInBar(double seconds) {
                int index = grid.positionAt(seconds);
                return index == Integer.MIN_VALUE
                        ? -1 : Math.floorMod(index, grid.positionsPerBar());
            }

            @Override public int barAt(double seconds) {
                int index = grid.positionAt(seconds);
                return index == Integer.MIN_VALUE
                        ? -1 : Math.floorDiv(index, grid.positionsPerBar());
            }

            @Override public int positionsPerBar() {
                return grid.positionsPerBar();
            }

            @Override public int subdivisionsPerBeat() {
                return BeatDetector.POSITIONS_PER_BEAT;
            }
        };
    }
}
