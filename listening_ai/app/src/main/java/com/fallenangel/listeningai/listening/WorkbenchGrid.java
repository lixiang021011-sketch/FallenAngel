package com.fallenangel.listeningai.listening;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * A beat grid computed offline, read from a .mgmcs.json.
 *
 * The app tracks beats from the audio, and tracking is guesswork: a phase found by
 * letting it settle, a period corrected by how the error grows, a meter assumed and
 * sometimes a half bar out. The Workbench files hold the same thing decided
 * elsewhere - with more of the song to look at, and with a person able to correct it
 * - so when one exists for the song being played, it is used instead.
 *
 * What is read, and what is deliberately not:
 *
 *   beats            the point of the whole file. Exact times, so the grid does not
 *                    have to be extrapolated from a tempo and a phase
 *   beatsPerBar      the meter, which the app otherwise assumes
 *   reading, source  which of the several readings this is, and who produced it
 *   health, suspect  a gate: a reading the validator distrusted is not used at all
 *   audioTime        where a piece of a grid starts; several entries with the same
 *                    reading are one grid in pieces, which is how a song that
 *                    changes tempo is written down
 *   bpm              a fallback when there are no beat times, and a number for the log
 *   title, duration  matching, and only matching: the title is the key and the
 *                    duration only breaks ties, because the player's own duration is
 *                    wrong by tens of seconds on this phone
 *
 * Not read: the events. A grid is a coordinate system and an event hangs off it;
 * importing both at once would make a mistake in either look like a mistake in both.
 *
 * Kept away from Android: it is a parser and a choice of readings.
 */
public final class WorkbenchGrid {
    /** Readings from best to worst, by who decided them. */
    private static final String[] RANK = {"workbench", "human_snapped", "human_unquantized"};

    public final String title;
    public final double durationSeconds;
    /** Which reading was used, for the record. */
    public final String reading;
    public final String generatedBy;
    public final int beatsPerBar;
    /** Beat times in seconds, ascending, one grid stitched from its pieces. */
    private final double[] beats;
    public final double bpm;

    private WorkbenchGrid(String title, double durationSeconds, String reading, String generatedBy,
                          int beatsPerBar, double[] beats, double bpm) {
        this.title = title == null ? "" : title;
        this.durationSeconds = durationSeconds;
        this.reading = reading == null ? "" : reading;
        this.generatedBy = generatedBy == null ? "" : generatedBy;
        this.beatsPerBar = beatsPerBar;
        this.beats = beats;
        this.bpm = bpm;
    }

    public int beatCount() {
        return beats.length;
    }

    public double beat(int index) {
        return beats[Math.max(0, Math.min(beats.length - 1, index))];
    }

    /** The first beat, which is what a downbeat lock needs. */
    public double firstBeat() {
        return beats.length == 0 ? 0 : beats[0];
    }

    /**
     * Whether the grid still says anything about this point of the song.
     *
     * False past the last beat: a grid that has run out is not a grid, and pretending
     * otherwise is how a file that stops half way becomes silence that looks like
     * knowledge.
     */
    public boolean covers(double seconds) {
        return beats.length > 0 && seconds <= beats[beats.length - 1];
    }

    /** Where this point falls in the bar, 0-based, or -1 when the grid has run out. */
    public int beatInBar(double seconds) {
        if (!covers(seconds) || beatsPerBar <= 0) return -1;
        return positionAt(seconds) % beatsPerBar;
    }

    /** The index of the last beat at or before this point, or -1 past the end. */
    public int positionAt(double seconds) {
        if (!covers(seconds)) return -1;
        int found = 0;
        for (int i = 0; i < beats.length; i++) {
            if (beats[i] <= seconds) found = i;
            else break;
        }
        return found;
    }

    /**
     * The tempo around this point, from the spacing of the beats themselves.
     *
     * Measured locally rather than taken from the file's `bpm` field, because a song
     * that changes tempo is written down as pieces with one bpm at the top: the
     * number that belongs on screen is the one for where the music is, which is what
     * the times of the nearby beats say.
     */
    public double localBpm(double seconds) {
        int at = positionAt(seconds);
        if (at < 0) at = beats.length - 1;
        if (at < 1) {
            return beats.length >= 2 && beats[1] > beats[0]
                    ? 60.0 / (beats[1] - beats[0]) : bpm;
        }
        // Medians of the few beats around it, so one odd gap cannot move the number.
        int first = Math.max(0, at - 3);
        int last = Math.min(beats.length - 1, at + 3);
        java.util.List<Double> gaps = new ArrayList<>();
        for (int i = first; i < last; i++) {
            double gap = beats[i + 1] - beats[i];
            if (gap > 0) gaps.add(gap);
        }
        if (gaps.isEmpty()) return bpm;
        java.util.Collections.sort(gaps);
        double middle = gaps.get(gaps.size() / 2);
        return middle > 0 ? 60.0 / middle : bpm;
    }

