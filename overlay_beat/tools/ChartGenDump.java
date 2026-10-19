// Offline check: turn a whole song into a chart, the way the file-based path
// does, and print what it decided.
//
//   javac -d out app/src/main/java/com/fallenangel/overlaybeat/*.java tools/ChartGenDump.java
//   java -cp out ChartGenDump song.raw [easy|normal|hard] [all]
//
// "all" prints every note time, which is the form the Python checks read.
import com.fallenangel.overlaybeat.ChartNote;
import com.fallenangel.overlaybeat.OfflineChart;

import java.io.File;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.ShortBuffer;
import java.nio.file.Files;
import java.util.Locale;

public class ChartGenDump {
    public static void main(String[] args) throws Exception {
        if (args.length == 0) {
            System.out.println("usage: ChartGenDump <song.raw> [easy|normal|hard] [all]");
            return;
        }
        byte[] bytes = Files.readAllBytes(new File(args[0]).toPath());
        ShortBuffer shorts = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).asShortBuffer();
        short[] pcm = new short[shorts.remaining()];
        shorts.get(pcm);

        OfflineChart.Difficulty difficulty = OfflineChart.Difficulty.NORMAL;
        for (int i = 1; i < args.length; i++) {
            if ("easy".equals(args[i])) difficulty = OfflineChart.Difficulty.EASY;
            if ("hard".equals(args[i])) difficulty = OfflineChart.Difficulty.HARD;
            if ("--verbose".equals(args[i])) OfflineChart.verbose = true;
        }

        long started = System.nanoTime();
        OfflineChart chart = OfflineChart.analyse(pcm, difficulty);
        double seconds = (System.nanoTime() - started) / 1e9;

        System.out.printf(Locale.US, "audio %.1fs, analysed in %.1fs (%.0fx real time)%n",
                pcm.length / 44100.0, seconds, pcm.length / 44100.0 / seconds);
        System.out.println("chart: " + chart.summary());
        System.out.printf(Locale.US, "grid: bpm %.2f, period %.3f frames%n",
                chart.bpm, chart.periodFrames);

        // A few bars of the figure, so a human can see what the analysis read.
        StringBuilder line = new StringBuilder();
        for (int bar = 0; bar < Math.min(8, chart.barFigures.length); bar++) {
            line.append(String.format(Locale.US, " %04x", chart.barFigures[bar]));
        }
        System.out.println("figures (first bars, hex):" + line);

        if (args.length > 1 && "all".equals(args[args.length - 1])) {
            for (ChartNote note : chart.notes) {
                System.out.printf(Locale.US, "%.4f%n", note.time);
            }
            System.out.println("#end");
        }
    }
}
