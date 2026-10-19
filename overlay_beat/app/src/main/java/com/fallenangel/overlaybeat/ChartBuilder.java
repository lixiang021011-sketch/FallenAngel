package com.fallenangel.overlaybeat;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.List;

/**
 * Turns musical events into the chart the player actually plays.
 *
 * This is the layer where chart complexity belongs, and it is deliberately
 * separate from the audio analysis: the analysis says "a beat lands here and
 * this slot usually has something in it", and every decision about what that
 * means for the player - whether it becomes a note, which lane, whether it is an
 * accent, a hold, or part of a run - happens here and nowhere else.
 *
 * Keeping it in its own class also makes it testable without audio: feed it
 * beats, check the chart.
 */
public final class ChartBuilder {
    /** A position needs this share of the average energy to earn a note. */
    public static final double DEFAULT_SLOT_SHARE = 0.45;
    /** Energy this far above the average makes a note an accent. */
    public static final double ACCENT_SHARE = 1.6;

    /** A note at most every N positions, so 1 = wherever the music has something. */
    private int stride = 1;
    private int lanes = 1;
    /** Written from the settings screen, read on the capture thread. */
    private volatile double slotShare = DEFAULT_SLOT_SHARE;

    private final ArrayDeque<ChartNote> pending = new ArrayDeque<>();

    /**
     * Every position the music has something on becomes a note, so the density
     * comes from the music rather than from a fixed rate. Raise it to thin the
     * chart out without changing which positions are considered.
     */
    public void setStride(int stride) {
        this.stride = Math.max(1, stride);
    }

    public void setLanes(int lanes) {
        this.lanes = Math.max(1, lanes);
    }

    public int lanes() {
        return lanes;
    }

    /** How much usually has to happen on a position for it to earn a note. */
    public void setSlotShare(double share) {
        this.slotShare = Math.max(0, share);
    }

    public double slotShare() {
        return slotShare;
    }

    /** Offer one beat to the chart. Returns the note it produced, if any. */
    public ChartNote accept(Beat beat) {
        if (Math.floorMod(beat.positionIndex, stride) != 0) return null;

        // Once the music is known to play a figure on this beat, the figure is
        // the answer and the per-position evidence is not asked: asking it as
        // well is what fills the bar with every place that ever had something,
        // which is the flat row of notes a groove is the opposite of.
        if (beat.figureKnown) {
            if (!beat.inFigure) return null;
        } else if (!beat.carriesHit(slotShare)) {
            return null;
        }

        // Which band the position belongs to is the only thing here that is a
        // musical fact rather than a taste; everything else - how many lanes,
        // what counts as an accent - is policy and is stated as such.
        int lane = laneFor(beat);
        boolean accent = beat.averageEnergy > 0
                && beat.energy >= beat.averageEnergy * ACCENT_SHARE;
        ChartNote note = ChartNote.tap(beat.time, lane, accent);
        pending.add(note);
        return note;
    }

    private int laneFor(Beat beat) {
        if (lanes <= 1) return 0;
        if (beat.kick < 0 && beat.snare < 0) return 0;
        return beat.kickDominant() ? 0 : lanes - 1;
    }

    /** Notes produced since the last call, oldest first. */
    public List<ChartNote> drain() {
        if (pending.isEmpty()) return java.util.Collections.emptyList();
        List<ChartNote> out = new ArrayList<>(pending);
        pending.clear();
        return out;
    }
}
