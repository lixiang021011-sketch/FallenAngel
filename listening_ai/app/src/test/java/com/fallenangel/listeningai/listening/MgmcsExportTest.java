package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertTrue;

import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.Test;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

/**
 * What the phone hands the desktop validator.

 * Two things matter here and nothing else does. The raw presses must survive, so
 * that a better reading of them is still possible after this code has been
 * replaced; and the shape must be the one the desktop reads, because a document
 * that parses into nothing looks exactly like a song with no grids in it.

 * The event names are shared with `mgmcs_validator/tools/from_listening_ai.py`,
 * which rebuilds the same document from old recordings. A change on one side has
 * to be made on the other, and these tests are what will say so.
 */
public class MgmcsExportTest {

    private static JSONObject tap(int index, String button, long audioMillis, double songTime) {
        JSONObject line = new JSONObject();
        try {
            line.put("kind", "ANCHOR_TAP");
            line.put("tapIndex", index);
            line.put("button", button);
            line.put("audioTimeMs", audioMillis);
            line.put("clientTime", 1791518484048L + index);
            line.put("sessionId", 12);
            line.put("songTime", songTime);
        } catch (Exception ignored) {
        }
        return line;
    }

    private static List<JSONObject> oneAttempt() {
        List<JSONObject> taps = new ArrayList<>();
        for (int i = 0; i < 4; i++) {
            taps.add(tap(i, i == 0 ? "DOWNBEAT" : "BEAT", 528922 + i * 680, 7.268 + i * 0.68));
        }
        return taps;
    }

    @Test
    public void aCalibrationCarriesTheNumbersAndThePressesBehindThem() {
        JSONObject grid = MgmcsExport.calibration("cal_01", 12.3456, 88.8888, 4,
                "由 4 下按压重算", oneAttempt());

        assertEquals("cal_01", grid.optString("calibrationId"));
        assertEquals(12.346, grid.optDouble("audioTime"), 0.0001);
        assertEquals(88.889, grid.optDouble("bpm"), 0.0001);
        assertEquals(4, grid.optInt("beatsPerBar"));
        assertEquals(4, grid.optInt("beatUnit"));
        assertEquals("human", grid.optString("source"));

        JSONArray taps = grid.optJSONArray("rawTaps");
        assertNotNull(taps);
        assertEquals(4, taps.length());
        JSONObject first = taps.optJSONObject(0);
        assertEquals("DOWNBEAT", first.optString("button"));
        assertEquals(0, first.optInt("tapIndex"));
        // Both clocks travel with the press: the song's, which the desktop draws
        // in, and the phone's own, which is the precise one.
        assertEquals(7.268, first.optDouble("songTime"), 0.0001);
        assertEquals(528.922, first.optDouble("captureTime"), 0.0001);
        assertEquals(12, first.optInt("sessionId"));
    }

    @Test
    public void aQuestionAndItsAnswerAreOneMomentOnTheTimeline() {
        List<JSONObject> records = new ArrayList<>();
        records.add(bankLine("ASKED", "ATTN_01", 12, 20.125, null));
        records.add(bankLine("ANSWERED", "ATTN_01", 12, 20.125, "鼓"));

        List<JSONObject> events = MgmcsExport.events(records);
        assertEquals(1, events.size());
        JSONObject event = events.get(0);
        assertEquals("attention_query", event.optString("type"));
        assertEquals(20.125, event.optDouble("audioTime"), 0.0001);
        assertEquals("鼓", event.optJSONObject("payload").optString("answer"));
        assertEquals("answered", event.optJSONObject("payload").optString("state"));
        assertEquals("ATTN_01", event.optJSONObject("payload").optString("templateId"));
    }

    @Test
    public void aQuestionNobodyAnsweredSaysSoRatherThanLookingUnanswered() {
        List<JSONObject> records = new ArrayList<>();
        records.add(bankLine("ASKED", "ROLE_01", 4, 9.5, null));
        records.add(bankLine("IGNORED", "ROLE_01", 4, 9.5, null));

        JSONObject event = MgmcsExport.events(records).get(0);
        assertEquals("ignored", event.optJSONObject("payload").optString("state"));
        assertTrue(event.optJSONObject("payload").isNull("answer"));
    }

    @Test
    public void aPauseMomentIsWrittenWhereTheListenerWasInTheSong() {
        JSONObject line = new JSONObject();
        try {
            line.put("id", "q6-1a11ed36c03");
            line.put("aiVersion", "candidates-v1");
            line.put("state", "AI_PROPOSED");
            line.put("targetMusicTime", 574.2879818594105);
            line.put("interactionTime", 579.5836281179138);
            line.put("chosenLabel", "");
            // The player's clock: what the desktop timeline is in. The capture
            // clock above is 500-odd seconds away from it and would put the event
            // minutes out of place.
            line.put("songTime", 41.5);
        } catch (Exception ignored) {
        }
        JSONObject event = MgmcsExport.events(Arrays.asList(line)).get(0);
        assertEquals("pause_moment", event.optString("type"));
        assertEquals(41.5, event.optDouble("audioTime"), 0.0001);
    }

