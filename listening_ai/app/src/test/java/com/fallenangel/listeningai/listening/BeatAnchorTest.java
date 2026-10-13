package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

/**
 * Two buttons: the bar head, and the rest of the beats.
 *
 * This is the shape that gets all three things out of one gesture, and gets each
 * of them by the method a person is actually good at. The tempo is the interval
 * between presses. The downbeat is the press the listener said was the downbeat -
 * not something inferred, which is what put the bar line on the wrong beat. The
 * meter is a *count* of the beats between two bar heads, not a division: the
 * division needed a trustworthy beat to divide by, and the beat under suspicion
 * was the grid's.
 *
 * A half bar then costs nothing: it is one bar that counted short, and the meter
 * is whichever count the listener used most.
 */
public class BeatAnchorTest {
    private static BeatAnchor pressed(BeatAnchor anchor, long start, int... pattern) {
        // pattern: 1 means the bar-head button, 0 means the beat button, 500ms apart.
        for (int i = 0; i < pattern.length; i++) {
            anchor.tap(pattern[i] == 1 ? BeatAnchor.Kind.DOWNBEAT : BeatAnchor.Kind.BEAT,
                    start + i * 500, start + i * 500);
        }
        return anchor;
    }

    /** Downbeat, three beats, downbeat, three beats: four beats to the bar. */
    @Test
    public void fourBeatsToTheBar() {
        BeatAnchor anchor = pressed(new BeatAnchor(), 0,
                1, 0, 0, 0, 1, 0, 0, 0, 1);
        assertTrue(anchor.enough());
        assertEquals(120.0, anchor.beatBpm(), 0.5);
        assertEquals(4, anchor.meter());
        assertEquals("the bar head is the last one they marked", 4_000,
                anchor.lastDownbeatMillis());
    }

    /** Downbeat, two beats, downbeat: three to the bar, and the same code path. */
    @Test
    public void threeBeatsToTheBar() {
        BeatAnchor anchor = pressed(new BeatAnchor(), 1_000,
                1, 0, 0, 1, 0, 0, 1);
        assertTrue(anchor.enough());
        assertEquals(3, anchor.meter());
        assertEquals(4_000, anchor.lastDownbeatMillis());
    }

    /** A half bar is one short bar, not a new meter. */
    @Test
    public void aHalfBarDoesNotChangeTheMeter() {
        BeatAnchor anchor = pressed(new BeatAnchor(), 0,
                1, 0, 0, 0, 1, 0, 1, 0, 0, 0, 1);
        assertEquals("the count used most is four", 4, anchor.meter());
    }

    /** Bar heads alone say nothing about how many beats a bar has. */
    @Test
    public void barHeadsWithoutBeatsAreNotAMeter() {
        BeatAnchor anchor = pressed(new BeatAnchor(), 0, 1, 1, 1);
        assertFalse(anchor.enough());
        assertEquals(0, anchor.meter());
        assertEquals("but the tempo of the presses is still there",
                120.0, anchor.beatBpm(), 0.5);
    }

    /** Every press is kept, with which button it was, so it can be re-read later. */
    @Test
    public void everyPressIsKeptWithItsKind() {
        BeatAnchor anchor = pressed(new BeatAnchor(), 0, 1, 0, 0, 0, 1);
        assertEquals(5, anchor.pressCount());
        assertEquals(BeatAnchor.Kind.DOWNBEAT, anchor.kindAt(0));
        assertEquals(BeatAnchor.Kind.BEAT, anchor.kindAt(1));
        assertEquals(2_000, anchor.pressAt(4));
    }
}
