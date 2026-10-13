package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import java.util.Arrays;
import java.util.List;

/**
 * Which listens to keep, written as the rule that deletes things.
 *
 * Every test here is about what must *not* be removed: this is the only code in the
 * project that destroys the record, and the failure that matters is deleting
 * something that was wanted.
 */
public class SessionKeeperTest {
    private static String session(String stamp, String song) {
        return stamp + " " + song;
    }

    @Test
    public void threeOrFewerListensAreLeftAlone() {
        List<String> names = Arrays.asList(
                session("20261013 100000", "A — 一"),
                session("20261013 110000", "A — 一"),
                session("20261013 120000", "A — 一"));
        assertTrue(SessionKeeper.toDelete(names, "").isEmpty());
    }

    @Test
    public void theOldestGoesOnceThereAreFour() {
        List<String> names = Arrays.asList(
                session("20261013 120000", "A — 一"),
                session("20261013 100000", "A — 一"),
                session("20261013 130000", "A — 一"),
                session("20261013 110000", "A — 一"));
        assertEquals(
                Arrays.asList(session("20261013 100000", "A — 一")),
                SessionKeeper.toDelete(names, ""));
    }

    @Test
    public void songsAreCountedApartFromEachOther() {
        List<String> names = Arrays.asList(
                session("20261013 100000", "A — 一"),
                session("20261013 100001", "B — 二"),
                session("20261013 100002", "B — 二"),
                session("20261013 100003", "B — 二"),
                session("20261013 100004", "B — 二"));
        // The song heard four times loses one; the song heard once loses nothing.
        assertEquals(
                Arrays.asList(session("20261013 100001", "B — 二")),
                SessionKeeper.toDelete(names, ""));
    }

    @Test
    public void theListenHappeningNowIsNeverDeleted() {
        List<String> names = Arrays.asList(
                session("20261013 100000", "A — 一"),
                session("20261013 100001", "A — 一"),
                session("20261013 100002", "A — 一"),
                session("20261013 100003", "A — 一"));
        // Even if it is the oldest by name - a clock that moved backwards, a session
        // resumed - the one being written to stays.
        assertTrue(SessionKeeper.toDelete(names, session("20261013 100000", "A — 一")).isEmpty());
    }

    @Test
    public void aNameThatIsNotASessionIsLeftAlone() {
        List<String> names = Arrays.asList(
                "summary-v2.csv",
                "songs",
                session("20261013 100000", "A — 一"),
                session("20261013 100001", "A — 一"),
                session("20261013 100002", "A — 一"),
                session("20261013 100003", "A — 一"));
        List<String> doomed = SessionKeeper.toDelete(names, "");
        assertEquals(1, doomed.size());
        assertEquals(session("20261013 100000", "A — 一"), doomed.get(0));
    }

    @Test
    public void nineListensLeaveSixGoneOldestFirst() {
        List<String> names = new java.util.ArrayList<>();
        for (int hour = 10; hour < 19; hour++) {
            names.add(session(String.format("20261013 %02d0000", hour), "A — 一"));
        }
        List<String> doomed = SessionKeeper.toDelete(names, "");
        assertEquals(6, doomed.size());
        assertEquals(session("20261013 100000", "A — 一"), doomed.get(0));
        assertEquals(session("20261013 150000", "A — 一"), doomed.get(5));
    }

    @Test
    public void aListenGivenANumberIsTheSameSong() {
        // Two listens starting in the same second get " 2" appended to the second
        // directory. Matching or counting by the raw name made them two songs, which
        // is why the chord cache never found a previous listen and why retention kept
        // three of each.
        assertEquals("A — 一", SessionKeeper.songOf(session("20261013 100000", "A — 一")));
        assertEquals("A — 一", SessionKeeper.songOf(session("20261013 100000", "A — 一 2")));
        assertEquals("A — 一", SessionKeeper.songOf(session("20261013 100000", "A — 一 12")));
        // A title that itself ends in a number cannot be told apart from a numbered
        // copy - "1984 2" is either a song called "1984 2" or the second directory of
        // a song called "1984". The suffix is stripped, and that is the accepted
        // cost: not stripping it is what made the cache never find a previous listen
        // and made retention keep three of each name.
        assertEquals("A — 1984", SessionKeeper.songOf(session("20261013 100000", "A — 1984 2")));
        assertEquals("", SessionKeeper.songOf("summary-v2.csv"));
    }

    @Test
    public void numberedCopiesCountTowardsTheSameThree() {
        List<String> names = Arrays.asList(
                session("20261013 100000", "A — 一"),
                session("20261013 100000", "A — 一 2"),
                session("20261013 110000", "A — 一"),
                session("20261013 120000", "A — 一"));
        List<String> doomed = SessionKeeper.toDelete(names, "");
        assertEquals(1, doomed.size());
        assertEquals(session("20261013 100000", "A — 一"), doomed.get(0));
    }
}
