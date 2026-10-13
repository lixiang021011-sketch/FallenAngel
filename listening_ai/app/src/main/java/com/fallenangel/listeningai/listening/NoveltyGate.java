package com.fallenangel.listeningai.listening;

/**
 * When the app may stop the music to ask.
 *
 * The listener's own pause is a signal that arrives by itself, and the app can
 * simply answer it. Asking on its own is a decision, so it needs rules: one
 * observation above the line is a moment, two in a row is a change, and a
 * question asked too soon after the last one is an interruption rather than a
 * question.
 *
 * The numbers here are the ones the first real model produces - it calls steady
 * music 0.02 to 0.05 and a track switch 0.25 - so the line sits above the noise
 * floor of the model itself rather than at a number that sounded plausible.
 *
 * Kept away from Android on purpose: a trigger policy that can only be tried by
 * listening to music for an hour is a policy nobody will tune.
 */
public final class NoveltyGate {
    private final double threshold;
    private final int needed;
    private final long gapMillis;
    private int runs;
    private long lastAskedMillis = Long.MIN_VALUE / 2;

    public NoveltyGate(double threshold, int needed, long gapMillis) {
        this.threshold = threshold;
        this.needed = Math.max(1, needed);
        this.gapMillis = gapMillis;
    }

    /**
     * What the first real model needed: 0.20 twice in a row, a minute apart.
     *
     * Those come from what the model reported over four tracks: steady music
     * sits around 0.05 to 0.07, the ninetieth percentile is 0.09 to 0.16, and
     * the peaks at real changes run 0.17 to 0.41. The first attempt at 0.20 was
     * above most of those peaks, which is how a trigger exists without ever
     * firing; 0.15 with a minute between questions fired twice in twenty
     * minutes, which is quieter than the app itself is interesting. So: just
     * above ordinary variation, and a quarter of a minute apart - the listener
     * asked for fifteen seconds, having heard how the longer gaps felt.
     */
    public static NoveltyGate standard() {
        return new NoveltyGate(0.12, 2, 15_000);
    }

    /**
     * One observation, and whether this is the one to ask about.
     *
     * A run of quiet moments clears the count: a change is a change, not two
     * unrelated pokes above a line.
     */
    public boolean observe(double novelty, long nowMillis) {
        if (!(novelty >= threshold)) {
            runs = 0;
            return false;
        }
        runs++;
        if (runs < needed) return false;
        if (nowMillis - lastAskedMillis < gapMillis) return false;
        runs = 0;
        lastAskedMillis = nowMillis;
        return true;
    }

    /** A new track: what was novel about the old one says nothing about this one. */
    public void reset() {
        runs = 0;
    }

    /** How many observations in a row have been above the line. */
    public int run() {
        return runs;
    }

    /** How many in a row it takes. */
    public int required() {
        return needed;
    }
}
