package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

/**
 * Speaking when the harmony moves, not when the clock says so.
 *
 * A remark on a timer lands in the middle of a chord and says what that chord is
 * - which the listener can hear, so it is worth nothing. A remark at the moment
 * the chord changes is the app saying something that just happened, which is the
 * only reason to say anything at all.
 *
 * Two things have to hold before it speaks: the reading has to have changed, and
 * it has to still be the new one on the next look. The second is what keeps a
 * noisy eight seconds from producing a sentence about a chord that lasted one
 * frame.
 */
public class ChordChangeGateTest {
    private static final long GAP = 20_000;

    @Test
    public void saysNothingWhileTheChordStaysTheSame() {
        ChordChangeGate gate = new ChordChangeGate(GAP);
        gate.said(0, "G 大调 Ⅰ 级（G 大三和弦）");
        assertFalse(gate.due(60_000, true, false, "G 大调 Ⅰ 级（G 大三和弦）"));
    }

    @Test
    public void aNewChordIsHeldForOneMoreLookBeforeItIsSaid() {
        ChordChangeGate gate = new ChordChangeGate(GAP);
        gate.said(0, "G 大调 Ⅰ 级（G 大三和弦）");
        // First sighting: noted, not spoken.
        assertFalse(gate.due(30_000, true, false, "G 大调 Ⅳ 级（C 大三和弦）"));
        // Still there: now it is something that happened.
        assertTrue(gate.due(35_000, true, false, "G 大调 Ⅳ 级（C 大三和弦）"));
    }

    @Test
    public void aChordThatFlickersIsNeverSaid() {
        ChordChangeGate gate = new ChordChangeGate(GAP);
        gate.said(0, "G 大调 Ⅰ 级（G 大三和弦）");
        assertFalse(gate.due(30_000, true, false, "G 大调 Ⅳ 级（C 大三和弦）"));
        // Back to where it was: the sighting is dropped, not spoken.
        assertFalse(gate.due(32_000, true, false, "G 大调 Ⅰ 级（G 大三和弦）"));
    }

    @Test
    public void theGapStillHoldsBetweenChanges() {
        ChordChangeGate gate = new ChordChangeGate(GAP);
        gate.said(100_000, "G 大调 Ⅰ 级（G 大三和弦）");
        assertFalse(gate.due(105_000, true, false, "G 大调 Ⅳ 级（C 大三和弦）"));
        // Held for another look, but the last one was five seconds ago.
        assertFalse(gate.due(110_000, true, false, "G 大调 Ⅳ 级（C 大三和弦）"));
        assertTrue(gate.due(121_000, true, false, "G 大调 Ⅳ 级（C 大三和弦）"));
    }

    @Test
    public void silentWhileStoppedOrWhileAQuestionIsOpen() {
        ChordChangeGate gate = new ChordChangeGate(GAP);
        assertFalse(gate.due(30_000, false, false, "C 大调 Ⅰ 级（C 大三和弦）"));
        assertFalse(gate.due(30_000, true, true, "C 大调 Ⅰ 级（C 大三和弦）"));
    }

    @Test
    public void theFirstChordOfASongIsSaidOnce() {
        // Nothing to compare against yet, so the first reading is the change.
        ChordChangeGate gate = new ChordChangeGate(GAP);
        String opening = "C 大调 Ⅰ 级（C 大三和弦）";
        assertFalse(gate.due(30_000, true, false, opening));
        assertTrue(gate.due(32_000, true, false, opening));
        // The caller writes down what it said - that is what makes the next look
        // find nothing new rather than say the same chord again.
        gate.said(32_000, opening);
        assertFalse(gate.due(40_000, true, false, opening));
    }
}
