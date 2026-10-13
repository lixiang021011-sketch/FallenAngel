// What the harmony reader says about a recording, window by window.
//
//   javac -d out app/src/main/java/com/fallenangel/listeningai/OnsetCurves.java \
//              app/src/main/java/com/fallenangel/listeningai/listening/Harmony.java \
//              tools/HarmonyDump.java
//   java -cp out HarmonyDump ../overlay_beat/tools/raw/local_chiharu.raw 30 90
//
// The same audio the app gets, the same code the app runs. Written because a
// chord reader that is checked only against chords this repository synthesised
// has been checked against its own assumptions - real music is where the key
// profile meets a mix, and where a wrong answer is obvious to the person who can
// hear it and invisible to the person reading the numbers.
import com.fallenangel.listeningai.OnsetCurves;
import com.fallenangel.listeningai.listening.Harmony;

import java.io.File;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.ShortBuffer;
import java.nio.file.Files;
import java.util.Locale;

public class HarmonyDump {
    private static boolean verbose = false;
    public static void main(String[] args) throws Exception {
        if (args.length == 0) {
            System.out.println("usage: HarmonyDump <song.raw> [fromSeconds] [toSeconds] "
                    + "[windowSeconds] [verbose]");
            return;
        }
        byte[] bytes = Files.readAllBytes(new File(args[0]).toPath());
        ShortBuffer shorts = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).asShortBuffer();
        short[] pcm = new short[shorts.remaining()];
        shorts.get(pcm);

        double from = args.length > 1 ? Double.parseDouble(args[1]) : 0;
        double to = args.length > 2 ? Double.parseDouble(args[2]) : from + 60;
        verbose = args.length > 4;
        double window = args.length > 3 ? Double.parseDouble(args[3]) : 2.0;
        int rate = OnsetCurves.SAMPLE_RATE;

        // The key from everything up to the end of the range, which is what the
        // app will do with a whole song - a two-second window is as much one key
        // as another. Accumulated window by window rather than measured in one
        // pass, because that is the order the app hears it in: the number it has
        // by the time it speaks is the sum of the windows it has been through.
        double[] songChroma = new double[12];
        for (double t = 0; t < to; t += window) {
            int offset = (int) (t * rate);
            int count = (int) Math.min(window * rate, pcm.length - offset);
            if (count < OnsetCurves.FFT_SIZE) break;
            double[] so_far = Harmony.chroma(pcm, offset, count, rate);
            for (int i = 0; i < 12; i++) songChroma[i] += so_far[i];
        }
        Harmony.Key key = Harmony.keyOf(songChroma);
        System.out.printf(Locale.US, "%s: key %s over %.0f-%.0fs%n",
                new File(args[0]).getName(), key.name(), from, to);

        for (double t = from; t + window <= to; t += window) {
            int offset = (int) (t * rate);
            int count = (int) (window * rate);
            double[] chroma = Harmony.chroma(pcm, offset, count, rate);
            Harmony.Reading chord = Harmony.chordOf(chroma, key);
            // The line the card would show, in the notation the card shows it in.
            System.out.printf(Locale.US, "  %6.1fs  %-28s %.2f",
                    t, Harmony.sentence(chord, key), chord.confidence);
            if (verbose) {
                // The measurement itself: twelve numbers, and what the twelve were
                // checked against. This is the whole difference between asking a
                // model and measuring - one of them can be printed.
                System.out.print("   [");
                for (int i = 0; i < 12; i++) {
                    System.out.printf(Locale.US, "%s%.2f%s",
                            Harmony.PITCH[i], chroma[i], i < 11 ? " " : "");
                }
                System.out.print("]");
            }
            System.out.println();
        }
    }
}
