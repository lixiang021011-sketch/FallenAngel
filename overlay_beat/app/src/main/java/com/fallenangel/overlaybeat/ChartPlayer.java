package com.fallenangel.overlaybeat;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.List;

/**
 * Plays a pre-made chart against music the game does not own.
 *
 * The chart counts seconds from the start of the song and so does the player, so
 * the only thing needed to line them up is where the song is right now. That
 * comes from the media session - coarse, reported every second or so - and the
 * remaining error is a constant, which is exactly the latency the calibration
 * already fits from the player's own taps. No new alignment machinery.
 *
 * Notes are handed over with the game's own clock, so from the view's point of
 * view a chart note and a generated one are the same thing.
 */
public final class ChartPlayer {
    public static final double LEAD_SECONDS = BeatDetector.SCHEDULE_LEAD_SECONDS;

    private final ArrayDeque<ChartNote> pending = new ArrayDeque<>();
    private List<ChartNote> notes;
    private String label = "";
    private int nextIndex;
    private double anchoredPosition = -1;
    private long anchoredGeneration = -1;
    private double anchoredAtCapture;
    /** Where the song is on its own clock, as of the last poll. */
    private double songTimeNow = -1;

    /** True when a chart is loaded and being played. */
    public boolean active() {
        return notes != null;
    }

    /** What is being played, for the readout: a chart name or a MIDI file name. */
    public String label() {
        return label;
    }

    /**
     * Where the song is now, on its own clock.
     *
     * The chart's own notes are expressed on the capture clock, but anything
     * that has to be looked up in the song - the waveform the lane draws - needs
     * the song's time, and the only thing that knows how the two relate is here.
     */
    public double songTimeNow() {
        return songTimeNow;
    }

    /**
     * @param loaded notes in song time, from a FallenAngel chart or a MIDI file
     * @param name   what to call it on screen
     */
    public void load(List<ChartNote> loaded, String name) {
        notes = loaded;
        label = name == null ? "" : name;
        nextIndex = 0;
        anchoredPosition = -1;
        anchoredGeneration = -1;
        pending.clear();
    }

    public void unload() {
        notes = null;
        label = "";
        pending.clear();
    }

    /**
     * @param captureNow seconds on the game's own clock
     * @param position   the song position from the player, or negative if unknown
     */
    public void poll(double captureNow, double position) {
        if (notes == null || position < 0) return;

        if (Playback.positionGeneration != anchoredGeneration) {
            anchoredGeneration = Playback.positionGeneration;
            double drift = anchoredPosition < 0 ? Double.MAX_VALUE
                    : Math.abs((position - anchoredPosition) - (captureNow - anchoredAtCapture));
            anchoredPosition = position;
            anchoredAtCapture = captureNow;
            if (drift > 0.25) {
                // The player jumped, or reported something quite different. The
                // notes already queued were placed against the old anchor, so
                // drop them and re-emit from the new one.
                pending.clear();
                nextIndex = firstNoteAfter(position);
            }
        }

        double playedOn = position + (captureNow - anchoredAtCapture);
        songTimeNow = playedOn;
        double horizon = playedOn + LEAD_SECONDS;
        while (nextIndex < notes.size()) {
            ChartNote note = notes.get(nextIndex);
            if (note.time > horizon) break;
            if (note.time >= playedOn - 0.5) {
                // Express the note on the capture clock: how far away it is, from
                // how far along the song is.
                double captureTime = captureNow + (note.time - playedOn);
                pending.add(new ChartNote(captureTime, 0, note.kind, true, note.duration));
            }
            nextIndex++;
        }
    }

    private int firstNoteAfter(double position) {
        for (int i = 0; i < notes.size(); i++) {
            if (notes.get(i).time >= position - 0.5) return i;
        }
        return notes.size();
    }

    public List<ChartNote> drain() {
        if (pending.isEmpty()) return java.util.Collections.emptyList();
        List<ChartNote> out = new ArrayList<>(pending);
        pending.clear();
        return out;
    }

    /** True when the whole chart has been handed over. */
    public boolean finished() {
        return notes != null && nextIndex >= notes.size();
    }
}
