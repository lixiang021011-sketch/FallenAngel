package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;

import org.junit.Test;

/**
 * The pause that arrived under our own question: when it can still be asked about.
 *
 * The moment is written into the record the instant it happens, so nothing here is
 * about not losing it - it is about not asking about it at the wrong time, which
 * is the difference between a question about the music and a question about the
 * app's backlog.
 */
public class HeldPauseTest {
    private static final long PATIENCE = 45_000;

    @Test
    public void waitedOutWhileTheCardIsStillAsking() {
        HeldPause held = new HeldPause(PATIENCE);
        assertEquals(HeldPause.Decision.WAIT,
                held.decide(10_000, 5_000, true, false));
    }

    @Test
    public void waitedOutWhileTheMusicIsStillBeingHeldForAQuestion() {
        HeldPause held = new HeldPause(PATIENCE);
        assertEquals(HeldPause.Decision.WAIT,
                held.decide(10_000, 5_000, false, true));
    }

    @Test
    public void askedOnceTheCardIsClearAndItIsStillRecent() {
        HeldPause held = new HeldPause(PATIENCE);
        assertEquals(HeldPause.Decision.ASK,
                held.decide(10_000, 5_000, false, false));
    }

    @Test
    public void letGoWhenItIsNoLongerThatMoment() {
        HeldPause held = new HeldPause(PATIENCE);
        assertEquals(HeldPause.Decision.TOO_OLD,
                held.decide(60_000, 5_000, false, false));
        // Right on the boundary it still counts as the same moment.
        assertEquals(HeldPause.Decision.ASK,
                held.decide(5_000 + PATIENCE, 5_000, false, false));
    }
}
