package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.Test;

import java.util.ArrayList;
import java.util.List;

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

    /** A list of plain times, the way madmom, BeatThis and librosa write them. */
    private static JSONArray numbers(double... times) throws Exception {
        JSONArray array = new JSONArray();
        for (double time : times) array.put(time);
        return array;
    }

    /**
     * A list of times the way the Workbench writes its bar lines: objects carrying a
     * `time`, because a bar line there is more than a time.
     */
    private static JSONArray bars(double... times) throws Exception {
        JSONArray array = new JSONArray();
        for (double time : times) array.put(new JSONObject().put("time", time));
        return array;
    }

    @Test
    public void barLinesReadAsBeatsTooWhenTheyCarryTheirTime() throws Exception {
        // 打上花火, the Workbench's reading of the drums batch: bar lines as objects, 1.25 s
        // apart (191.994 bpm, 4/4), and no beats anywhere in the entry.
        JSONObject workbench = new JSONObject()
                .put("calibrationId", "workbench")
                .put("bpm", 191.994)
                .put("audioTime", 0.627237)
                .put("beatsPerBar", 4)
                .put("beatUnit", 4)
                .put("health", "ok")
                .put("barLines", bars(0.627237, 1.877276, 3.127315, 4.377354));
        WorkbenchGrid grid = WorkbenchGrid.from(file("打上花火", -1, workbench));
        assertNotNull(grid);
        assertEquals("workbench", grid.reading);
        assertEquals("barLines", grid.shape);
        assertEquals(13, grid.beatCount());
        assertEquals(0.627237, grid.beat(0), 1e-9);
        assertEquals(0.93974675, grid.beat(1), 1e-6);
        assertEquals(0, grid.barAt(1.0));
        assertEquals(3, grid.beatInBar(1.8));
        assertFalse("past the last bar line it must not invent beats", grid.covers(6));
    }

    @Test
    public void downbeatsBecomeBeatsAndTheFileNameSaysWhichAudio() throws Exception {
        // 打上花火 again, madmom this time: bar lines only, made from drums.wav while the
        // song is the mix, and no `reading` at all - the drums batch writes only the
        // calibrationId, so that is what has to be read instead.
        JSONObject madmom = new JSONObject()
                .put("calibrationId", "madmom")
                .put("bpm", 95.238)
                .put("audioTime", 20.62)
                .put("beatsPerBar", 4)
                .put("downbeats", numbers(20.62, 23.12, 25.62))
                .put("audioFileName", "drums.wav");
        List<String> journal = new ArrayList<>();
        WorkbenchGrid grid = WorkbenchGrid.from(file("打上花火", -1, madmom), journal);
        assertNotNull(grid);
        assertEquals("madmom", grid.reading);
        assertEquals("downbeats", grid.shape);
        assertEquals("drums", grid.audioSource);
        assertEquals(9, grid.beatCount());
        assertEquals(21.245, grid.beat(1), 1e-9);
        assertEquals(0, grid.barAt(20.7));
        assertEquals(1, grid.barAt(23.2));
        assertFalse("before its first line the grid says nothing", grid.covers(19.0));
        assertEquals(-1, grid.positionAt(5));
        assertTrue(journal.toString(), journal.toString().contains("没写 reading"));
    }

    @Test
    public void theFirstBarLineSaysWhichBeatStartsTheBar() throws Exception {
        // 打上花火, BeatThis: beats from 20.64 and the first bar line at 21.28, which is
        // its second beat. Dividing by the meter instead would flash a whole bar early
        // for the rest of the song.
        JSONObject beatThis = new JSONObject()
                .put("calibrationId", "BeatThis")
                .put("reading", "BeatThis")
                .put("bpm", 191.994)
                .put("audioTime", 20.64)
                .put("beatsPerBar", 4)
                .put("health", "ok")
                .put("beats", numbers(20.64, 21.28, 21.9, 22.2, 22.52, 23.14))
                .put("downbeats", numbers(21.28, 23.78));
        WorkbenchGrid grid = WorkbenchGrid.from(file("打上花火", -1, beatThis));
        assertNotNull(grid);
        assertEquals("beats+downbeats", grid.shape);
        assertEquals(-1, grid.barAt(20.7));
        assertEquals(-1, grid.beatInBar(20.7));
        assertEquals(0, grid.barAt(21.5));
        assertEquals(0, grid.beatInBar(21.5));
        assertEquals(1, grid.beatInBar(21.95));
    }

    @Test
    public void whenAudioTimeIsTheFirstBarLineTheBarLinesAreTheGrid() throws Exception {
        // Which list an entry is written in is decided by which one `audioTime` is the
        // first line of. The Workbench hard-checks audioTime == downbeats[0]; BeatThis
        // writes audioTime == beats[0] in every file measured so far. Both are the same
        // song, so both are accepted, and the notes say which one was used.
        JSONObject beatThis = new JSONObject()
                .put("calibrationId", "BeatThis")
                .put("reading", "BeatThis")
                .put("bpm", 191.994)
                .put("audioTime", 21.28)
                .put("beatsPerBar", 4)
                .put("health", "ok")
                .put("beats", numbers(20.64, 20.96, 21.28, 21.6, 21.92, 22.24))
                .put("downbeats", numbers(21.28, 23.78));
        List<String> journal = new ArrayList<>();
        WorkbenchGrid grid = WorkbenchGrid.from(file("打上花火", -1, beatThis), journal);
        assertNotNull(grid);
        assertEquals("BeatThis", grid.reading);
        assertEquals("downbeats", grid.shape);
        assertEquals(21.28, grid.firstBeat(), 1e-9);
        assertEquals(5, grid.beatCount());
        assertEquals(0, grid.barAt(21.3));
        assertTrue(journal.toString(), journal.toString().contains("按downbeats铺拍点"));
    }

    @Test
    public void aReadingWhoseListsAreBothInAnotherCoordinateIsRefused() throws Exception {
        JSONObject odd = new JSONObject()
                .put("calibrationId", "BeatThis")
                .put("reading", "BeatThis")
                .put("bpm", 120)
                .put("audioTime", 99.0)
                .put("beatsPerBar", 4)
                .put("health", "ok")
                .put("beats", numbers(1.0, 1.5, 2.0))
                .put("downbeats", numbers(1.0, 3.0));
        List<String> journal = new ArrayList<>();
        assertNull(WorkbenchGrid.from(file("打上花火", -1, odd), journal));
        assertTrue(journal.toString(), journal.toString().contains("不是一个坐标"));
    }

    @Test
    public void aReadingInAnotherCoordinateIsRefusedWithItsReason() throws Exception {
        // mapperatorinator: audioTime is the start of its valid range and its beats
        // start 1.657 s earlier (打上花火: 27.895 against 26.238), so its beats are in a
        // coordinate of its own and every bar built on them would be in the wrong place.
        JSONObject mapperatorinator = new JSONObject()
                .put("calibrationId", "mapperatorinator")
                .put("reading", "mapperatorinator")
                .put("bpm", 96)
                .put("audioTime", 27.895)
                .put("beatsPerBar", 4)
                .put("health", "ok")
                .put("beats", numbers(26.238, 26.863, 27.488))
                .put("validRanges", new JSONArray()
                        .put(new JSONObject().put("start", 27.895).put("end", 60.0)));
        assertNull("只有它一条读法时，没有网格可用",
                WorkbenchGrid.from(file("打上花火", -1, mapperatorinator)));
        List<String> journal = new ArrayList<>();
        WorkbenchGrid grid = WorkbenchGrid.from(file("打上花火", -1, mapperatorinator,
                calibration("librosa", "ok", 95.3, 4, new double[] {0.021, 0.65, 1.28})), journal);
        assertEquals("librosa", grid.reading);
        assertTrue(journal.toString(), journal.toString().contains("不是一个坐标"));
    }

    @Test
    public void aReadingThatIsOnlyTapsIsNotAGrid() throws Exception {
        // human_snapped: taps and the offset they were snapped to, and no times of its
        // own - it is an anchor, not a grid.
        JSONObject human = new JSONObject()
                .put("calibrationId", "cal_01")
                .put("reading", "human_snapped")
                .put("bpm", 105.469)
                .put("audioTime", 18.471)
                .put("beatsPerBar", 4)
                .put("health", "ok")
                .put("rawTaps", new JSONArray().put(new JSONObject()
                        .put("tapIndex", 0).put("button", "DOWNBEAT").put("songTime", 5.021)));
        assertNull(WorkbenchGrid.from(file("消えちゃう前に", 200, human)));
        List<String> journal = new ArrayList<>();
        WorkbenchGrid.from(file("消えちゃう前に", 200, human), journal);
        assertTrue(journal.toString(), journal.toString().contains("锚点"));
    }

    @Test
    public void aSuspectReadingIsDroppedAndTheNextOneIsUsed() throws Exception {
        // 雲と幽霊: the Workbench's reading is suspect and carries no list at all, and
        // BeatThis is fine. One reading refused is not the whole file refused.
        JSONObject workbench = new JSONObject()
                .put("calibrationId", "workbench")
                .put("reading", "workbench")
                .put("bpm", 127.5)
                .put("audioTime", 1.773061)
                .put("beatsPerBar", 4)
                .put("health", "suspect")
                .put("suspectReasons", new JSONArray().put("beatThisLock 0.00（< 0.9）"));
        WorkbenchGrid grid = WorkbenchGrid.from(file("雲と幽霊", 238, workbench,
                calibration("BeatThis", "ok", 127.5, 4, new double[] {2.26, 2.73, 3.2})));
        assertNotNull(grid);
        assertEquals("BeatThis", grid.reading);
        assertFalse(grid.caution);
    }

    @Test
    public void aDerivedReadingIsUsedAndMarked() throws Exception {
        // mapperatorinator-filled: measures filled in by a rule between the model's own
        // windows, written with health=derived. Usable, and not a clean reading.
        JSONObject filled = new JSONObject()
                .put("calibrationId", "mapperatorinator-filled")
                .put("reading", "mapperatorinator-filled")
                .put("bpm", 170)
                .put("audioTime", 1.185)
                .put("beatsPerBar", 4)
                .put("health", "derived")
                .put("barLines", numbers(1.185, 2.5968, 4.0086));
        WorkbenchGrid grid = WorkbenchGrid.from(file("雲と幽霊", 6, filled,
                calibration("librosa", "ok", 84.7, 4, new double[] {0.244, 0.95})));
        assertNotNull(grid);
        assertEquals("mapperatorinator-filled", grid.reading);
        assertEquals("barLines", grid.shape);
        assertTrue("derived 不算干净读法", grid.caution);
    }

    @Test
    public void aRuleMadeBarIsMarkedForEars() throws Exception {
        // The Workbench's own flag on 打上花火: two short bars its rules proposed, waiting
        // for a person. It is the best reading there is, and it is not a measured one.
        JSONObject workbench = new JSONObject()
                .put("calibrationId", "workbench")
                .put("reading", "workbench")
                .put("bpm", 191.994)
                .put("audioTime", 0.627237)
                .put("beatsPerBar", 4)
                .put("health", "ok")
                .put("needsEars", true)
                .put("needsEarsNote", "含 2 个规则推出来的短小节")
                .put("barLines", bars(0.627237, 1.877276, 3.127315));
        WorkbenchGrid grid = WorkbenchGrid.from(file("打上花火", -1, workbench));
        assertNotNull(grid);
        assertTrue(grid.caution);
        assertTrue(grid.notes.toString(), grid.notes.toString().contains("needsEars"));
    }

    @Test
    public void aReadingThatOnlyReportsHalfItsBarsIsRefused() throws Exception {
        JSONObject sparse = new JSONObject()
                .put("calibrationId", "mapperatorinator")
                .put("reading", "mapperatorinator")
                .put("bpm", 170)
                .put("audioTime", 1.185)
                .put("beatsPerBar", 4)
                .put("health", "ok")
                .put("barLines", numbers(1.185, 2.5968, 4.0086));
        List<String> journal = new ArrayList<>();
        assertNull("3 根小节线撑不起 238 秒的歌",
                WorkbenchGrid.from(file("雲と幽霊", 238, sparse), journal));
        assertTrue(journal.toString(), journal.toString().contains("只报了"));
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
    public void positionAndTempoComeFromTheBeatTimes() throws Exception {
        // Four beats at 120 bpm, then three at 60: the file's `bpm` field says one
        // number for the whole file, and the number on screen has to be the one for
        // where the music is.
        WorkbenchGrid grid = WorkbenchGrid.from(file("变速", 100,
                calibration("workbench", "ok", 120, 4,
                        new double[] {0.0, 0.5, 1.0, 1.5, 2.5, 3.5, 4.5})));
        assertEquals(0, grid.positionAt(0.1));
        assertEquals(3, grid.positionAt(1.9));
        // 4.5 is still ahead of 4.4, so the beat in force is the one at 3.5.
        assertEquals(5, grid.positionAt(4.4));
        assertEquals(120.0, grid.localBpm(0.6), 1.0);
        assertEquals(60.0, grid.localBpm(4.0), 2.0);
        assertEquals(3, grid.beatInBar(1.9));
        assertEquals(1, grid.beatInBar(4.4));
    }

    @Test
    public void matchingIsByTitleAndForbearingAboutPunctuation() throws Exception {
        // The real pair, from the two systems' own files: the Workbench writes
        // "ACIDMAN - 赤橙 (Remastered 2019)" and the app's player gives
        // "ACIDMAN (アシッドマン) — 赤橙 (Remastered 2019)". An equality test rejected
        // exactly the file that was right.
        WorkbenchGrid grid = WorkbenchGrid.from(file("ACIDMAN - 赤橙 (Remastered 2019)", 305.9,
                calibration("librosa", "ok", 136.0, 4, new double[] {0.0, 0.44})));
        assertTrue(grid.matches("ACIDMAN (アシッドマン) — 赤橙 (Remastered 2019)", 305.9));
        assertTrue(grid.matches("ACIDMAN - 赤橙 (Remastered 2019)", 305.9));
        assertFalse(grid.matches("完全另一首歌", 305.9));
        assertFalse(grid.matches("ACIDMAN - 入秋的第一场雨真让人矫情", 210));
        assertEquals(1.0, grid.titleOverlap("ACIDMAN (アシッドマン) — 赤橙 (Remastered 2019)"), 1e-9);
    }
}
