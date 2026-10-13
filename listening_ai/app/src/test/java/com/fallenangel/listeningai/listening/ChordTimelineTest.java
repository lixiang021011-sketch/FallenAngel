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

    private static ChordTimeline some() {
        return ChordTimeline.from(Arrays.asList(
                line(0.0, "C 调 Ⅰ 级"),
                line(10.0, "C 调 Ⅳ 级"),
                line(20.0, "C 调 Ⅴ 级")));
    }

    @Test
    public void readsBackWhatWasWritten() {
        assertEquals(3, some().size());
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
        // The change is written at ten seconds. Read verbatim, ten seconds of the
        // song would still show the old chord; read ahead, the new one arrives at
        // the point where the music actually changes.
        assertEquals("C 调 Ⅳ 级", timeline.at(9.5));
        assertEquals("C 调 Ⅰ 级", timeline.at(8.0));
    }

    @Test
    public void beforeTheFirstReadingItSaysWhatTheFirstReadingSays() {
        assertEquals("C 调 Ⅰ 级", some().at(-30));
    }

    @Test
    public void afterTheLastReadingItKeepsTheLastOne() {
        assertEquals("C 调 Ⅴ 级", some().at(600));
    }

    @Test
    public void pastTheLastReadingItSaysItHasNothingLeft() {
        ChordTimeline timeline = some();      // 0, 10, 20 seconds
        assertTrue(timeline.covers(0));
        assertTrue(timeline.covers(18));
        // The last reading is at 20 seconds, and one look-ahead is needed to say
        // anything about the music there.
        assertFalse(timeline.covers(19));
        assertFalse("a listen cut short must not be read past its end", timeline.covers(25));
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
