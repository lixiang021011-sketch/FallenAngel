package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertTrue;

import org.json.JSONObject;
import org.json.JSONArray;
import org.junit.Assume;
import org.junit.Test;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;

/**
 * The reader against every file the Workbench has actually written.
 *
 * Written by hand, the shapes in these files are whatever I remember them being. This
 * runs the reader over all of them - 220 at the time of writing, 34 batches off the
 * mix and 159 off the drums - and says what came out, because "the import does not
 * work" is not a bug report and "214 of 220 leave a grid, refused reasons here" is.
 *
 * The two readings it wants to see are the ones the phone needed and did not get:
 * workbench and madmom, both of which report bar lines rather than beats and were
 * invisible to a reader that only understood `beats`.
 */
public class WorkbenchGridRealFilesTest {

    /**
     * The Workbench's own folder, found by walking up from wherever the tests run.
     *
     * `LISTENING_AI_MGMCS_DIR` points it somewhere else, which is how a handover folder
     * from the other side gets tested as it is, without being copied into this repo -
     * measured 2026-10-14, their samples arrive as a directory of their own.
     */
    private static File docsDirectory() {
        String given = System.getenv("LISTENING_AI_MGMCS_DIR");
        if (given != null && !given.isEmpty()) {
            File elsewhere = new File(given);
            if (elsewhere.isDirectory()) return elsewhere;
        }
        File at = new File(System.getProperty("user.dir", ".")).getAbsoluteFile();
        for (int up = 0; up < 8 && at != null; up++) {
            File candidate = new File(at, "mgmcs_validator" + File.separator + "static" + File.separator + "docs");
            if (candidate.isDirectory()) return candidate;
            at = at.getParentFile();
        }
        return null;
    }

