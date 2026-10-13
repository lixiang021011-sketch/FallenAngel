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
        int found = 0;
        for (int i = 0; i < beats.length; i++) {
            if (beats[i] <= seconds) found = i;
            else break;
        }
        return found % beatsPerBar;
    }

    /** Whether this file is about the song the app is playing. */
    public boolean matches(String appLabel, double appDurationSeconds) {
        if (title.isEmpty() || appLabel == null) return false;
        return normalise(title).equals(normalise(appLabel));
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
