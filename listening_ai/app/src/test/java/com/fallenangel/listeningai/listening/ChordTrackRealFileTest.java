package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertTrue;

import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.Assume;
import org.junit.Test;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

/**
 * The chord reader against the Workbench's own harmony file, all thirty songs of it.
 *
 * Written by hand, a fixture is whatever I remember the shape being. This one is the
 * file: every song in it has to come back out as a track whose chords are where the
 * file says they are, and the app has to find the song by the audio name inside.
 */
public class ChordTrackRealFileTest {

    private static File harmonyFile() {
        File at = new File(System.getProperty("user.dir", ".")).getAbsoluteFile();
        for (int up = 0; up < 8 && at != null; up++) {
            File candidate = new File(at, "mgmcs_validator" + File.separator + "static"
                    + File.separator + "harmony.json");
            if (candidate.isFile()) return candidate;
            at = at.getParentFile();
        }
        return null;
    }

    @Test
    public void everySongInTheirHarmonyFileComesBackOut() throws Exception {
        File file = harmonyFile();
        Assume.assumeTrue("这台机器上没有 mgmcs_validator/static/harmony.json，跳过", file != null);
        JSONObject root = new JSONObject(new String(Files.readAllBytes(file.toPath()),
                StandardCharsets.UTF_8));
        JSONArray songs = root.optJSONArray("songs");
        assertNotNull(songs);
        assertTrue("他们的和弦文件里应该有歌", songs.length() > 0);

        int found = 0;
        int spans = 0;
        StringBuilder summary = new StringBuilder("和弦轨（" + songs.length() + " 首）：");
        for (int i = 0; i < songs.length(); i++) {
            JSONObject song = songs.optJSONObject(i);
            if (song == null) continue;
            String audio = song.optString("audio", song.optString("song", ""));
            ChordTrack track = ChordTrack.from(root, audio);
            assertNotNull("按 " + audio + " 找不回来", track);
            found++;
            spans += track.size();
            assertTrue(audio, track.size() > 0);
            // Every track has to answer for its own first half bar, and say nothing
            // before it: a file that started at 18 seconds must not claim 0.
            ChordTrack.Chord first = track.at(track.firstStart() + 0.01);
            assertNotNull(audio, first);
            assertTrue(audio + " → 匹配到 " + track.song + "，第一格 [" + first.label + "]",
                    first.label.length() > 0);
            if (i < 3) summary.append("\n  ").append(track.song).append(" → ").append(first.label)
                    .append("（").append(track.size()).append(" 个半小节格，")
                    .append(track.keyName).append("，小节来自 ").append(track.reading).append("）");
        }
        System.out.println(summary);
        assertTrue("一半都找不回来：" + found + "/" + songs.length(), found >= songs.length() - 1);
        assertTrue(spans > 1000);
    }

}
