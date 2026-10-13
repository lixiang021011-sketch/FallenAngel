package com.fallenangel.listeningai.listening;

/**
 * When the app may speak about the harmony: when it moves, and only then.
 *
 * A remark on a timer lands in the middle of a chord and names it. That is worth
 * nothing - the listener is hearing the chord at that moment, and naming what
 * somebody is already hearing is the definition of noise. A remark at the moment
 * the chord changes is the app saying something that just happened, which is the
 * only kind of thing worth interrupting for.
 *
 * So the trigger is the change itself, with two guards. A reading has to repeat
 * before it counts, because eight seconds of a real mix produce single-window
 * readings that do not last; and a minimum gap still applies, because a song that
 * really does change chord every bar would otherwise be described bar by bar.
 *
 * The same rule applies to the first chord of a song: nothing to compare against
 * means the opening reading is itself a change, said once and then held.
 *
 * Kept away from Android so the rule can be tried in a test instead of by
 * listening to a song with a notebook.
 */
public final class ChordChangeGate {
    private final long gapMillis;
    private String lastSaid = null;
    private String pending = null;
    private long lastSaidMillis = Long.MIN_VALUE / 2;

    public ChordChangeGate(long gapMillis) {
        this.gapMillis = Math.max(0, gapMillis);
    }

    /**
     * @param playing     whether anything is being heard right now
     * @param waiting     whether a question is on screen and unanswered
     * @param reading     the chord as the card would print it, or null if none
     */
    public boolean due(long nowMillis, boolean playing, boolean waiting, String reading) {
        if (!playing || waiting) return false;
        if (reading == null || reading.isEmpty()) return false;
        if (reading.equals(lastSaid)) {
            pending = null;
            return false;
        }
        if (!reading.equals(pending)) {
            // Seen once. A chord that is still there next look is a change.
            pending = reading;
            return false;
        }
        if (nowMillis - lastSaidMillis < gapMillis) return false;
        return true;
    }

    /** It said this one. */
    public void said(long nowMillis, String reading) {
        lastSaid = reading;
        pending = null;
        lastSaidMillis = nowMillis;
    }

    /** A new song starts over: the first chord of it is a change again. */
    public void reset() {
        lastSaid = null;
        pending = null;
        lastSaidMillis = Long.MIN_VALUE / 2;
    }
}
