// Offline check: turn a real MIDI file into the notes this game would play, so
// the reading and the reduction can be looked at without a phone.
//
//   javac -d out app/src/main/java/com/fallenangel/overlaybeat/*.java tools/MidiDump.java
//   java -cp out MidiDump song.mid [--notes] [--tracks] [--lanes N] [--drums]
import com.fallenangel.overlaybeat.ChartNote;
import com.fallenangel.overlaybeat.MidiChart;
import com.fallenangel.overlaybeat.MidiFile;

import java.io.File;
import java.util.Locale;

public class MidiDump {
    public static void main(String[] args) throws Exception {
        if (args.length == 0) {
            System.out.println("usage: MidiDump <file.mid> [--notes] [--tracks] "
                    + "[--lanes N] [--drums] [--offset S]");
            return;
        }
        MidiFile midi = MidiFile.parse(new File(args[0]));
        MidiChart.Config config = new MidiChart.Config();
        boolean showNotes = false;
        boolean showTracks = false;
        for (int i = 1; i < args.length; i++) {
            switch (args[i]) {
                case "--notes": showNotes = true; break;
                case "--tracks": showTracks = true; break;
                case "--drums": config.trackNameContains = "drum"; break;
                case "--lanes": config.lanes = Integer.parseInt(args[++i]); break;
                case "--offset": config.offsetSeconds = Double.parseDouble(args[++i]); break;
                default: throw new IllegalArgumentException("unknown option " + args[i]);
            }
        }

        System.out.printf(Locale.US, "format=%d division=%d tracks=%d tempos=%d%n",
                midi.format, midi.ticksPerQuarter, midi.trackNames.size(), midi.tempos.size());
        System.out.printf(Locale.US, "raw notes=%d truncated=%d duration=%.1fs bpm=%.1f%n",
                midi.notes.size(), midi.truncatedNotes, midi.durationSeconds(),
                midi.bpmAt(midi.notes.isEmpty() ? 0 : midi.notes.get(0).startTick));
        if (showTracks) {
            int[] perTrack = new int[midi.trackNames.size()];
            for (MidiFile.RawNote note : midi.notes) perTrack[note.track]++;
            for (int i = 0; i < perTrack.length; i++) {
                System.out.printf(Locale.US, "  track %-2d %-24s %5d notes%n",
                        i, "\"" + midi.trackNames.get(i) + "\"", perTrack[i]);
            }
        }

        MidiChart chart = MidiChart.build(midi, config);
        System.out.println("chart: " + chart.summary());
        System.out.printf(Locale.US, "first=%.3fs last=%.3fs%n",
                chart.firstNoteTime, chart.lastNoteTime);
        if (showNotes) {
            for (ChartNote note : chart.notes) {
                System.out.printf(Locale.US, "%.3f lane=%d %s%s%s%n", note.time, note.lane,
                        note.kind, note.accent ? " accent" : "",
                        note.duration > 0 ? String.format(Locale.US, " %.2fs", note.duration) : "");
            }
            System.out.println("#end");
        }
    }
}