    /**
     * How much of this file's title is in the app's label for what is playing.
     *
     * Not equality, and the reason is in the two systems' own files: the Workbench
     * writes "ACIDMAN - 赤橙 (Remastered 2019)" and the app, reading the player's
     * session, writes "ACIDMAN (アシッドマン) — 赤橙 (Remastered 2019)". The same song,
     * with the artist's own spelling added in the middle - so an equality test
     * rejects exactly the file that is right.
     *
     * A share of the file's own words is the honest measure: the file's title is the
     * shorter and cleaner of the two, and every one of its words ought to appear in
     * the label the player gave.
     */
    public double titleOverlap(String appLabel) {
        if (title.isEmpty() || appLabel == null) return 0;
        java.util.Set<String> mine = words(normalise(title));
        java.util.Set<String> theirs = words(normalise(appLabel));
        if (mine.isEmpty()) return 0;
        int found = 0;
        for (String word : mine) if (theirs.contains(word)) found++;
        return found / (double) mine.size();
    }

    /** Enough of the title to be the same song, and at least two words to go on. */
    public boolean matches(String appLabel, double appDurationSeconds) {
        java.util.Set<String> mine = words(normalise(title));
        return mine.size() >= 2 && titleOverlap(appLabel) >= 0.8;
    }

    private static java.util.Set<String> words(String normalised) {
        java.util.Set<String> out = new java.util.HashSet<>();
        for (String word : normalised.split(" ")) {
            // One and two character words are punctuation-sized ("feat", "2019" aside)
            // and carry almost no identity; every word counts, so this only drops the
            // empty ones the split leaves behind.
            if (!word.isEmpty()) out.add(word);
        }
        return out;
    }

    /** Titles differ in punctuation and spacing between the two systems. */
    static String normalise(String text) {
        String clean = text.toLowerCase(java.util.Locale.US).trim();
        clean = clean.replaceAll("[\\p{Punct}\\s]+", " ").replaceAll("\\s+", " ").trim();
        return clean;
    }

    /** Reads a file, or null when it holds no grid that may be used. */
    public static WorkbenchGrid from(JSONObject root) {
        if (root == null) return null;
        JSONObject song = root.optJSONObject("song");
        JSONArray calibrations = root.optJSONArray("calibrations");
        if (song == null || calibrations == null) return null;

        // Which reading to use: the best-ranked one that is also healthy. A reading
        // the validator distrusted is not a reading to build on, whatever its rank.
        String chosenReading = null;
        for (String rank : RANK) {
            if (hasUsable(calibrations, rank)) {
                chosenReading = rank;
                break;
            }
        }
        if (chosenReading == null) {
            for (int i = 0; i < calibrations.length(); i++) {
                JSONObject item = calibrations.optJSONObject(i);
                if (item != null && !isSuspect(item) && item.optJSONArray("beats") != null) {
                    chosenReading = item.optString("reading", "");
                    break;
                }
            }
        }
        if (chosenReading == null) return null;

        // Every healthy entry with that reading is a piece of the same grid: a song
        // that changes tempo is written down as several, each starting where the last
        // stopped.
        List<double[]> pieces = new ArrayList<>();
        int beatsPerBar = 0;
        double bpm = 0;
        for (int i = 0; i < calibrations.length(); i++) {
            JSONObject item = calibrations.optJSONObject(i);
            if (item == null || isSuspect(item)) continue;
            if (!chosenReading.equals(item.optString("reading", ""))) continue;
            JSONArray beats = item.optJSONArray("beats");
            if (beats == null || beats.length() == 0) continue;
            double[] times = new double[beats.length()];
            for (int b = 0; b < beats.length(); b++) times[b] = beats.optDouble(b, 0);
            pieces.add(times);
            if (beatsPerBar == 0) beatsPerBar = item.optInt("beatsPerBar", 0);
            if (bpm == 0) bpm = item.optDouble("bpm", 0);
        }
        if (pieces.isEmpty()) return null;
        Collections.sort(pieces, (a, b) -> Double.compare(a[0], b[0]));
        int total = 0;
        for (double[] piece : pieces) total += piece.length;
        double[] beats = new double[total];
        int at = 0;
        for (double[] piece : pieces) {
            System.arraycopy(piece, 0, beats, at, piece.length);
            at += piece.length;
        }

        return new WorkbenchGrid(song.optString("title", ""),
                song.optDouble("durationSeconds", -1), chosenReading,
                root.optString("generatedBy", ""), beatsPerBar, beats, bpm);
    }

    private static boolean hasUsable(JSONArray calibrations, String reading) {
        for (int i = 0; i < calibrations.length(); i++) {
            JSONObject item = calibrations.optJSONObject(i);
            if (item == null || isSuspect(item)) continue;
            if (reading.equals(item.optString("reading", "")) && item.optJSONArray("beats") != null) {
                return true;
            }
        }
        return false;
    }

    /** A reading the validator distrusted, or one with nothing in it. */
    private static boolean isSuspect(JSONObject calibration) {
        String health = calibration.optString("health", "ok");
        if (!health.isEmpty() && !"ok".equals(health)) return true;
        JSONArray reasons = calibration.optJSONArray("suspectReasons");
        return reasons != null && reasons.length() > 0;
    }
}
