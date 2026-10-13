package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

/**
 * When the app is allowed to stop the music and ask.
 *
 * The listener's pause asks a question by itself; this is the app deciding to
 * interrupt, so the rules have to be the kind that can be argued with: one
 * observation above the line is a moment, two in a row is a change, and a
 * question too soon after the last one is not a question.
 */
public class NoveltyGateTest {
    private static final long SECOND = 1000;

    @Test
    public void oneLoudMomentIsNotAChange() {
        NoveltyGate gate = NoveltyGate.standard();
        assertFalse(gate.observe(0.5, 0));
        assertFalse("the first one only counts", gate.observe(0.05, SECOND));
        assertFalse(gate.observe(0.4, 2 * SECOND));
        assertTrue("two in a row is", gate.observe(0.4, 3 * SECOND));
    }

    @Test
    public void steadyMusicNeverAsks() {
        NoveltyGate gate = NoveltyGate.standard();
        for (int i = 0; i < 100; i++) {
            assertFalse(gate.observe(0.03, i * SECOND));
        }
        assertEquals("and the run is empty", 0, gate.run());
    }

    @Test
    public void aSecondQuestionWaitsForTheFirstOneToSettle() {
        NoveltyGate gate = NoveltyGate.standard();
        assertFalse(gate.observe(0.4, 0));
        assertTrue(gate.observe(0.4, SECOND));
        assertFalse(gate.observe(0.4, 2 * SECOND));
        assertFalse("still inside the gap: twice in a row and it says no", gate.observe(0.4, 3 * SECOND));
        assertFalse("still inside it at ten seconds", gate.observe(0.4, 13 * SECOND));
        assertTrue("past the gap it may ask again", gate.observe(0.4, 16 * SECOND));
    }

    @Test
    public void aNewTrackStartsCountingAgain() {
        NoveltyGate gate = NoveltyGate.standard();
        assertFalse(gate.observe(0.9, 0));
        assertEquals(1, gate.run());
        gate.reset();
        assertEquals(0, gate.run());
        assertFalse("the moment before a change is not half of one", gate.observe(0.9, SECOND));
    }

    /** The line is drawn where the model's own noise floor is not. */
    @Test
    public void theLineSitsAboveTheModelsOwnNoise() {
        NoveltyGate gate = NoveltyGate.standard();
        // Steady music measured around 0.05 to 0.07, with a p90 of 0.16.
        assertFalse(gate.observe(0.07, 0));
        assertEquals(0, gate.run());
    }
}
