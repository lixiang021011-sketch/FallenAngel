package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.Test;

/**
 * The Workbench's own chord track, read as the chord track it is.
 *
 * The shapes and values are the ones their harmony file actually writes: half-bar
 * spans with their notes' chord, the degree, and two other readings to compare with.
 */
public class ChordTrackTest {

    private static JSONObject span(int bar, int half, double start, double end, String template,
                                   int degree, String roman, String chart, String madmom)
            throws Exception {
        return new JSONObject()
                .put("bar", bar).put("half", half)
                .put("start", start).put("end", end)
                .put("template", template).put("degree", degree).put("roman", roman)
                .put("chart", chart).put("madmom", madmom);
    }

    /** 赤橙, as harmony.json writes it: the first two bars, half a bar at a time. */
    private static JSONObject chichunhua() throws Exception {
        return new JSONObject()
                .put("song", "chichunhua")
                .put("audio", "ACIDMAN - 赤橙 (Remastered 2019).ogg")
                .put("timing", "caution")
                .put("reading", "BeatThis")
                .put("agreement", 0.61)
                .put("key", new JSONObject().put("tonic", "A#").put("mode", "major")
                        .put("coverage", 0.729))
                .put("spans", new JSONArray()
                        .put(span(1, 1, 0.68, 1.55, "Cm9", 2, "ii", "Cm", "D#"))
                        .put(span(1, 2, 1.55, 2.44, "Cm9", 2, "ii", "D", "D#"))
                        .put(span(2, 1, 2.44, 3.31, "Cm9", 2, "ii", "D", "D#"))
                        .put(span(2, 2, 3.31, 4.18, "A#maj7", 1, "I", "A#maj7", "A#maj7")));
    }

    private static JSONObject everything(JSONObject... songs) throws Exception {
        JSONArray list = new JSONArray();
        for (JSONObject song : songs) list.put(song);
        return new JSONObject().put("note", "三条读法落在同一个半小节格上").put("songs", list);
    }

    @Test
    public void theChordIsLookedUpByHalfBar() throws Exception {
        ChordTrack track = ChordTrack.from(everything(chichunhua()), "ACIDMAN - 赤橙");
        assertNotNull(track);
        assertEquals(4, track.size());
        assertEquals("A# 调 ⅱ 级（Cm9）", track.at(0.7).label);
        assertEquals("A# 调 ⅱ 级（Cm9）", track.at(1.6).label);
        assertEquals("A# 调 Ⅰ 级（A#maj7）", track.at(3.5).label);
        assertFalse("文件从头开始的地方之前它不说话", track.covers(0.5));
        assertFalse("结尾之后也一样", track.covers(5.0));
        assertTrue(track.covers(2.5));
    }

    @Test
    public void theSongIsFoundByTheAudioFileNameInside() throws Exception {
        // The app knows the player's label for the song, the file knows the audio it was
        // measured from, and the two spell the artist differently - the same tolerance
        // the grid reader already needs.
        JSONObject file = everything(chichunhua(),
                new JSONObject().put("song", "other")
                        .put("audio", "YOASOBI - Monster.ogg")
                        .put("spans", new JSONArray().put(span(1, 1, 1.0, 1.5, "C", 1, "I", "C", "C"))));
        assertNotNull(ChordTrack.from(file, "ACIDMAN (アシッドマン) — 赤橙 (Remastered 2019)"));
        assertNotNull(ChordTrack.from(file, "YOASOBI - Monster"));
        assertNull(ChordTrack.from(file, "完全另一首歌"));
    }

    @Test
    public void aFileWithOneSongInItIsReadJustAsWell() throws Exception {
        ChordTrack track = ChordTrack.from(chichunhua(), "ACIDMAN — 赤橙");
        assertNotNull(track);
        assertEquals("A# 调 ⅱ 级（Cm9）", track.at(1.0).label);
    }

    @Test
    public void aDisagreementIsKeptForTheMomentItIsIn() throws Exception {
        ChordTrack track = ChordTrack.from(everything(chichunhua()), "ACIDMAN - 赤橙");
        assertTrue("三读法不一致的那格", track.at(1.0).disagreed);
        assertFalse("一致的那格不必存疑", track.at(3.5).disagreed);
    }

    @Test
    public void aSourceThatObservedNothingIsNotADisagreement() throws Exception {
        // Their note: an empty label means that source observed nothing there. One voice
        // is not a disagreement.
        JSONObject song = new JSONObject()
                .put("song", "x").put("audio", "Someone - Song.ogg")
                .put("key", new JSONObject().put("tonic", "D").put("mode", "major"))
                .put("spans", new JSONArray()
                        .put(new JSONObject().put("bar", 1).put("half", 1)
                                .put("start", 0.0).put("end", 1.0)
                                .put("template", "").put("roman", "").put("chart", "D")
                                .put("madmom", "")));
        ChordTrack track = ChordTrack.from(song, "Someone - Song");
        assertNotNull(track);
        assertEquals("只有一条读法说了话就用它", "D", track.at(0.5).label);
        assertFalse(track.at(0.5).disagreed);
    }

    @Test
    public void theModeIsNotInTheLabelButTheTonicIs() throws Exception {
        // A listener asked not to have major and minor in the label; the key still says
        // which tonic the degrees are counted from, which is what makes them readable.
        ChordTrack track = ChordTrack.from(everything(chichunhua()), "ACIDMAN - 赤橙");
        assertEquals("A# 调", track.keyName);
        assertEquals("", ChordTrack.numeral(""));
        assertFalse(track.at(1.0).label.contains("大调"));
        assertFalse(track.at(1.0).label.contains("小调"));
    }

    @Test
    public void theDegreesTheFileActuallyUsesAllHaveNumerals() {
        assertEquals("Ⅰ", ChordTrack.numeral("I"));
        assertEquals("ⅱ", ChordTrack.numeral("ii"));
        assertEquals("ⅲ", ChordTrack.numeral("iii"));
        assertEquals("Ⅳ", ChordTrack.numeral("IV"));
        assertEquals("Ⅴ", ChordTrack.numeral("V"));
        assertEquals("ⅵ", ChordTrack.numeral("vi"));
        assertEquals("Ⅶ", ChordTrack.numeral("VII"));
        // Anything else is printed as written rather than guessed at.
        assertEquals("bVII", ChordTrack.numeral("bVII"));
    }
}
