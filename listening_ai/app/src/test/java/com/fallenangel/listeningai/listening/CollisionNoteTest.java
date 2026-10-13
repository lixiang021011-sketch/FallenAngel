package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

/**
 * What to say when the listener and the app stopped on the same moment.
 *
 * The app asks when the music changes; the listener pauses when something
 * matters to them. When those land together, the app used to say nothing about
 * it - one of the two triggers was simply dropped, and the most interesting
 * thing that had just happened (that both of them noticed the same second) was
 * the one thing not recorded.
 */
public class CollisionNoteTest {
    private static final long SECOND = 1000;
    private static final long T = 1_700_000_000_000L;

    @Test
    public void theSameMomentIsSaidOutLoud() {
        String note = CollisionNote.of(T, T + 800);
        assertTrue("says so: " + note, note.contains("也"));
        assertTrue("and when: " + note, note.contains("正好"));
    }

    @Test
    public void eitherOrderCounts() {
        assertEquals(CollisionNote.of(T, T + 2 * SECOND),
                CollisionNote.of(T + 2 * SECOND, T));
    }

    @Test
    public void farApartIsNotACollision() {
        assertEquals("", CollisionNote.of(T, T + 30 * SECOND));
        assertEquals("", CollisionNote.of(T - 30 * SECOND, T));
    }

    @Test
    public void nothingNoticedMeansNothingToSay() {
        assertEquals("", CollisionNote.of(0, T));
        assertEquals("", CollisionNote.of(Long.MIN_VALUE / 2, T));
    }
}