    /**
     * The four samples the other side hands over, read one by one.
     *
     * The point is not that they parse - it is which reading each one lands on, since
     * that is the whole output of this layer: a file full of readings is worth nothing
     * if the one that gets used is the wrong one.
     */
    @Test
    public void theHandoverSamplesComeOutAsExpected() throws Exception {
        File docs = docsDirectory();
        Assume.assumeTrue("没有样张目录，跳过", docs != null);
        List<String> names = java.util.Arrays.asList("new_01", "new_140", "new_05", "kiechau");
        StringBuilder summary = new StringBuilder("样张（" + docs + "）：");
        int found = 0;
        for (String name : names) {
            File file = new File(docs, name + ".mgmcs.json");
            if (!file.isFile()) continue;
            found++;
            List<String> journal = new ArrayList<>();
            WorkbenchGrid grid = WorkbenchGrid.from(
                    new JSONObject(new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8)),
                    journal);
            assertNotNull(name + " 读不出网格：" + journal, grid);
            summary.append("\n  ").append(name).append(" → ").append(grid.reading)
                    .append(" / ").append(grid.shape)
                    .append(" / 素材 ").append(grid.audioSource.isEmpty() ? "没写" : grid.audioSource)
                    .append(" / ").append(grid.beatCount()).append(" 个拍点")
                    .append(" / 首条 ").append(String.format(java.util.Locale.US, "%.3f", grid.firstBeat()))
                    .append(" / 到 ").append(String.format(java.util.Locale.US, "%.1f", grid.lastBeat()))
                    .append(grid.caution ? " / 谨慎" : " / 干净");
        }
        System.out.println(summary);
        assertTrue("样张不在 " + docs, found > 0);
    }

    @Test
    public void everyRealFileLeavesAGridOrSaysWhyNot() throws Exception {
        File docs = docsDirectory();
        Assume.assumeTrue("这台机器上没有 mgmcs_validator/static/docs，跳过", docs != null);
        File[] files = docs.listFiles();
        assertNotNull(files);

        Map<String, Integer> chosen = new TreeMap<>();
        List<String> refused = new ArrayList<>();
        int withGrid = 0;
        int total = 0;
        for (File file : files) {
            if (!file.isFile() || !file.getName().endsWith(".mgmcs.json")) continue;
            total++;
            String text = new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8);
            List<String> journal = new ArrayList<>();
            WorkbenchGrid grid = WorkbenchGrid.from(new JSONObject(text), journal);
            if (grid == null) {
                refused.add(file.getName());
                continue;
            }
            withGrid++;
            Integer was = chosen.get(grid.reading);
            chosen.put(grid.reading, was == null ? 1 : was + 1);

            assertTrue(file.getName(), grid.beatsPerBar >= 1 && grid.beatsPerBar <= 12);
            assertTrue(file.getName() + " 的首条网格线不在自己的覆盖范围里", grid.covers(grid.firstBeat()));
            double previous = grid.beat(0);
            for (int i = 1; i < grid.beatCount(); i++) {
                double at = grid.beat(i);
                assertTrue(file.getName() + " 第 " + i + " 个拍点时是 " + at + "，在 " + previous + " 之前",
                        at > previous);
                previous = at;
            }
        }

        StringBuilder summary = new StringBuilder();
        summary.append("真实文件读出来 ").append(withGrid).append('/').append(total).append(" 首有网格");
        for (Map.Entry<String, Integer> entry : chosen.entrySet()) {
            summary.append("\n  用了 ").append(entry.getKey()).append(" = ").append(entry.getValue()).append(" 首");
        }
        if (!refused.isEmpty()) {
            summary.append("\n  一条读法都没有的：").append(refused);
        }
        System.out.println(summary);
        assertTrue("整库能读出网格的歌太少：" + withGrid + '/' + total, withGrid >= total * 0.8);
        assertTrue("workbench 的读法一条都没用上：" + chosen, chosen.containsKey("workbench"));
    }

    /**
     * Why the best-ranked reading loses, when it does.
     *
     * The reader says so in its journal, and this is what that journal is for: "the
     * import is using BeatThis instead of workbench" is only actionable with the
     * reason attached, and only the files can say whether the reason is a rule of
     * mine that is too strict or a reading that really is broken.
     */
    @Test
    public void whyTheWorkbenchReadingIsPassedOver() throws Exception {
        File docs = docsDirectory();
        Assume.assumeTrue("这台机器上没有 mgmcs_validator/static/docs，跳过", docs != null);
        File[] files = docs.listFiles();
        assertNotNull(files);

        Map<String, Integer> reasons = new TreeMap<>();
        int passedOver = 0;
        int looked = 0;
        for (File file : files) {
            if (!file.isFile() || !file.getName().endsWith(".mgmcs.json")) continue;
            looked++;
            String text = new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8);
            List<String> journal = new ArrayList<>();
            WorkbenchGrid grid = WorkbenchGrid.from(new JSONObject(text), journal);
            if (grid == null || "workbench".equals(grid.reading)) continue;
            for (String line : journal) {
                if (!line.startsWith("workbench")) continue;
                passedOver++;
                String key = line.length() > 90 ? line.substring(0, 90) : line;
                Integer was = reasons.get(key);
                reasons.put(key, was == null ? 1 : was + 1);
            }
        }
        StringBuilder summary = new StringBuilder("workbench 被让过的理由（共 " + passedOver + " 条）");
        for (Map.Entry<String, Integer> entry : reasons.entrySet()) {
            summary.append("\n  ").append(entry.getValue()).append(" 次  ").append(entry.getKey());
        }
        System.out.println(summary);
        assertTrue("一个文件都没看：" + docs, looked > 0);
    }

    /**
     * What would change if a suspect workbench reading were allowed anyway.
     *
     * The Workbench's own note says its source-health rule is not a verdict on the
     * reading: workbench goes suspect when Beat This! and the drum onsets disagree,
     * and it says so in those words. If that rule is what is throwing its bar lines
     * away, this is how many songs that costs - measured, not guessed.
     */
    @Test
    public void whatWouldChangeIfASuspectWorkbenchReadingWereAllowed() throws Exception {
        File docs = docsDirectory();
        Assume.assumeTrue("这台机器上没有 mgmcs_validator/static/docs，跳过", docs != null);
        File[] files = docs.listFiles();
        assertNotNull(files);

        int nowBeatThis = 0;
        int wouldUseWorkbench = 0;
        for (File file : files) {
            if (!file.isFile() || !file.getName().endsWith(".mgmcs.json")) continue;
            String text = new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8);
            WorkbenchGrid now = WorkbenchGrid.from(new JSONObject(text), new ArrayList<String>());
            if (now == null || !"BeatThis".equals(now.reading)) continue;
            nowBeatThis++;
            JSONObject lenient = new JSONObject(text);
            JSONArray again = lenient.optJSONArray("calibrations");
            for (int i = 0; again != null && i < again.length(); i++) {
                JSONObject item = again.optJSONObject(i);
                if (item == null) continue;
                String name = item.optString("reading", item.optString("calibrationId", ""));
                if ("workbench".equals(name)) item.put("health", "ok");
            }
            WorkbenchGrid ifAllowed = WorkbenchGrid.from(lenient, new ArrayList<String>());
            if (ifAllowed != null && "workbench".equals(ifAllowed.reading)) wouldUseWorkbench++;
        }
        System.out.println("现在用 BeatThis 的 " + nowBeatThis + " 首里，"
                + wouldUseWorkbench + " 首会改用 workbench（若 suspect 的 workbench 也算可用）");
        assertTrue(nowBeatThis > 0);
    }
}
