package com.fallenangel.listeningai.listening;

/**
 * Saying so when the listener and the app stopped on the same second.
 *
 * Kept as a function of two times so the rule can be read and tested: how close
 * counts as together, and what the app says about it. The wording lives here
 * rather than at the call site because there are two directions to it - the
 * listener paused and the app had just noticed, or the app asked and the
 * listener paused a moment later - and both should say the same thing.
 */
public final class CollisionNote {
    /** How close counts as the same moment. */
    public static final long NEAR_MILLIS = 5_000;

    private CollisionNote() {
    }

    /**
     * The note, or an empty string when there is nothing to report.
     *
     * @param noticedMillis when the model said it heard a change, or 0
     * @param otherMillis   the listener's pause, or the app's question
     */
    public static String of(long noticedMillis, long otherMillis) {
        if (noticedMillis <= 0 || otherMillis <= 0) return "";
        if (Math.abs(otherMillis - noticedMillis) > NEAR_MILLIS) return "";
        return "这一刻我也正好听到了变化。";
    }
}
