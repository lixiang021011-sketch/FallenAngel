package com.fallenangel.listeningai.listening;

/**
 * Whether the drums agree with the bar the app is counting in.
 *
 * A tempo cannot answer this: in every profile it judges, the kick and the snare
 * both land on beats, and what differs is only *which* beat the bar starts on. A
 * grid can be perfectly in time and still have its bar line on the wrong beat -
 * measured, 6/4 was named where the music was four, and half the flashes still
 * looked right, because six beats land on beat one and then on beat three.
 *
 * Two things are checked, both of them conventions of popular music rather than
 * laws: the downbeat carries the most kick, and the snare is somewhere other than
 * the downbeat. Anything else - no drums, or drums on every beat equally - is not
 * a disagreement, it is an absence of evidence, and the score says so by sitting
 * in the middle.
 */
public final class DrumPattern {
    /** What to report when the drums say nothing either way. */
    public static final double NO_EVIDENCE = 0.5;
    /** The longest bar this can judge; beyond it a "pattern" is a guess. */
    private static final int MAX_BEATS = 8;

    private DrumPattern() {
    }

    /**
     * @param kick  kick energy per beat of the bar, first beat first
     * @param snare snare energy per beat of the same bar
     * @return 1 for "the drums say beat one is here", 0 for "they say it is not",
     *         {@link #NO_EVIDENCE} when there is no pattern to speak of
     */
    public static double score(double[] kick, double[] snare) {
        if (kick == null || snare == null) return 0;
        if (kick.length != snare.length) return 0;
        if (kick.length < 2 || kick.length > MAX_BEATS) return 0;
        double total = 0;
        for (int beat = 0; beat < kick.length; beat++) total += kick[beat] + snare[beat];
        if (total <= 0) return 0;
        double kickOnTheDownbeat = prominence(kick, 0);
        double snareAwayFromTheDownbeat = 1 - prominence(snare, 0);
        return Math.max(0, Math.min(1,
                (kickOnTheDownbeat + snareAwayFromTheDownbeat) / 2));
    }

    /**
     * How much one beat's energy stands out above the average of the others.

     * Zero as soon as it is not above them, which is what makes a flat profile
     * score nothing at all rather than scoring well for being even.
     */
    static double prominence(double[] perBeat, int beat) {
        if (beat < 0 || beat >= perBeat.length) return 0;
        double mine = perBeat[beat];
        if (mine <= 0) return 0;
        double rest = 0;
        for (int i = 0; i < perBeat.length; i++) {
            if (i != beat) rest += perBeat[i];
        }
        double meanOfOthers = rest / (perBeat.length - 1);
        if (mine <= meanOfOthers) return 0;
        return Math.min(1, (mine - meanOfOthers) / mine);
    }
}
