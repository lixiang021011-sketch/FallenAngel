package com.fallenangel.overlaybeat;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import java.io.File;
import java.util.List;

/**
 * The reduction from what a MIDI file says to what the player is asked to do.
 *
 * This is where taste lives, so each rule is stated as a test rather than left
 * as an emergent property of a long loop.
 */
public class MidiChartTest {

    private static MidiFile.RawNote note(int pitch, int velocity, long startTick, long endTick) {
        return new MidiFile.RawNote(0, 0, pitch, velocity, -1, startTick, endTick, "Piano");
    }

    @Test
    public void timesAreSecondsOnTheTempoMap() throws Exception {
        MidiFile midi = MidiFile.parse(MidiFixtures.smf(0, 480,
                new MidiFixtures.Track()
                        .delta(0).noteOn(0, 60, 90)
                        .delta(480).noteOff(0, 60)
                        .delta(480).noteOn(0, 62, 90)
                        .delta(480).noteOff(0, 62)
                        .delta(0).end().bytes()));

        MidiChart chart = MidiChart.build(midi, new MidiChart.Config());
        assertEquals(2, chart.notes.size());
        assertEquals(0.0, chart.notes.get(0).time, 1e-9);
        assertEquals(1.0, chart.notes.get(1).time, 1e-9);
        assertEquals(120.0, chart.bpm, 1e-9);
    }

    @Test
    public void theOffsetMovesTheWholeChart() throws Exception {
        MidiFile midi = MidiFile.parse(MidiFixtures.smf(0, 480,
                new MidiFixtures.Track().delta(0).noteOn(0, 60, 90)
                        .delta(480).noteOff(0, 60).delta(0).end().bytes()));

        MidiChart.Config config = new MidiChart.Config();
        config.offsetSeconds = 4.25;
        MidiChart chart = MidiChart.build(midi, config);
        assertEquals(4.25, chart.notes.get(0).time, 1e-9);
    }

    @Test
    public void aChordIsOnePressForOneFinger() {
        MidiFile midi = midiWith(note(60, 90, 0, 100), note(64, 90, 0, 100), note(67, 90, 0, 100));

        MidiChart chart = MidiChart.build(midi, new MidiChart.Config());
        assertEquals(1, chart.notes.size());
        assertEquals(2, chart.mergedSimultaneous);
    }

    @Test
    public void notesTooCloseTogetherAreDroppedAndCounted() {
        // 40 ticks at 120 bpm is 42 ms: under the 100 ms one finger needs.
        MidiFile midi = midiWith(note(60, 90, 0, 40), note(62, 90, 40, 80), note(64, 90, 80, 120));

        MidiChart chart = MidiChart.build(midi, new MidiChart.Config());
        assertEquals(1, chart.notes.size());
        assertEquals(2, chart.droppedTooFast);
    }

    @Test
    public void aNoteHeldLongEnoughBecomesAHold() {
        MidiFile midi = midiWith(note(60, 90, 0, 480), note(62, 90, 480, 520));

        MidiChart chart = MidiChart.build(midi, new MidiChart.Config());
        assertEquals(ChartNote.Kind.HOLD, chart.notes.get(0).kind);
        assertEquals(0.5, chart.notes.get(0).duration, 1e-9);
        assertEquals(ChartNote.Kind.TAP, chart.notes.get(1).kind);
        assertEquals(1, chart.holdCount);
    }

    @Test
    public void velocityDecidesWhatIsAnAccent() {
        MidiFile midi = midiWith(note(60, 127, 0, 40), note(60, 40, 480, 520));

        MidiChart chart = MidiChart.build(midi, new MidiChart.Config());
        assertTrue(chart.notes.get(0).accent);
        assertTrue(!chart.notes.get(1).accent);
        assertEquals(1, chart.accentCount);
    }

    @Test
    public void moreLanesSpreadNotesByPitchRegister() {
        MidiFile midi = midiWith(note(40, 90, 0, 100), note(90, 90, 480, 580));

        MidiChart.Config config = new MidiChart.Config();
        config.lanes = 4;
        MidiChart chart = MidiChart.build(midi, config);
        assertEquals(0, chart.notes.get(0).lane);
        assertEquals(3, chart.notes.get(1).lane);
    }

    @Test
    public void oneLanePutsEverythingOnTheSameLane() {
        MidiFile midi = midiWith(note(30, 90, 0, 100), note(100, 90, 480, 580));

        MidiChart chart = MidiChart.build(midi, new MidiChart.Config());
        assertEquals(0, chart.notes.get(0).lane);
        assertEquals(0, chart.notes.get(1).lane);
    }

    @Test
    public void aTrackFilterKeepsOneInstrument() throws Exception {
        MidiFile midi = MidiFile.parse(MidiFixtures.smf(1, 480,
                new MidiFixtures.Track().delta(0).name("PART DRUMS").delta(0)
                        .noteOn(0, 36, 110).delta(240).noteOff(0, 36)
                        .delta(0).noteOn(0, 38, 110).delta(240).noteOff(0, 38)
                        .delta(0).end().bytes(),
                new MidiFixtures.Track().delta(0).name("PART GUITAR").delta(0)
                        .noteOn(0, 60, 110).delta(480).noteOff(0, 60)
                        .delta(0).end().bytes()));

        MidiChart.Config config = new MidiChart.Config();
        config.trackNameContains = "drums";
        MidiChart chart = MidiChart.build(midi, config);
        assertEquals(2, chart.notes.size());
        assertEquals(2, chart.offered);
    }

    @Test
    public void aRealMidiFileBecomesAPlayableChart() {
        List<File> files = MidiFileTest.realMidiFiles(3);
        if (files.isEmpty()) {
            System.out.println("no MIDI files to check on this machine, skipping");
            return;
        }
        for (File file : files) {
            MidiChart chart;
            try {
                chart = MidiChart.fromFile(file, new MidiChart.Config());
            } catch (Exception e) {
                throw new AssertionError(file + " could not be turned into a chart", e);
            }
            assertTrue(file.getName() + " produced nothing playable", chart.notes.size() > 0);
            // The reduction never invents a note and never grows the file.
            assertTrue(file.getName() + " invented notes", chart.notes.size() <= chart.offered);
            for (int i = 1; i < chart.notes.size(); i++) {
                assertTrue(file.getName() + " is out of order",
                        chart.notes.get(i).time >= chart.notes.get(i - 1).time);
            }
            System.out.println("  " + file.getParentFile().getName() + ": " + chart.summary());
        }
    }

    private static MidiFile midiWith(MidiFile.RawNote... notes) {
        // Built through the real reader instead of a hand-made MidiFile, so the
        // fixture cannot drift from what the reader actually produces.
        List<Object[]> events = new java.util.ArrayList<>();
        for (MidiFile.RawNote note : notes) {
            events.add(new Object[] {note.startTick,
                    new int[] {0x90 | note.channel, note.pitch, note.velocity}});
            events.add(new Object[] {note.endTick,
                    new int[] {0x80 | note.channel, note.pitch, 0}});
        }
        events.sort((a, b) -> Long.compare((Long) a[0], (Long) b[0]));

        MidiFixtures.Track track = new MidiFixtures.Track();
        long tick = 0;
        for (Object[] event : events) {
            long at = (Long) event[0];
            track.delta(at - tick);
            track.raw((int[]) event[1]);
            tick = at;
        }
        track.delta(0).end();
        try {
            return MidiFile.parse(MidiFixtures.smf(0, 480, track.bytes()));
        } catch (MidiFile.MidiParseException e) {
            throw new AssertionError("the fixture itself is malformed", e);
        }
    }
}
