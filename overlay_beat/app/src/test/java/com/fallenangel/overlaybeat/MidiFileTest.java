package com.fallenangel.overlaybeat;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertTrue;
import static org.junit.Assert.fail;

import org.junit.Test;

import java.io.File;
import java.util.ArrayList;
import java.util.List;

/**
 * The MIDI reader, pinned down.
 *
 * A file that parses into plausible-looking but wrong notes would look like "the
 * chart is bad" rather than "the reader is bad", so the decoding rules are
 * asserted one at a time, and then once more against real files.
 */
public class MidiFileTest {

    @Test
    public void headerAndTrackNamesAreRead() throws Exception {
        MidiFile midi = MidiFile.parse(MidiFixtures.smf(1, 480,
                new MidiFixtures.Track().delta(0).name("Conductor").delta(0).end().bytes(),
                new MidiFixtures.Track().delta(0).name("Piano").delta(0)
                        .noteOn(0, 60, 100).delta(480).noteOff(0, 60).delta(0).end().bytes()));

        assertEquals(1, midi.format);
        assertEquals(480, midi.ticksPerQuarter);
        assertEquals(2, midi.trackNames.size());
        assertEquals("Piano", midi.trackNames.get(1));
    }

    @Test
    public void aNoteOnAndItsNoteOffBecomeOneNote() throws Exception {
        MidiFile midi = MidiFile.parse(MidiFixtures.smf(0, 480,
                new MidiFixtures.Track().delta(0).noteOn(0, 60, 100)
                        .delta(480).noteOff(0, 60).delta(0).end().bytes()));

        assertEquals(1, midi.notes.size());
        MidiFile.RawNote note = midi.notes.get(0);
        assertEquals(60, note.pitch);
        assertEquals(100, note.velocity);
        assertEquals(0, note.startTick);
        assertEquals(480, note.endTick);
        assertEquals(0, note.track);
    }

    @Test
    public void aFileWithoutATempoIsPlayedAt120() throws Exception {
        MidiFile midi = MidiFile.parse(MidiFixtures.smf(0, 480,
                new MidiFixtures.Track().delta(0).noteOn(0, 60, 100)
                        .delta(480).noteOff(0, 60).delta(0).end().bytes()));

        assertEquals(120.0, midi.bpmAt(0), 1e-9);
        assertEquals(0.5, midi.secondsAt(480), 1e-9);
    }

    @Test
    public void tempoChangesMoveLaterNotesInTime() throws Exception {
        // Half a second at 120 bpm, then a full second for the same 480 ticks
        // once the file drops to 60.
        MidiFile midi = MidiFile.parse(MidiFixtures.smf(0, 480,
                new MidiFixtures.Track()
                        .delta(0).tempo(500_000)
                        .delta(0).noteOn(0, 60, 100)
                        .delta(480).noteOff(0, 60)
                        .delta(0).tempo(1_000_000)
                        .delta(0).noteOn(0, 62, 100)
                        .delta(480).noteOff(0, 62)
                        .delta(0).end().bytes()));

        assertEquals(1.5, midi.secondsAt(960), 1e-9);
        assertEquals(60.0, midi.bpmAt(480), 1e-9);
        // The second note starts the moment the tempo drops, so it lasts the
        // full second the slower tempo gives its 480 ticks.
        assertEquals(0.5, midi.secondsAt(midi.notes.get(1).startTick), 1e-9);
        assertEquals(1.5, midi.secondsAt(midi.notes.get(1).endTick), 1e-9);
    }

    @Test
    public void runningStatusCarriesTheLastStatusByte() throws Exception {
        // After 0x90 60 100, the pairs that follow are note-ons on the same
        // channel: velocity 0 is how a file says "off".
        MidiFile midi = MidiFile.parse(MidiFixtures.smf(0, 480,
                new MidiFixtures.Track()
                        .delta(0).noteOn(0, 60, 100)
                        .delta(10).raw(62, 100)
                        .delta(10).raw(60, 0)
                        .delta(10).raw(62, 0)
                        .delta(0).end().bytes()));

        assertEquals(2, midi.notes.size());
        assertEquals(60, midi.notes.get(0).pitch);
        assertEquals(0, midi.notes.get(0).startTick);
        assertEquals(20, midi.notes.get(0).endTick);
        assertEquals(62, midi.notes.get(1).pitch);
        assertEquals(10, midi.notes.get(1).startTick);
        assertEquals(30, midi.notes.get(1).endTick);
    }

    @Test
    public void aNoteOnWithZeroVelocityEndsTheNote() throws Exception {
        MidiFile midi = MidiFile.parse(MidiFixtures.smf(0, 480,
                new MidiFixtures.Track()
                        .delta(0).noteOn(0, 64, 90)
                        .delta(240).noteOn(0, 64, 0)
                        .delta(0).end().bytes()));

        assertEquals(1, midi.notes.size());
        assertEquals(240, midi.notes.get(0).endTick);
    }

