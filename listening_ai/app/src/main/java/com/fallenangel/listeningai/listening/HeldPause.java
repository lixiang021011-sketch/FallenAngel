package com.fallenangel.listeningai.listening;

/**
 * Whether a pause that was held under our own question can still be asked about.
 *
 * The listener's pause is the one moment this app has that nobody had to be asked
 * for. When it arrives while a question is already on screen there is nothing to
 * do but remember it - a second card on top of the first would be the app talking
 * over itself. But remembering is not the same as keeping it forever: a pause from
 * two minutes ago is not what the listener is thinking about now, and a question
 * about it would be the app asking about its own backlog.
 *
 * So: wait while the card is busy, ask once it is clear, and let it go when it is
 * no longer that moment. The moment itself is already written into the record
 * either way, which is what makes letting it go safe.
 *
 * Kept away from Android because it is three conditions and a clock, and rules
 * like that are worth reading in one place.
 */
public final class HeldPause {
    public enum Decision { WAIT, ASK, TOO_OLD }

    private final long patienceMillis;

    public HeldPause(long patienceMillis) {
        this.patienceMillis = Math.max(0, patienceMillis);
    }

    public Decision decide(long nowMillis, long heldAtMillis, boolean cardIsAsking, boolean musicHeld) {
        if (cardIsAsking || musicHeld) return Decision.WAIT;
        if (nowMillis - heldAtMillis > patienceMillis) return Decision.TOO_OLD;
        return Decision.ASK;
    }
}
