package com.fallenangel.overlaybeat;

import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/**
 * Turns a MIDI file into the notes this game plays.
 *
 * This is the second half of "MIDI -> ChartNote": {@link MidiFile} says what the
 * file contains, and this decides what the player is asked to do about it. All
 * the taste lives here - how close two presses may be, what counts as loud, when
 * a note is long enough to hold, how notes spread over lanes - and every
 * decision is counted, so a reduction is never silent.
 *
 * Chart times are seconds from the start of the audio. A MIDI file usually
 * starts where its transcription starts, which is not always where the
 * recording does, so the offset is a knob rather than an assumption.
 */
public final class MidiChart {

    /** Presses closer together than this cannot both be meant by one finger. */
    public static final double DEFAULT_MIN_GAP_SECONDS = 0.10;
    /** A note at least this long becomes a press-and-hold. */
    public static final double DEFAULT_HOLD_SECONDS = 0.35;
    /** A note at this velocity or above is a real hit, not a filler. */
    public static final int DEFAULT_ACCENT_VELOCITY = 100;
    /** Two note-ons this close are one moment, whatever the file says. */
    private static final double SIMULTANEOUS_SECONDS = 0.001;

    public static final class Config {
        /** Added to every note time, to move the chart onto the recording. */
        public double offsetSeconds;
        public double minGapSeconds = DEFAULT_MIN_GAP_SECONDS;
        public double holdSeconds = DEFAULT_HOLD_SECONDS;
        public int accentVelocity = DEFAULT_ACCENT_VELOCITY;
        /** 1 = everything on one lane; more spreads notes by pitch register. */
        public int lanes = 1;
        /** Keep only notes from tracks whose name contains this, or null for all. */
        public String trackNameContains;
        /** Keep only this MIDI channel, or -1 for all. */
        public int channel = -1;
    }

    public final List<ChartNote> notes;
    /** The tempo in force at the first note, for the readout. */
    public final double bpm;
    public final int lanes;
    public final double firstNoteTime;
    public final double lastNoteTime;
    /** Notes the MIDI offered, after the track/channel filters. */
    public final int offered;
    /** Notes that were at the same moment as the one before them. */
    public final int mergedSimultaneous;
    /** Notes dropped because a single finger already had one to press. */
    public final int droppedTooFast;
    public final int holdCount;
    public final int accentCount;
    public final int truncatedNotes;

    private MidiChart(List<ChartNote> notes, double bpm, int lanes, double firstNoteTime,
                      double lastNoteTime, int offered, int mergedSimultaneous,
                      int droppedTooFast, int holdCount, int accentCount, int truncatedNotes) {
        this.notes = notes;
        this.bpm = bpm;
        this.lanes = lanes;
        this.firstNoteTime = firstNoteTime;
        this.lastNoteTime = lastNoteTime;
        this.offered = offered;
        this.mergedSimultaneous = mergedSimultaneous;
        this.droppedTooFast = droppedTooFast;
        this.holdCount = holdCount;
        this.accentCount = accentCount;
        this.truncatedNotes = truncatedNotes;
    }

    public static MidiChart fromFile(File file, Config config)
            throws IOException, MidiFile.MidiParseException {
        return build(MidiFile.parse(file), config);
    }

    public static MidiChart build(MidiFile midi, Config config) {
        int lanes = Math.max(1, config.lanes);
        String wanted = config.trackNameContains == null
                ? null : config.trackNameContains.toLowerCase(Locale.ROOT);

        List<MidiFile.RawNote> kept = new ArrayList<>();
        int lowPitch = 127;
        int highPitch = 0;
        for (MidiFile.RawNote note : midi.notes) {
            if (config.channel >= 0 && note.channel != config.channel) continue;
            if (wanted != null && !note.trackName.toLowerCase(Locale.ROOT).contains(wanted)) continue;
            kept.add(note);
            lowPitch = Math.min(lowPitch, note.pitch);
            highPitch = Math.max(highPitch, note.pitch);
        }
        kept.sort((a, b) -> Long.compare(a.startTick, b.startTick));

        List<ChartNote> out = new ArrayList<>();
        int merged = 0;
        int dropped = 0;
        int holds = 0;
        int accents = 0;
        double lastKept = Double.NEGATIVE_INFINITY;
        double first = -1;
        double last = 0;
        double bpm = 120;

        for (MidiFile.RawNote note : kept) {
            double start = midi.secondsAt(note.startTick) + config.offsetSeconds;
            double seconds = midi.secondsAt(note.endTick) - midi.secondsAt(note.startTick);

            if (out.isEmpty()) {
                bpm = midi.bpmAt(note.startTick);
            }
            if (start - lastKept < SIMULTANEOUS_SECONDS) {
                // A chord on one lane is one press; the finger cannot tell the
                // notes apart, and neither can a player.
                merged++;
                continue;
            }
            if (start - lastKept < config.minGapSeconds) {
                dropped++;
                continue;
            }

            boolean accent = note.velocity >= config.accentVelocity;
            if (accent) accents++;
            ChartNote.Kind kind = ChartNote.Kind.TAP;
            double duration = 0;
            if (seconds >= config.holdSeconds) {
                kind = ChartNote.Kind.HOLD;
                duration = seconds;
                holds++;
            }

            out.add(new ChartNote(start, laneFor(note.pitch, lowPitch, highPitch, lanes),
                    kind, accent, duration));
            lastKept = start;
            if (first < 0) first = start;
            last = start;
        }

        return new MidiChart(out, bpm, lanes, Math.max(0, first), last, kept.size(),
                merged, dropped, holds, accents, midi.truncatedNotes);
    }

    /**
     * A pitch's lane, by register rather than by pitch class.
     *
     * Pitch class would scatter a chord across every lane; register keeps low
     * notes low, which is what a player's hands expect. With one lane this is
     * always 0, and the reduction is the only thing the game currently plays.
     */
    private static int laneFor(int pitch, int lowPitch, int highPitch, int lanes) {
        if (lanes <= 1) return 0;
        int span = highPitch - lowPitch + 1;
        if (span <= 1) return 0;
        int lane = (pitch - lowPitch) * lanes / span;
        return Math.max(0, Math.min(lanes - 1, lane));
    }

    /** One line for the log: what the file offered and what survived. */
    public String summary() {
        return String.format(Locale.US,
                "%d notes of %d offered, %d merged, %d too fast, %d holds, "
                        + "%d accents, bpm %.1f",
                notes.size(), offered, mergedSimultaneous, droppedTooFast, holdCount,
                accentCount, bpm);
    }
}
