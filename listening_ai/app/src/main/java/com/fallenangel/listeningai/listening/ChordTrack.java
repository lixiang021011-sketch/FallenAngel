package com.fallenangel.listeningai.listening;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * A song's chords as the offline side worked them out, read from a file.
 *
 * There are two ways to know a song's chords and this app had the worse one: it
 * listened to the music and measured. That works, and it is why a chord can be shown
 * for a song nobody has ever analysed - but it is late by its own window, it needs a
 * cache of the previous listen to be on time the second time through, and it is a
 * guess about a thing somebody else had already measured properly. The Workbench's
 * harmony file is that somebody: every half bar of the song, with the chord from its
 * own notes, a second reading from an upstream chart, and a third from madmom.
 *
 * What is read:
 *
 *   spans[]   one per half bar: `start`/`end` in song seconds, `bar` and `half`, the
 *             chord as `template` (their notes, the one that can be explained), the
 *             degree and its `roman` numeral, and the two other readings to compare
 *             with
 *   key       the key of the song, and how much of it the estimate covers. Shown as
 *             "A# 调" without the mode: measured 2026-10-14, a listener asked for
 *             that, and the mode is in the record for whoever wants it
 *   reading   which timing reading the bars were put on, for the record
 *
 * Disagreement is kept rather than smoothed: the three readings disagreeing over a
 * span is the one thing worth telling a listener about - "this half bar is the one
 * to listen to" - so it is a flag on the moment, not a reason to drop the chord.
 *
 * Kept away from Android: a parser and a lookup by time.
 */
public final class ChordTrack {
    /** The chord at a moment, and whether the readings agreed about it. */
    public static final class Chord {
        public final String label;
        public final boolean disagreed;

        Chord(String label, boolean disagreed) {
            this.label = label;
            this.disagreed = disagreed;
        }
    }

    public final String song;
    /** Which timing reading the half bars were counted on. */
    public final String reading;
    public final String keyName;
    public final double agreement;

    private final double[] starts;
    private final double[] ends;
    private final String[] labels;
    private final boolean[] disagreed;

    private ChordTrack(String song, String reading, String keyName, double agreement,
                       double[] starts, double[] ends, String[] labels, boolean[] disagreed) {
        this.song = song == null ? "" : song;
        this.reading = reading == null ? "" : reading;
        this.keyName = keyName == null ? "" : keyName;
        this.agreement = agreement;
        this.starts = starts;
        this.ends = ends;
        this.labels = labels;
        this.disagreed = disagreed;
    }

    public int size() {
        return starts.length;
    }

    /** The first moment the file has a chord for, and the last: what it covers. */
    public double firstStart() {
        return starts.length == 0 ? 0 : starts[0];
    }

    public double lastEnd() {
        return ends.length == 0 ? 0 : ends[ends.length - 1];
    }

    /** Whether the file has something to say about this moment of the song. */
    public boolean covers(double seconds) {
        return indexAt(seconds) >= 0;
    }

    /** The chord at this moment, or null when the file does not reach it. */
    public Chord at(double seconds) {
        int found = indexAt(seconds);
        return found < 0 ? null : new Chord(labels[found], disagreed[found]);
    }

    private int indexAt(double seconds) {
        if (starts.length == 0) return -1;
        // In order, so a scan is enough for a few hundred half bars.
        for (int i = 0; i < starts.length; i++) {
            if (seconds >= starts[i] && seconds < ends[i]) return i;
        }
        return -1;
    }

    /**
     * The song's track out of a harmony file, or null when it holds none for it.
     *
     * Two shapes are accepted, because both are ways the other side may hand it over:
     * one file per song (`spans` at the top) and one file for everything (`songs[]`,
     * matched by the audio file name it was measured from).
     */
    public static ChordTrack from(JSONObject root, String appLabel) {
        if (root == null) return null;
        JSONArray songs = root.optJSONArray("songs");
        if (songs == null) return parse(root, appLabel);
        for (int i = 0; i < songs.length(); i++) {
            JSONObject song = songs.optJSONObject(i);
            if (song == null) continue;
            if (!WorkbenchGrid.sameSong(stem(song.optString("audio", song.optString("song", ""))),
                    appLabel)) {
                continue;
            }
            return parse(song, appLabel);
        }
        return null;
    }

