package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import org.json.JSONObject;
import org.junit.Test;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.List;

/**
 * One song, one place, in two forms: something a script can read and something
 * a person can read.
 *
 * The machine form is what it has always been - one JSON object per line,
 * appended, never rewritten. What was missing is that it was all one file across
 * every song and every evening, with times counted from whenever the app was
 * started, which is a file you cannot answer a question from. So: a directory
 * per song, times counted from the start of that song, and a plain text log
 * beside the JSON whose lines say who spoke and what they said.
 */
public class SongLogTest {
    private static File tempRoot() throws Exception {
        File root = File.createTempFile("listening", "");
        root.delete();
        root.mkdirs();
        return root;
    }

    @Test
    public void timesAreCountedFromTheStartOfTheSong() {
        assertEquals("00:00.0", SongLog.clock(0));
        assertEquals("00:03.5", SongLog.clock(3.48));
        assertEquals("01:03.4", SongLog.clock(63.44));
        assertEquals("12:34.5", SongLog.clock(754.5));
        assertEquals("1:02:05.0", SongLog.clock(3725));
        assertEquals("--:--.-", SongLog.clock(-1));
    }

    @Test
    public void aNameBecomesSomethingAFileSystemWillTake() {
        assertEquals("孤雪-真岛遥", SongLog.slug("孤雪 — 真岛遥"));
        assertEquals("a-b", SongLog.slug("a/b"));
        assertEquals("track", SongLog.slug("   "));
        assertTrue(SongLog.slug("x".repeat(200)).length() <= 60);
    }

    @Test
    public void bothFormsAreWrittenIntoTheSongsOwnDirectory() throws Exception {
        File root = tempRoot();
        SongLog song = new SongLog(root, "孤雪 — 真岛遥", 1_700_000_000_000L);
        song.say("模型", "听起来像爵士，和刚才比很稳定", 12.0);
        song.say("我", "暂停，问：你刚才可能注意到的是：", 31.2);
        song.event(new JSONObject().put("state", "AI_PROPOSED"), 31.2);

        assertTrue(song.directory().isDirectory());
        assertTrue("under the root: " + song.directory(),
                song.directory().getParentFile().equals(root));

        List<String> readable = Files.readAllLines(song.readable().toPath(),
                StandardCharsets.UTF_8);
        assertTrue("a header naming the song: " + readable,
                readable.get(0).contains("孤雪"));
        assertTrue(readable.get(1).startsWith("00:12.0"));
        assertTrue(readable.get(1).contains("模型"));
        assertTrue(readable.get(2).contains("00:31.2"));
        assertTrue(readable.get(2).contains("暂停"));

        List<String> events = Files.readAllLines(song.events().toPath(),
                StandardCharsets.UTF_8);
        assertEquals(1, events.size());
        assertTrue("the machine line knows which song it belongs to: " + events.get(0),
                events.get(0).contains("孤雪"));
        assertTrue("and where in the song: " + events.get(0), events.get(0).contains("31.2"));
    }

    /** Two songs in one evening do not share a file. */
    @Test
    public void aSecondSongGetsItsOwnDirectory() throws Exception {
        File root = tempRoot();
        SongLog first = new SongLog(root, "第一首", 1_700_000_000_000L);
        SongLog second = new SongLog(root, "第二首", 1_700_000_060_000L);
        assertTrue(!first.directory().equals(second.directory()));
        first.event(new JSONObject().put("state", "AI_PROPOSED"), 1);
        assertEquals(1, Files.readAllLines(first.events().toPath(),
                StandardCharsets.UTF_8).size());
        assertEquals("the other song's file is untouched",
                true, !second.events().exists() || second.events().length() == 0);
    }
}
