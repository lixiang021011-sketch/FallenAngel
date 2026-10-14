package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

/**
 * Reading a song's chords back, and reading them ahead of the music.
 *
 * The point of the whole thing is the look-ahead: a recording made while listening
 * is already late by its own window, so replaying it verbatim would reproduce the
 * delay it was supposed to remove.
 */
public class ChordTimelineTest {
    private static String line(double atSeconds, String reading) {
        return "{\"songTime\":" + atSeconds + ",\"reading\":\"" + reading + "\"}";
    }

    /** A record as one is actually written: a reading every second. */
    private static ChordTimeline some() {
        List<String> lines = new ArrayList<>();
        for (int i = 0; i <= 9; i++) lines.add(line(i, "C 调 Ⅰ 级"));
        for (int i = 10; i <= 20; i++) lines.add(line(i, "C 调 Ⅳ 级"));
        return ChordTimeline.from(lines);
    }

    @Test
    public void readsBackWhatWasWritten() {
        assertEquals(21, some().size());
    }

    @Test
    public void aFragmentIsNotWorthUsing() {
        ChordTimeline fragment = ChordTimeline.from(Arrays.asList(line(1.0, "C 调 Ⅰ 级")));
        assertFalse(fragment.useful());

        List<String> many = new ArrayList<>();
        for (int i = 0; i < 25; i++) many.add(line(i, "C 调 Ⅰ 级"));
        assertTrue(ChordTimeline.from(many).useful());
    }

    @Test
    public void theChangeIsShownBeforeTheMusicReachesIt() {
        ChordTimeline timeline = some();
        // The change is written at ten seconds, and a reading written at ten seconds
        // already describes the window that ended there. Read verbatim, the box would
        // change at ten; read ahead, the new chord arrives at the point the music
        // actually changes.
        assertEquals("C 调 Ⅳ 级", timeline.at(9.5));
        assertEquals("C 调 Ⅰ 级", timeline.at(8.0));
    }

    @Test
    public void beforeTheFirstReadingItSaysNothing() {
        // A listen that started in the middle of a song has its first reading in the
        // middle: that reading describes its own moment, not the one before it.
        assertEquals("", some().at(-30));
        assertEquals("", ChordTimeline.from(Arrays.asList(
                line(60.0, "C 调 Ⅰ 级"), line(61.0, "C 调 Ⅰ 级"))).at(20));
    }

    @Test
    public void afterTheLastReadingItSaysNothing() {
        // Not the last chord held for the rest of the song: a record that stops at
        // twenty seconds says nothing about a minute in.
        assertEquals("", some().at(600));
    }

    @Test
    public void pastTheLastReadingItSaysItHasNothingLeft() {
        ChordTimeline timeline = some();      // one reading a second, 0 to 20
        assertTrue(timeline.covers(0));
        assertTrue(timeline.covers(18));
        assertTrue("the look-ahead reaches the last reading from here", timeline.covers(19));
        assertFalse("a listen cut short must not be read past its end", timeline.covers(25));
    }

    @Test
    public void aHoleInTheMiddleIsNotHeldOver() throws Exception {
        // The real shape of the bug (雲と幽霊, 2026-10-14): the earlier listens left
        // readings for the first seconds and again much later, and the box sat on one
        // chord for fifty-two seconds while the music went on.
        List<String> lines = new ArrayList<>();
        for (int i = 0; i <= 5; i++) lines.add(line(i, "D 调 Ⅰ 级"));
        for (int i = 60; i <= 65; i++) lines.add(line(i, "D 调 Ⅳ 级"));
        ChordTimeline stitched = ChordTimeline.from(lines);
        assertEquals(12, stitched.size());
        assertEquals("D 调 Ⅰ 级", stitched.at(2));
        assertTrue(stitched.covers(2));
        assertEquals("这里上一遍没记录，不该拿旧和弦顶着", "", stitched.at(30));
        assertFalse(stitched.covers(30));
        // A short gap is still a gap, but one reading stands a moment past itself.
        assertEquals("D 调 Ⅰ 级", stitched.at(5.5));
    }

    @Test
    public void severalShortListensStitchIntoOne() {
        // The real case: started, skipped, come back. Neither listen covers the song
        // and each on its own was refused as too little.
        List<String> first = Arrays.asList(
                line(0.0, "C 调 Ⅰ 级"), line(1.0, "C 调 Ⅰ 级"), line(2.0, "C 调 Ⅰ 级"));
        List<String> second = Arrays.asList(
                line(60.0, "C 调 Ⅳ 级"), line(61.0, "C 调 Ⅳ 级"), line(62.0, "C 调 Ⅳ 级"));
        ChordTimeline stitched = ChordTimeline.merge(Arrays.asList(second, first));

        assertEquals(6, stitched.size());
        assertEquals("C 调 Ⅰ 级", stitched.at(0));
        assertEquals("C 调 Ⅳ 级", stitched.at(60));
        assertTrue(stitched.covers(0));
        assertTrue(stitched.covers(60));
    }

    @Test
    public void whereTwoListensOverlapTheNewerOneIsKept() {
        List<String> older = Arrays.asList(line(10.0, "C 调 Ⅰ 级"), line(20.0, "C 调 Ⅰ 级"));
        List<String> newer = Arrays.asList(line(10.0, "C 调 Ⅳ 级"));
        ChordTimeline stitched = ChordTimeline.merge(Arrays.asList(newer, older));
        assertEquals("C 调 Ⅳ 级", stitched.at(10));
        // ...and the older one still fills what the newer one never reached.
        assertEquals("C 调 Ⅰ 级", stitched.at(20));
    }

    @Test
    public void anUnreadableLineDoesNotLoseTheRest() {
        ChordTimeline timeline = ChordTimeline.from(Arrays.asList(
                line(0.0, "C 调 Ⅰ 级"),
                "{ this is not json",
                line(10.0, "C 调 Ⅳ 级")));
        assertEquals(2, timeline.size());
        assertEquals("C 调 Ⅳ 级", timeline.at(10));
    }
}