    @Test
    public void theHumanAnswerAboutTheBarLineIsKept() {
        JSONObject line = new JSONObject();
        try {
            line.put("kind", "BAR_ANCHOR");
            line.put("choice", "bar_starts");
            line.put("meter", 4);
            line.put("bpm", 120.0);
            line.put("songTime", 33.25);
        } catch (Exception ignored) {
        }
        JSONObject event = MgmcsExport.events(Arrays.asList(line)).get(0);
        assertEquals("bar_answer", event.optString("type"));
        assertEquals(33.25, event.optDouble("audioTime"), 0.0001);
        assertEquals("bar_starts", event.optJSONObject("payload").optString("choice"));
    }

    @Test
    public void pressesStayInsideTheirCalibrationRatherThanBecomingEvents() {
        List<JSONObject> records = new ArrayList<>(oneAttempt());
        JSONObject press = new JSONObject();
        try {
            press.put("kind", "PRESS_SNAP");
            press.put("what", "question_answer");
            press.put("songTime", 15.75);
        } catch (Exception ignored) {
        }
        records.add(press);
        assertEquals(0, MgmcsExport.events(records).size());
    }

    /**
     * What the listener did to the music is part of the record.

     * A switch is not something a grid can show, and without it a session that
     * was abandoned halfway looks like a recording that simply stops.
     */
    @Test
    public void switchingCameBackAndJumpingAreAllWrittenDown() {
        List<JSONObject> records = new ArrayList<>();
        records.add(line("SONG_ENTER", "songTime", 0.0,
                "from", "previous song", "reentry", true, "playsSoFar", 2));
        records.add(line("SEEK", "songTime", 120.0,
                "fromSeconds", 120.0, "toSeconds", 12.0, "backwards", true));
        records.add(line("SONG_CHANGE", "songTime", 205.0, "to", "next song"));

        List<JSONObject> events = MgmcsExport.events(records);
        assertEquals(3, events.size());
        assertEquals("song_enter", events.get(0).optString("type"));
        assertTrue(events.get(0).optJSONObject("payload").optBoolean("reentry"));
        assertEquals(0.0, events.get(0).optDouble("audioTime"), 0.0001);
        assertEquals("seek", events.get(1).optString("type"));
        assertEquals(12.0, events.get(1).optJSONObject("payload").optDouble("to"), 0.0001);
        assertEquals("song_change", events.get(2).optString("type"));
        assertEquals("next song", events.get(2).optJSONObject("payload").optString("to"));
        assertEquals(205.0, events.get(2).optDouble("audioTime"), 0.0001);
    }

    private static JSONObject line(String kind, String timeField, double time,
                                   Object... pairs) {
        JSONObject out = new JSONObject();
        try {
            out.put("kind", kind);
            out.put(timeField, time);
            for (int i = 0; i + 1 < pairs.length; i += 2) {
                out.put(String.valueOf(pairs[i]), pairs[i + 1]);
            }
        } catch (Exception ignored) {
        }
        return out;
    }

    @Test
    public void theDocumentHasEverythingTheValidatorAsksFor() {
        List<JSONObject> grids = new ArrayList<>();
        grids.add(MgmcsExport.calibration("cal_01", 12.0, 120.0, 4, "", oneAttempt()));
        JSONObject document = MgmcsExport.document("song_001", "Example", "song.flac",
                226.12, grids, Arrays.asList(bankLine("ASKED", "ATTN_01", 3, 8.0, null)));

        assertEquals("0.1", document.optString("schemaVersion"));
        JSONObject song = document.optJSONObject("song");
        assertEquals("song_001", song.optString("songId"));
        assertEquals(226.12, song.optDouble("durationSeconds"), 0.0001);

        JSONArray calibrations = document.optJSONArray("calibrations");
        assertEquals(1, calibrations.length());
        JSONObject grid = calibrations.optJSONObject(0);
        assertTrue(grid.optDouble("bpm") > 0);
        assertTrue(grid.optInt("beatsPerBar") >= 1);

        JSONArray events = document.optJSONArray("events");
        assertEquals(1, events.length());
        assertTrue(events.optJSONObject(0).has("audioTime"));
        assertTrue(events.optJSONObject(0).optDouble("audioTime") >= 0);
    }

    @Test
    public void aSongNobodyTappedStillExportsAndSaysItHasNoGrids() {
        JSONObject document = MgmcsExport.document("song_002", "Unplayed", "", -1,
                new ArrayList<JSONObject>(), new ArrayList<JSONObject>());
        assertEquals(0, document.optJSONArray("calibrations").length());
        assertEquals(0, document.optJSONArray("events").length());
        assertFalse(document.optJSONObject("song").has("durationSeconds"));
        assertEquals("0 条网格，0 个事件", MgmcsExport.summarise(document));
    }

    private static JSONObject bankLine(String state, String templateId, int songBar,
                                       double songSeconds, String answer) {
        JSONObject line = new JSONObject();
        try {
            line.put("type", "QUERY");
            line.put("state", state);
            line.put("templateId", templateId);
            line.put("targetSignal", "perceived_salience");
            line.put("songBar", songBar);
            line.put("songSeconds", songSeconds);
            line.put("text", "刚才最吸引你注意的是谁？");
            line.put("songTime", songSeconds);
            if (answer != null) line.put("answer", answer);
        } catch (Exception ignored) {
        }
        return line;
    }
}
