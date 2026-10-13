package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;

import org.junit.Test;

/**
 * What the box shows while the readings underneath it are noisy.
 *
 * The rule is not politeness, it is readability: readings arrive every second and
 * the top two chords are frequently within a hundredth of each other, so a box
 * that followed them would be unreadable several times a second.
 */
public class ChordDisplayTest {
    private static final String C = "G 调 Ⅳ 级（C 大三和弦）";
    private static final String Am = "G 调 ⅵ 级（e 小三和弦）";
    private static final String G = "G 调 Ⅰ 级（G 大三和弦）";

    @Test
    public void nothingIsShownUntilTheReadingHoldsForAWhile() {
        ChordDisplay display = ChordDisplay.standard();
        assertEquals("", display.consider(C));
        assertEquals("", display.consider(C));
        assertEquals(C, display.consider(C));
        assertEquals(C, display.consider(C));
    }

    @Test
    public void aReadingThatFlickersNeverReachesTheScreen() {
        ChordDisplay display = ChordDisplay.standard();
        display.consider(C);
        display.consider(C);
        assertEquals(C, display.consider(C));
        // One second of something else, then back: the box does not move.
        assertEquals(C, display.consider(Am));
        assertEquals(C, display.consider(C));
    }

    @Test
    public void aRealChangeArrivesOnceItHasHeld() {
        ChordDisplay display = ChordDisplay.standard();
        for (int i = 0; i < 3; i++) display.consider(C);
        assertEquals(C, display.shown());
        display.consider(Am);
        display.consider(Am);
        assertEquals("a real change waits for the same agreement", C, display.shown());
        assertEquals(Am, display.consider(Am));
    }

    @Test
    public void aNearTieStillReachesTheScreenOnceItHasHeld() {
        // The first version of this required the reading to beat the runner-up by
        // 0.03 before it could replace what was up. On this app's own recordings the
        // gap is 0.005 to 0.008, so the box froze on the first chord it ever showed
        // and sat there for thirty seconds while the readings underneath moved.
        // Near-ties are the normal case here; agreement over time is the filter.
        ChordDisplay display = ChordDisplay.standard();
        for (int i = 0; i < 3; i++) display.consider(C);
        assertEquals(C, display.shown());
        // Held, despite being a hundredth ahead of the next chord.
        display.consider(Am);
        display.consider(Am);
        assertEquals(Am, display.consider(Am));
    }

    @Test
    public void aChangeWaitsForTheBarLine() {
        ChordDisplay display = ChordDisplay.standard();
        for (int i = 0; i < 3; i++) display.consider(C, true);
        assertEquals(C, display.shown());

        // The new chord has agreed - but it agreed in the middle of a bar, and a
        // chord that swaps mid-bar reads as a jump rather than as the music moving.
        assertEquals(C, display.consider(Am, false));
        assertEquals(C, display.consider(Am, false));
        assertEquals(C, display.consider(Am, false));
        // The bar line arrives, and the change lands on it.
        assertEquals(Am, display.consider(Am, true));
    }

    @Test
    public void withoutAGridEveryTickIsAMomentItMayChangeOn() {
        // The caller marks every tick as a switch point when the beat is unknown,
        // because a box that stops working whenever the grid is down is worse than
        // one that is not quantised.
        ChordDisplay display = ChordDisplay.standard();
        display.consider(C, true);
        display.consider(C, true);
        assertEquals(C, display.consider(C, true));
        display.consider(Am, true);
        display.consider(Am, true);
        assertEquals(Am, display.consider(Am, true));
    }

    @Test
    public void aCoinTossIsNotBelievedUntilItKeepsLandingTheSameWay() {
        ChordDisplay display = ChordDisplay.standard();
        for (int i = 0; i < 3; i++) display.consider(C, 0.2, true);
        assertEquals(C, display.shown());

        // Three readings is enough for a clear winner, and not enough for a tie:
        // this app's readings are ties most of the time (measured 0.002 to 0.008),
        // and three coin tosses landing the same way is not evidence.
        for (int i = 0; i < 3; i++) {
            assertEquals(C, display.consider(Am, 0.002, true));
        }
        // Six in a row is a different story.
        for (int i = 0; i < 3; i++) display.consider(Am, 0.002, true);
        assertEquals(Am, display.shown());
    }

    @Test
    public void aClearWinnerStillArrivesInThreeReadings() {
        ChordDisplay display = ChordDisplay.standard();
        for (int i = 0; i < 3; i++) display.consider(C, 0.2, true);
        display.consider(Am, 0.15, true);
        display.consider(Am, 0.15, true);
        assertEquals(Am, display.consider(Am, 0.15, true));
    }

    @Test
    public void theBoxGoesQuietWhenTheReadingIsLost() {
        ChordDisplay display = ChordDisplay.standard();
        for (int i = 0; i < 3; i++) display.consider(C);
        assertEquals(C, display.shown());
        assertEquals(C, display.consider(""));
        assertEquals("", display.consider(""));
    }
}