    @Test
    public void aRepeatedNoteClosesTheOneThatOpenedLast() throws Exception {
        MidiFile midi = MidiFile.parse(MidiFixtures.smf(0, 480,
                new MidiFixtures.Track()
                        .delta(0).noteOn(0, 60, 100)
                        .delta(100).noteOn(0, 60, 100)
                        .delta(100).noteOff(0, 60)
                        .delta(100).noteOff(0, 60)
                        .delta(0).end().bytes()));

        assertEquals(2, midi.notes.size());
        // The note that opened first gets the last note-off, so the two overlap
        // rather than replacing each other.
        assertEquals(0, midi.notes.get(0).startTick);
        assertEquals(300, midi.notes.get(0).endTick);
        assertEquals(100, midi.notes.get(1).startTick);
        assertEquals(200, midi.notes.get(1).endTick);
    }

    @Test
    public void aNoteLeftOpenAtTheEndOfATrackIsKeptAndCounted() throws Exception {
        MidiFile midi = MidiFile.parse(MidiFixtures.smf(0, 480,
                new MidiFixtures.Track()
                        .delta(0).noteOn(0, 60, 100)
                        .delta(100).end().bytes()));

        assertEquals(1, midi.notes.size());
        assertEquals(100, midi.notes.get(0).endTick);
        assertEquals("the truncation is reported, not silent", 1, midi.truncatedNotes);
    }

    @Test
    public void everyTrackContributesItsNotes() throws Exception {
        MidiFile midi = MidiFile.parse(MidiFixtures.smf(1, 480,
                new MidiFixtures.Track().delta(0).name("Drums").delta(0)
                        .noteOn(9, 36, 110).delta(60).noteOff(9, 36).delta(0).end().bytes(),
                new MidiFixtures.Track().delta(0).name("Piano").delta(0)
                        .noteOn(0, 60, 80).delta(60).noteOff(0, 60).delta(0).end().bytes()));

        assertEquals(2, midi.notes.size());
        assertEquals("Drums", midi.notes.get(0).trackName);
        assertEquals(9, midi.notes.get(0).channel);
        assertEquals("Piano", midi.notes.get(1).trackName);
    }

    @Test
    public void smpteDivisionIsRefusedRatherThanGuessed() {
        // 0xE728: the high bit means SMPTE, which this reader does not do.
        try {
            MidiFile.parse(MidiFixtures.smf(0, 0xE728,
                    new MidiFixtures.Track().delta(0).end().bytes()));
            fail("SMPTE division should be refused");
        } catch (MidiFile.MidiParseException expected) {
            assertTrue(expected.getMessage().contains("SMPTE"));
        }
    }

    @Test
    public void somethingThatIsNotAMidiFileIsRefused() {
        try {
            MidiFile.parse(new byte[] {'R', 'I', 'F', 'F', 0, 0, 0, 0});
            fail("a file that does not start with MThd should be refused");
        } catch (MidiFile.MidiParseException expected) {
            assertTrue(expected.getMessage().contains("MThd"));
        }
    }

    /**
     * Real files, when this machine has some. Rock Band's note tracks are a
     * useful shape: many tracks, tempo changes, thousands of notes.
     */
    @Test
    public void realMidiFilesParse() throws Exception {
        List<File> files = realMidiFiles(8);
        if (files.isEmpty()) {
            System.out.println("no MIDI files to check on this machine, skipping");
            return;
        }
        for (File file : files) {
            MidiFile midi = MidiFile.parse(file);
            assertNotNull(midi.notes);
            assertTrue(file.getName() + " has no notes at all", midi.notes.size() > 0);
            assertEquals(file.getName() + " has a strange tick division",
                    true, midi.ticksPerQuarter > 0);
            System.out.printf("  %-28s tracks=%-3d notes=%-5d %6.1fs%n",
                    file.getParentFile().getName() + "/" + file.getName(),
                    midi.trackNames.size(), midi.notes.size(), midi.durationSeconds());
        }
    }

    static List<File> realMidiFiles(int limit) {
        File root = new File("../../_refs/chart_research/onyx/haskell/resources/rb3-updates");
        List<File> found = new ArrayList<>();
        collect(root, found, limit, 0);
        return found;
    }

    private static void collect(File dir, List<File> found, int limit, int depth) {
        if (dir == null || !dir.isDirectory() || depth > 3 || found.size() >= limit) return;
        File[] entries = dir.listFiles();
        if (entries == null) return;
        for (File entry : entries) {
            if (found.size() >= limit) return;
            if (entry.isDirectory()) {
                collect(entry, found, limit, depth + 1);
            } else {
                String name = entry.getName().toLowerCase(java.util.Locale.ROOT);
                if (name.endsWith(".mid") || name.endsWith(".midi")) found.add(entry);
            }
        }
    }
}
