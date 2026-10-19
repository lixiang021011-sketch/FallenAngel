// Offline cross-check: run the on-device detector over a raw PCM file so its
// tempo estimate and note times can be compared with the Python prototype.
//
//   ffmpeg -i song.ogg -ac 1 -ar 44100 -f s16le song.raw
//   javac -d out app/src/main/java/.../BeatDetector.java tools/BeatDump.java
//   java -cp out BeatDump song.raw
import com.fallenangel.overlaybeat.BeatDetector;
import com.fallenangel.overlaybeat.Beat;
import com.fallenangel.overlaybeat.ChartBuilder;
import com.fallenangel.overlaybeat.ChartNote;

import java.io.File;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.ShortBuffer;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

public class BeatDump {
    public static void main(String[] args) throws Exception {
        if (args.length >= 2 && "--taps".equals(args[0])) {
            BeatDetector tapped = new BeatDetector();
            tapped.beginTapTempo();
            for (int i = 1; i < args.length; i++) {
                tapped.onTapTempo(Double.parseDouble(args[i]));
            }
            System.out.printf(Locale.US, "taps -> bpm=%.1f%n", tapped.bpm());
            return;
        }
        byte[] bytes = Files.readAllBytes(new File(args[0]).toPath());
        ShortBuffer shorts = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).asShortBuffer();
        short[] all = new short[shorts.remaining()];
        shorts.get(all);

        BeatDetector detector = new BeatDetector();
        ChartBuilder builder = new ChartBuilder();
        if (args.length > 1) builder.setStride(Integer.parseInt(args[1]));
        // Calibration knobs: how fine the grid is, and how much usually has to
        // happen there for the position to earn a note.
        for (int i = 2; i < args.length; i++) {
            if ("--subdivisions".equals(args[i])) {
                detector.setSubdivisions(Integer.parseInt(args[++i]));
            } else if ("--share".equals(args[i])) {
                builder.setSlotShare(Double.parseDouble(args[++i]));
            } else if ("--predict".equals(args[i])) {
                BeatDetector.useSongAverage = "average".equals(args[++i]);
            }
        }
        System.out.printf(Locale.US, "grid: %d positions per beat, note share %.2f%n",
                detector.subdivisions(), builder.slotShare());

        List<Double> notes = new ArrayList<>();
        List<Double> leads = new ArrayList<>();
        List<Double> bpms = new ArrayList<>();
        int kickNotes = 0;
        int snareNotes = 0;
        int accents = 0;
        double bpmAtFirstNote = 0;
        int periodAtFirstNote = 0;
        int block = 2048;
        for (int offset = 0; offset < all.length; offset += block) {
            int count = Math.min(block, all.length - offset);
            short[] chunk = new short[count];
            System.arraycopy(all, offset, chunk, 0, count);
            detector.push(chunk, count);
            for (Beat beat : detector.drainBeats()) {
                ChartNote note = builder.accept(beat);
                if (note == null) continue;
                if (notes.isEmpty()) {
                    bpmAtFirstNote = detector.bpm();
                    periodAtFirstNote = detector.periodFrames();
                }
                notes.add(note.time);
                bpms.add(detector.bpm());
                leads.add(note.time - detector.audioTime());
                if (beat.kick >= 0 || beat.snare >= 0) {
                    if (beat.kickDominant()) kickNotes++;
                    else snareNotes++;
                }
                if (note.accent) accents++;
            }
        }

        System.out.printf(Locale.US, "java bpm=%.1f locked=%s notes=%d dur=%.1fs%n",
                detector.bpm(), detector.locked(), notes.size(), detector.audioTime());
        System.out.printf(Locale.US, "chart: %d kick, %d snare, %d accents%n",
                kickNotes, snareNotes, accents);
        System.out.println("figures per beat: " + detector.describeFigures());
        System.out.printf(Locale.US, "locked at first note: bpm=%.1f period=%d frames%n",
                bpmAtFirstNote, periodAtFirstNote);
        StringBuilder times = new StringBuilder();
        for (int i = 0; i < Math.min(24, notes.size()); i++) {
            times.append(String.format(Locale.US, " %.2f", notes.get(i)));
        }
        System.out.println("first notes:" + times);
        StringBuilder leadText = new StringBuilder();
        for (int i = 0; i < Math.min(8, leads.size()); i++) {
            leadText.append(String.format(Locale.US, " %.2f", leads.get(i)));
        }
        System.out.println("lead at emit:" + leadText + "   (target "
                + BeatDetector.SCHEDULE_LEAD_SECONDS + ")");
        double minLead = Double.MAX_VALUE;
        for (double lead : leads) minLead = Math.min(minLead, lead);
        if (!leads.isEmpty()) {
            System.out.printf(Locale.US, "min lead: %.2f s   (must be >= travel %.2f s)%n",
                    minLead, BeatDetector.TRAVEL_SECONDS);
        }
        if (!bpms.isEmpty()) {
            java.util.Collections.sort(bpms);
            System.out.printf(Locale.US, "median bpm while playing: %.1f%n",
                    bpms.get(bpms.size() / 2));
            System.out.printf(Locale.US, "bpm span: %.1f .. %.1f  (stability)%n",
                    bpms.get(0), bpms.get(bpms.size() - 1));
        }
        if (args.length > 2 && "trace".equals(args[2])) {
            for (int i = 0; i < notes.size(); i++) {
                System.out.printf(Locale.US, "trace %.2f %.1f%n", notes.get(i), bpms.get(i));
            }
        }
        List<Double> settled = new ArrayList<>();
        for (int i = 0; i < bpms.size(); i++) {
            if (i < notes.size() && notes.get(i) > 20.0) settled.add(bpms.get(i));
        }
        if (!settled.isEmpty()) {
            java.util.Collections.sort(settled);
            System.out.printf(Locale.US, "settled (after 20s): bpm=%.1f  span=%.1f..%.1f%n",
                    settled.get(settled.size() / 2), settled.get(0),
                    settled.get(settled.size() - 1));
        }
        if (args.length > 2 && "all".equals(args[2])) {
            for (double time : notes) System.out.printf(Locale.US, "%.4f%n", time);
            System.out.println("#end");
        }
    }
}
