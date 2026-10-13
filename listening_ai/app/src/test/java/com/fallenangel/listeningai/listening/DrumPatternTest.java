package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertTrue;

import org.junit.Test;

/**
 * Whether the drums agree with the bar the app is counting in.
 *
 * This is the check that a tempo cannot make: the beat is right in all of these
 * profiles - the kick and the snare both land on beats - and what differs is
 * whether beat one is where the kick says it is. A grid can be perfectly in time
 * and still have the bar line on the wrong beat (measured: 6/4 was named where the
 * music was four, and half the flashes still looked right, because six beats land
 * on beat one and then beat three, over and over).
 *
 * The scale is deliberately three-valued: near one means the drums agree, near
 * zero means they disagree, and about a half means there is no drum pattern to
 * tell - which is not the same as agreeing.
 */
public class DrumPatternTest {
    /** Four beats, kick on one and three, snare on two and four. */
    @Test
    public void theCommonPopPatternAgrees() {
        double[] kick = {1.0, 0.1, 0.8, 0.1};
        double[] snare = {0.1, 0.9, 0.1, 0.8};
        assertTrue("agrees: " + DrumPattern.score(kick, snare),
                DrumPattern.score(kick, snare) > 0.7);
    }

    /** The same drums, counted one beat out: the snare sits on the downbeat. */
    @Test
    public void aBarLineOneBeatLateDisagrees() {
        double[] kick = {0.1, 0.8, 0.1, 1.0};
        double[] snare = {0.9, 0.1, 0.8, 0.1};
        assertTrue("disagrees: " + DrumPattern.score(kick, snare),
                DrumPattern.score(kick, snare) < 0.3);
    }

    /** A waltz: kick on one, snare on two and three. */
    @Test
    public void aWaltzAgreesInThree() {
        double[] kick = {1.0, 0.2, 0.2};
        double[] snare = {0.1, 0.9, 0.8};
        assertTrue("agrees: " + DrumPattern.score(kick, snare),
                DrumPattern.score(kick, snare) > 0.7);
    }

    /** Nothing to go on is not agreement. */
    @Test
    public void noDrumsIsNoEvidence() {
        assertTrue(DrumPattern.score(new double[] {0, 0, 0, 0}, new double[] {0, 0, 0, 0}) == 0.0);
        double flat = DrumPattern.score(new double[] {1, 1, 1, 1}, new double[] {1, 1, 1, 1});
        assertTrue("inconclusive sits in the middle: " + flat,
                Math.abs(flat - DrumPattern.NO_EVIDENCE) < 0.05);
    }

    /** Mismatched or nonsense input is refused rather than scored. */
    @Test
    public void shapesThatMakeNoSenseAreRefused() {
        assertTrue(DrumPattern.score(new double[] {1, 0}, new double[] {0, 1, 0}) == 0.0);
        assertTrue(DrumPattern.score(new double[] {1}, new double[] {1}) == 0.0);
        assertTrue(DrumPattern.score(null, null) == 0.0);
    }
}
