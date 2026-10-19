package com.fallenangel.overlaybeat;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;

/**
 * The charts available on this phone, and which one belongs to the song that is
 * playing.
 *
 * Matching is done on the chart's own metadata, not on filenames: FallenAngel
 * charts are named after their source (`cc_bit_shift.json`) while the player
 * reports a title (`Bit Shift`), so anything filename-based would need a naming
 * convention that nobody keeps. Normalised containment handles it.
 */
public final class ChartLibrary {
    private final File dir;
    private final List<ChartFile> charts = new ArrayList<>();
    private int failed;

    public ChartLibrary(File dir) {
        this.dir = dir;
    }

    public File directory() {
        return dir;
    }

    public int size() {
        return charts.size();
    }

    public int failed() {
        return failed;
    }

    /** Parse everything once. Charts are small and this runs off the main thread. */
    public void scan() {
        charts.clear();
        failed = 0;
        File[] files = dir == null ? null : dir.listFiles((d, name) -> name.endsWith(".json"));
        if (files == null) return;
        for (File file : files) {
            try {
                String json = new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8);
                charts.add(ChartFile.parse(json, -1));
            } catch (Exception e) {
                failed++;
            }
        }
    }

    /**
     * The best chart for a song, or null when nothing is close enough.
     *
     * Score, rather than a boolean match, because a phone library will hold
     * several charts of the same song (per instrument, per difficulty) and the
     * best-named one should win rather than the first one found.
     */
    public ChartFile find(String artist, String title) {
        String wantedTitle = Names.normalise(title);
        String wantedArtist = Names.normalise(artist);
        if (wantedTitle.isEmpty()) return null;

        ChartFile best = null;
        int bestScore = 0;
        for (ChartFile chart : charts) {
            String chartTitle = Names.normalise(chart.songName);
            int score = Names.score(chartTitle, wantedTitle);
            if (score == 0) continue;
            if (!wantedArtist.isEmpty()
                    && Names.normalise(chart.songArtist).equals(wantedArtist)) score += 50;
            if (score > bestScore) {
                bestScore = score;
                best = chart;
            }
        }
        return best;
    }
}