    private static ChordTrack parse(JSONObject song, String appLabel) {
        JSONArray spans = song.optJSONArray("spans");
        if (spans == null || spans.length() == 0) return null;
        List<Double> starts = new ArrayList<>();
        List<Double> ends = new ArrayList<>();
        List<String> labels = new ArrayList<>();
        List<Boolean> disagreed = new ArrayList<>();
        String keyName = keyName(song.optJSONObject("key"));
        for (int i = 0; i < spans.length(); i++) {
            JSONObject span = spans.optJSONObject(i);
            if (span == null) continue;
            double start = span.optDouble("start", Double.NaN);
            double end = span.optDouble("end", Double.NaN);
            if (Double.isNaN(start) || Double.isNaN(end) || end <= start) continue;
            String label = label(keyName, span);
            if (label.isEmpty()) continue;
            starts.add(start);
            ends.add(end);
            labels.add(label);
            disagreed.add(disagreeing(span));
        }
        if (starts.isEmpty()) return null;
        double[] when = new double[starts.size()];
        double[] until = new double[starts.size()];
        for (int i = 0; i < when.length; i++) {
            when[i] = starts.get(i);
            until[i] = ends.get(i);
        }
        return new ChordTrack(song.optString("song", appLabel), song.optString("reading", ""),
                keyName, song.optDouble("agreement", -1), when, until,
                labels.toArray(new String[0]), toBooleans(disagreed));
    }

    /**
     * What the box prints for one half bar: the key, the degree, and the chord.
     *
     * The degree is the offline side's own reading of its own notes, which is the one
     * that can be explained - so it is the one shown, and the other two are only
     * compared against it.
     */
    private static String label(String keyName, JSONObject span) {
        String chord = span.optString("template", "");
        if (chord.isEmpty()) chord = span.optString("chart", "");
        if (chord.isEmpty()) chord = span.optString("madmom", "");
        if (chord.isEmpty()) return "";
        String roman = numeral(span.optString("roman", ""));
        if (roman.isEmpty()) return chord;
        StringBuilder out = new StringBuilder();
        if (!keyName.isEmpty()) out.append(keyName).append(' ');
        return out.append(roman).append(" 级（").append(chord).append('）').toString();
    }

    /** "A# major" as the box prints it: the tonic only. */
    private static String keyName(JSONObject key) {
        if (key == null) return "";
        String tonic = key.optString("tonic", "");
        return tonic.isEmpty() ? "" : tonic + " 调";
    }

    /**
     * Whether the three readings say different things about this half bar.
     *
     * Blanks are not disagreements: the file writes an empty reading where that source
     * observed nothing, and their own note says the spellings have been normalised, so
     * two different strings really are two different chords.
     */
    private static boolean disagreeing(JSONObject span) {
        Set<String> said = new LinkedHashSet<>();
        for (String field : new String[] {"template", "chart", "madmom"}) {
            String value = span.optString(field, "").trim();
            if (!value.isEmpty()) said.add(value);
        }
        return said.size() > 1;
    }

    /** A roman degree in the numerals the box uses, keeping its case: "ii" -> "ⅱ". */
    static String numeral(String roman) {
        String clean = roman == null ? "" : roman.trim();
        if (clean.isEmpty()) return "";
        String[] plain = {"i", "ii", "iii", "iv", "v", "vi", "vii"};
        String lowered = clean.toLowerCase(Locale.US);
        for (int i = 0; i < plain.length; i++) {
            if (!plain[i].equals(lowered)) continue;
            boolean minor = clean.equals(lowered);
            String upper = "\u2160\u2161\u2162\u2163\u2164\u2165\u2166";   // Ⅰ Ⅱ Ⅲ Ⅳ Ⅴ Ⅵ Ⅶ
            String lower = "\u2170\u2171\u2172\u2173\u2174\u2175\u2176";   // ⅰ ⅱ ⅲ ⅳ ⅴ ⅵ ⅶ
            return String.valueOf(minor ? lower.charAt(i) : upper.charAt(i));
        }
        // Anything that is not one of the seven (a borrowed chord, a slash, a figure)
        // is printed as it was written rather than guessed at.
        return clean;
    }

    private static boolean[] toBooleans(List<Boolean> values) {
        boolean[] out = new boolean[values.size()];
        for (int i = 0; i < out.length; i++) out[i] = values.get(i);
        return out;
    }

    /** "…/song.ogg" -> "song", which is what the file name of the audio carries. */
    private static String stem(String fileName) {
        if (fileName == null || fileName.isEmpty()) return "";
        String name = fileName.replace('\\', '/');
        int slash = name.lastIndexOf('/');
        if (slash >= 0) name = name.substring(slash + 1);
        int dot = name.lastIndexOf('.');
        return dot > 0 ? name.substring(0, dot) : name;
    }
}
