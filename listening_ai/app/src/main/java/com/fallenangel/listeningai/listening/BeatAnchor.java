package com.fallenangel.listeningai.listening;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * Two buttons: the bar head, and the rest of the beats.

 * This shape gets all three things a grid needs out of one gesture, and gets each
 * of them the way a person is actually good at it. The tempo is the interval
 * between presses. The downbeat is the press the listener said was the downbeat,
 * rather than something inferred - inferring it is what put the bar line on the
 * wrong beat, because every phase fits the onsets equally well. The meter is a
 * *count* of the beats between two bar heads rather than a division: the division
 * needed a trustworthy beat to divide by, and the beat in question was the grid's.

 * A half bar then costs nothing: it is one bar that counted short, and the meter
 * is the count used most often. That is also how a person keeps time through one -
 * they do not stop and work out what happened, they keep counting.

 * No restart rule here, unlike tap tempo: an anchor is a short deliberate act that
 * the listener ends by pressing 完成, not something left running unattended.
 */
public final class BeatAnchor {
    /** Which button a press was. */
    public enum Kind {
        /** The first beat of a bar. */
        DOWNBEAT,
        /** Any other beat. */
        BEAT
    }

    /** A bar's worth of counting before the meter is worth trusting. */
    private static final int MIN_BARS = 2;
    private static final int MIN_PRESSES = 4;
    /** A bar of one beat is not a meter: that is somebody pressing one button. */
    private static final int MIN_BEATS_PER_BAR = 2;
    private static final int MAX_BEATS_PER_BAR = 8;

    private final List<Kind> kinds = new ArrayList<>();
    private final List<Long> audioMillis = new ArrayList<>();
    private final List<Long> clientMillis = new ArrayList<>();

    /** One press, with both clocks: ours for the music, theirs for the record. */
    public void tap(Kind kind, long audio, long client) {
        kinds.add(kind == null ? Kind.BEAT : kind);
        audioMillis.add(audio);
        clientMillis.add(client);
    }

    /** Whether there is a bar's worth of counting to go on. */
    public boolean enough() {
        return bars().size() >= MIN_BARS && pressCount() >= MIN_PRESSES && meter() > 0;
    }

    /**
     * The tempo, from the gaps between presses.

     * Every pair of consecutive presses is one beat apart by definition - the
     * listener pressed on beats - so the median gap is the beat, whoever's button
     * the two presses were.
     */
    public double beatBpm() {
        double median = medianInterval();
        return median <= 0 ? 0 : 60_000.0 / median;
    }

    /** How many beats the listener counted in a bar, or 0 when they never said. */
    public int meter() {
        List<Integer> counts = bars();
        if (counts.isEmpty()) return 0;
        Map<Integer, Integer> timesSeen = new HashMap<>();
        int best = 0, bestSeen = 0;
        for (int count : counts) {
            int seen = timesSeen.merge(count, 1, Integer::sum);
            if (seen > bestSeen || (seen == bestSeen && count > best)) {
                bestSeen = seen;
                best = count;
            }
        }
        return best >= MIN_BEATS_PER_BAR && best <= MAX_BEATS_PER_BAR ? best : 0;
    }

    /**
     * Where the listener put the last bar head of this attempt.

     * The last one and not the first, because it is the closest to the moment they
     * stop tapping and therefore to where the music is now: the phase is then
     * pinned at a place the grid has just been following, and whatever drift their
     * hand had over two bars never gets applied to it at all.
     */
    public long lastDownbeatMillis() {
        for (int i = kinds.size() - 1; i >= 0; i--) {
            if (kinds.get(i) == Kind.DOWNBEAT) return audioMillis.get(i);
        }
        return -1;
    }

    public int pressCount() {
        return kinds.size();
    }

    public long pressAt(int index) {
        return index >= 0 && index < audioMillis.size() ? audioMillis.get(index) : -1;
    }

    public long clientTimeAt(int index) {
        return index >= 0 && index < clientMillis.size() ? clientMillis.get(index) : -1;
    }

    public Kind kindAt(int index) {
        return index >= 0 && index < kinds.size() ? kinds.get(index) : Kind.BEAT;
    }

    /** Presses per bar, one entry for each pair of consecutive bar heads. */
    private List<Integer> bars() {
        List<Integer> counts = new ArrayList<>();
        int previous = -1;
        for (int i = 0; i < kinds.size(); i++) {
            if (kinds.get(i) != Kind.DOWNBEAT) continue;
            if (previous >= 0) counts.add(i - previous);
            previous = i;
        }
        return counts;
    }

    private double medianInterval() {
        int count = audioMillis.size();
        if (count < 2) return 0;
        double[] intervals = new double[count - 1];
        for (int i = 1; i < count; i++) {
            intervals[i - 1] = audioMillis.get(i) - audioMillis.get(i - 1);
        }
        Arrays.sort(intervals);
        return intervals[intervals.length / 2];
    }
}
