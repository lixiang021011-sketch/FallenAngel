package com.fallenangel.listeningai.listening;

/**
 * When the app may say something without being asked.
 *
 * The questions have a budget: a gate, a minimum gap, and a bank that cannot
 * pick twice within four bars. Remarks had none, because until now the app had no
 * way to speak that was not either a question or an excuse for not asking one.
 *
 * Whether a person wants the app talking while they listen is not a question this
 * class can answer, and the honest version of it is not a design decision at all
 * - it is the number they would choose after hearing it. So the amount of talking
 * is one number, the gap, and it is meant to be turned: ten seconds is a
 * companion, three minutes is a machine that says something now and then.
 *
 * Two things it will never do, whatever the gap: talk over a question nobody has
 * answered, and talk when the music is not playing. Those are not tuning, they
 * are the difference between talking to somebody and talking at them.
 *
 * Kept away from Android so the rule is testable without listening to music for
 * an hour - a policy nobody can try is a policy nobody will get right.
 */
public final class RemarkPolicy {
    private final long gapMillis;
    private long lastSpokeMillis = Long.MIN_VALUE / 2;

    public RemarkPolicy(long gapMillis) {
        this.gapMillis = Math.max(0, gapMillis);
    }

    /**
     * @param musicPlaying     whether anything is being heard right now
     * @param someoneIsWaiting whether a question is on screen and unanswered
     */
    public boolean due(long nowMillis, boolean musicPlaying, boolean someoneIsWaiting) {
        if (!musicPlaying) return false;
        if (someoneIsWaiting) return false;
        return nowMillis - lastSpokeMillis >= gapMillis;
    }

    /** It spoke just now. */
    public void spoke(long nowMillis) {
        lastSpokeMillis = nowMillis;
    }

    /** How much talking this is set to. */
    public long gapMillis() {
        return gapMillis;
    }
}
