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
 * Reading the Workbench's own files, in the shape they are actually written in.
 *
 * The interesting part is not the parsing - it is which of the readings inside a
 * file gets used. A file usually holds several: a machine's guess, a person's taps
 * unsnapped and snapped, and the workbench's corrected version. Choosing the wrong
 * one would make the app confident and wrong, which is worse than tracking the beat
 * itself.
 */
public class WorkbenchGridTest {
    private static JSONObject calibration(String reading, String health, double bpm,
                                          int beatsPerBar, double[] beats) throws Exception {
        JSONArray array = new JSONArray();
        for (double beat : beats) array.put(beat);
        JSONObject item = new JSONObject()
                .put("calibrationId", reading)
                .put("reading", reading)
                .put("audioTime", beats.length == 0 ? 0 : beats[0])
                .put("bpm", bpm)
                .put("beatsPerBar", beatsPerBar)
                .put("beats", array)
                .put("health", health);
        if (!"ok".equals(health)) item.put("suspectReasons", new JSONArray().put("滑了一拍"));
        return item;
    }

    private static JSONObject file(String title, double duration, JSONObject... calibrations)
            throws Exception {
        JSONArray array = new JSONArray();
        for (JSONObject calibration : calibrations) array.put(calibration);
        return new JSONObject()
                .put("schemaVersion", 5)
                .put("generatedBy", "mgmcs_validator")
                .put("song", new JSONObject().put("title", title).put("durationSeconds", duration))
                .put("calibrations", array)
                .put("events", new JSONArray());
    }

    @Test
    public void theWorkbenchReadingBeatsTheMachines() throws Exception {
        WorkbenchGrid grid = WorkbenchGrid.from(file("赤橙", 305.9,
                calibration("librosa", "ok", 136.0, 4, new double[] {0.0, 0.44, 0.88}),
                calibration("workbench", "ok", 136.0, 4, new double[] {0.0, 0.441, 0.882})));
        assertNotNull(grid);
        assertEquals("workbench", grid.reading);
        assertEquals(4, grid.beatsPerBar);
        assertEquals(3, grid.beatCount());
    }

    @Test
    public void aCorrectedHumanReadingBeatsTheMachines() throws Exception {
        WorkbenchGrid grid = WorkbenchGrid.from(file("赤橙", 305.9,
                calibration("librosa", "ok", 136.0, 4, new double[] {0.0, 0.44}),
                calibration("human_snapped", "ok", 136.0, 4, new double[] {0.0, 0.442})));
        assertEquals("human_snapped", grid.reading);
    }

    @Test
    public void aReadingTheValidatorDistrustedIsNotUsed() throws Exception {
        // The workbench one is preferred but suspect - so the next best healthy
        // reading is the one to build on, not the highest ranked one there is.
        WorkbenchGrid grid = WorkbenchGrid.from(file("赤橙", 305.9,
                calibration("workbench", "suspect", 68.0, 4, new double[] {0.0, 0.88}),
                calibration("librosa", "ok", 136.0, 4, new double[] {0.0, 0.44, 0.88})));
        assertEquals("librosa", grid.reading);
        assertEquals(3, grid.beatCount());
    }

    @Test
    public void severalEntriesOfOneReadingAreOneGridInPieces() throws Exception {
        // How a song that changes tempo is written down: two pieces, in order, with
        // the second starting where the first stopped.
        WorkbenchGrid grid = WorkbenchGrid.from(file("变速的歌", 200,
                calibration("workbench", "ok", 120, 4, new double[] {10.0, 10.5, 11.0}),
                calibration("workbench", "ok", 90, 4, new double[] {11.66, 12.33})));
        assertEquals(5, grid.beatCount());
        assertEquals(10.0, grid.beat(0), 1e-9);
        assertEquals(11.66, grid.beat(3), 1e-9);
        assertEquals(12.33, grid.beat(4), 1e-9);
    }

    @Test
    public void aGridThatHasRunOutSaysSo() throws Exception {
        WorkbenchGrid grid = WorkbenchGrid.from(file("短文件", 60,
                calibration("workbench", "ok", 120, 4, new double[] {0.0, 0.5, 1.0})));
        assertTrue(grid.covers(0.9));
        assertFalse("past the last beat it must not pretend", grid.covers(30));
        assertEquals(-1, grid.beatInBar(30));
        assertEquals(1, grid.beatInBar(0.5));
    }

    @Test
    public void aFileWithNoUsableReadingIsNotAGrid() throws Exception {
        assertNull(WorkbenchGrid.from(file("只有嫌疑读法", 100,
                calibration("workbench", "suspect", 120, 4, new double[] {0.0, 0.5}))));
        assertNull(WorkbenchGrid.from(new JSONObject().put("song", new JSONObject())));
    }

    @Test
    public void matchingIsByTitleAndForbearingAboutPunctuation() throws Exception {
        WorkbenchGrid grid = WorkbenchGrid.from(file("ACIDMAN - 赤橙 (Remastered 2019)", 305.9,
                calibration("librosa", "ok", 136.0, 4, new double[] {0.0, 0.44})));
        // The app's own label writes the artist into the title too, and the two
        // systems punctuate differently.
        assertTrue(grid.matches("ACIDMAN - 赤橙 (Remastered 2019)", 305.9));
        assertFalse(grid.matches("完全另一首歌", 305.9));
    }
}
