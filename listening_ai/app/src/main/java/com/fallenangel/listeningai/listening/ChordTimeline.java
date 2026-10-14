package com.fallenangel.listeningai.listening;

import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

/**
 * A song's chords as they were heard last time, ready to be read ahead of the music.
 *
 * Every listen already writes a line a second into chords.jsonl - the reading, its
 * two shares, and where in the song it was taken. That is a measurement of the song
 * rather than of the listen, so the second time through, the app does not have to
 * work anything out: it can look the answer up, a second or two before the music
 * gets there.
 *
 * Which is the only way to remove the delay at all. A reader listening to the
 * present cannot know the chord of the present: the window has to fill before the
 * reading moves, and that lag is built into measuring. Knowing the answer in advance
 * is not a trick on top of the measurement, it is a different position relative to
 * the music - the difference between hearing and having heard.
 *
 * Looked ahead by {@link #LOOK_AHEAD_SECONDS}, because what was recorded was
 * already late: the reading written at a second reflects the window that ended
 * there, so the chord that *started* there is the one recorded a little later.
 *
 * Kept away from Android, and away from files: it is a list of readings by time.
 */
public final class ChordTimeline {
    /**
     * How far ahead to read, in seconds of song time.
     *
     * The size of the lag the recording itself has: a 1.2 second window plus the
     * agreement that makes a reading count. Deliberately one number rather than a
     * derivation - if it is wrong it is wrong in a way that can be seen and
     * corrected, which a cleverer estimate would hide.
     */
    public static final double LOOK_AHEAD_SECONDS = 1.2;

    /**
     * How long one reading stands for.
     *
     * A reading taken at 30s says what the chord was at 30s, not what it is at 45s.
     * Records have holes - a window too noisy to read, a stretch where the harmony
     * could not be named, a listen that was cut off - and filling a hole by holding
     * the last reading is how the box came to sit on one chord while the music moved:
     * measured 2026-10-14, 雲と幽霊 26.3s-78.7s, every line written `from=cache` and
     * `unreadable`, the box showing "D 调 Ⅰ 级" for fifty-two seconds. Past this the
     * timeline says nothing, and a caller that must say something measures instead.
     */
    public static final double MAX_HOLD_SECONDS = 3.0;

    private final double[] times;
    private final String[] readings;

    private ChordTimeline(double[] times, String[] readings) {
        this.times = times;
        this.readings = readings;
    }

    /** How many readings are in it. */
    public int size() {
        return times.length;
    }

    /**
     * Whether it is worth using.
     *
     * A handful of readings is a fragment - a song that was skipped, or a listen cut
     * short - and a fragment used as a cache would show nonsense for the rest of the
     * song, which is worse than measuring it, because it looks confident.
     */
    public boolean useful() {
        return times.length >= 8;
    }

    /**
     * Whether the timeline still has something to say about this point of the song.
     *
     * A listen that was cut short leaves a timeline that stops half way, and a
     * caller that kept reading it would keep showing the last chord it had for the
     * rest of the song - measured 2026-10-13: "半首过后怎么不动了". Past the end the
     * caller has to go back to measuring, which is what it did before there was any
     * cache at all.
     */
    public boolean covers(double seconds) {
        return indexAt(seconds + LOOK_AHEAD_SECONDS) >= 0;
    }

    /** The chord the app would have shown at this point of the song. */
    public String at(double seconds) {
        int found = indexAt(seconds + LOOK_AHEAD_SECONDS);
        return found < 0 ? "" : readings[found];
    }

    /**
     * The reading that still stands at this point, or -1 when none does.
     *
     * Nothing before the first reading (it describes its own moment, not the one
     * before it) and nothing after the last one by more than the look-ahead.
     */
    private int indexAt(double wanted) {
        if (times.length == 0 || wanted < times[0]) return -1;
        // The readings arrive in order; a scan is enough for a few hundred of them.
        int found = 0;
        for (int i = 0; i < times.length; i++) {
            if (times[i] <= wanted) found = i;
            else break;
        }
        return wanted - times[found] <= MAX_HOLD_SECONDS ? found : -1;
    }

    /**
     * Read a session's chords.jsonl back.
     *
     * Lines that make no sense are skipped rather than failing the whole file: a
     * partly readable cache is still worth having, and a song that cannot be read
     * back is simply measured again, which is what the app did before any of this.
     */
    public static ChordTimeline from(List<String> lines) {
        List<double[]> times = new ArrayList<>();
        List<String> readings = new ArrayList<>();
        collect(lines, times, readings, new java.util.HashSet<>());
        return build(times, readings);
    }

    /**
     * Several listens of one song, stitched into one timeline.
     *
     * Because listening is not a single act: a song gets started, skipped, returned
     * to, played again the next day. Each of those leaves a partial record, and any
     * one of them on its own is "too little to reuse" - measured 2026-10-13, a
     * previous listen of nine seconds was refused and the cache did nothing. Read
     * together they cover the song.
     *
     * Where two listens both have a second, the earlier list wins: the caller passes
     * them newest first, so the newest reading of each moment is the one kept, and an
     * older listen only fills the gaps.
     */
    public static ChordTimeline merge(List<List<String>> listensNewestFirst) {
        List<double[]> times = new ArrayList<>();
        List<String> readings = new ArrayList<>();
        java.util.Set<Long> taken = new java.util.HashSet<>();
        if (listensNewestFirst != null) {
            for (List<String> listen : listensNewestFirst) collect(listen, times, readings, taken);
        }
        return build(times, readings);
    }

    /** One listen's lines, keeping only the seconds nothing newer has covered. */
    private static void collect(List<String> lines, List<double[]> times, List<String> readings,
                                java.util.Set<Long> taken) {
        if (lines == null) return;
        for (String line : lines) {
            if (line == null || line.isEmpty()) continue;
            try {
                JSONObject record = new JSONObject(line);
                String reading = record.optString("reading", "");
                if (reading.isEmpty()) continue;
                double at = record.optDouble("songTime", -1);
                if (at < 0) continue;
                long second = Math.round(at);
                if (!taken.add(second)) continue;
                times.add(new double[] {at});
                readings.add(reading);
            } catch (Exception unusable) {
                // One bad line is not a reason to lose the rest.
            }
        }
    }

    private static ChordTimeline build(List<double[]> times, List<String> readings) {
        // Written newest-first above or read in order from one file, the result has
        // to be sorted by time before it can be read at a point.
        List<Integer> order = new ArrayList<>();
        for (int i = 0; i < times.size(); i++) order.add(i);
        order.sort((a, b) -> Double.compare(times.get(a)[0], times.get(b)[0]));
        double[] when = new double[times.size()];
        String[] said = new String[times.size()];
        for (int i = 0; i < when.length; i++) {
            when[i] = times.get(order.get(i))[0];
            said[i] = readings.get(order.get(i));
        }
        return new ChordTimeline(when, said);
    }
}
