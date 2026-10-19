package com.fallenangel.overlaybeat;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

/**
 * The chart reader is the join between FallenAngel's finished chart pipeline and
 * this game. A silent mistake here would look like "the chart is wrong" rather
 * than "the reader is wrong", so the reduction is pinned down explicitly.
 */
public class ChartFileTest {
    private static final String CHART = "{"
            + "\"metadata\":{\"songName\":\"Bit Shift\",\"songArtist\":\"Someone\","
            + "\"difficulty\":\"Expert\",\"bpm\":130.0,\"offset\":0.02},"
            + "\"notes\":["
            + "{\"type\":\"tap\",\"lane\":2,\"time\":2.296,\"duration\":0.0},"
            + "{\"type\":\"tap\",\"lane\":0,\"time\":3.000,\"duration\":0.0},"
            + "{\"type\":\"tap\",\"lane\":1,\"time\":3.010,\"duration\":0.0},"
            + "{\"type\":\"hold\",\"lane\":3,\"time\":5.000,\"duration\":1.5},"
            + "{\"type\":\"flick\",\"lane\":4,\"time\":7.000,\"duration\":0.0,\"direction\":\"up\"},"
            + "{\"type\":\"slide\",\"lane\":4,\"time\":9.000,\"duration\":2.0}"
            + "]}";

    @Test
    public void metadataIsRead() throws Exception {
        ChartFile chart = ChartFile.parse(CHART, -1);
        assertEquals("Bit Shift", chart.songName);
        assertEquals("Someone", chart.songArtist);
        assertEquals(130.0, chart.bpm, 1e-9);
        assertEquals(0.02, chart.offset, 1e-9);
    }

    @Test
    public void theChartOffsetIsAppliedToEveryNote() throws Exception {
        ChartFile chart = ChartFile.parse(CHART, -1);
        assertEquals(2.296 + 0.02, chart.notes.get(0).time, 1e-9);
    }

    @Test
    public void everyLaneFoldsOntoTheOneLane() throws Exception {
        ChartFile chart = ChartFile.parse(CHART, -1);
        for (ChartNote note : chart.notes) assertEquals(0, note.lane);
    }

    @Test
    public void lanesTenMillisecondsApartBecomeOneTap() throws Exception {
        ChartFile chart = ChartFile.parse(CHART, -1);
        assertEquals("two lanes 10 ms apart are one moment for one finger",
                1, chart.mergedSimultaneous);
        long nearThree = chart.notes.stream()
                .filter(n -> n.time > 3.0 && n.time < 3.1).count();
        assertEquals(1, nearThree);
    }

    @Test
    public void filteringToOneLaneCountsWhatItDropped() throws Exception {
        ChartFile chart = ChartFile.parse(CHART, 2);
        assertEquals(1, chart.notes.size());
        assertEquals(2.316, chart.notes.get(0).time, 1e-9);
        assertTrue("the dropped notes are reported, not silently lost",
                chart.droppedByLane == 5);
    }

    @Test
    public void holdsKeepTheirDurationAndTapTypesAreCountedAsReduced() throws Exception {
        ChartFile chart = ChartFile.parse(CHART, -1);
        ChartNote hold = chart.notes.stream()
                .filter(n -> n.kind == ChartNote.Kind.HOLD).findFirst().orElse(null);
        assertTrue("a hold survives as a hold", hold != null);
        assertEquals(1.5, hold.duration, 1e-9);
        assertEquals("flick and slide need gestures this game has not got",
                2, chart.reducedToTap);
    }

    @Test
    public void notesComeOutInTimeOrder() throws Exception {
        ChartFile chart = ChartFile.parse(CHART, -1);
        for (int i = 1; i < chart.notes.size(); i++) {
            assertTrue(chart.notes.get(i).time >= chart.notes.get(i - 1).time);
        }
    }

    /**
     * The synthetic cases above pin the rules; this one checks the rules against
     * the charts FallenAngel actually produces, when the repository is present.
     */
    @Test
    public void realFallenAngelChartsParse() throws Exception {
        File dir = new File("../../Assets/Resources/Charts");
        File[] charts = dir.listFiles((d, name) -> name.endsWith(".json"));
        if (charts == null || charts.length == 0) {
            System.out.println("no real charts available here, skipping");
            return;
        }
        System.out.println("parsing " + charts.length + " real charts");
        for (File file : charts) {
            String json = new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8);
            ChartFile folded = ChartFile.parse(json, -1);
            ChartFile oneLane = ChartFile.parse(json, 2);
            // Invariants that must hold for every chart, tiny test fixtures
            // included: the reduction never invents notes and never grows.
            assertTrue(file.getName() + " exploded the chart",
                    folded.notes.size() < 5000);
            assertTrue(file.getName() + " grew when filtering to one lane",
                    oneLane.notes.size() <= folded.notes.size());
            assertTrue(file.getName() + " lost everything and kept nothing",
                    folded.notes.size() == 0 || oneLane.notes.size() > 0
                            || folded.droppedByLane + folded.mergedSimultaneous > 0);
            System.out.printf("  %-42s folded=%-4d oneLane=%-4d (merged %d, to tap %d)%n",
                    file.getName(), folded.notes.size(), oneLane.notes.size(),
                    folded.mergedSimultaneous, folded.reducedToTap);
        }
    }
}
