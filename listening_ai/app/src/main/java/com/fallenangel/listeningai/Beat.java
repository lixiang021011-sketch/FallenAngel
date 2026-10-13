package com.fallenangel.listeningai;

/**
 * One beat of music, as far as the analysis can tell.
 *
 * This is deliberately not a chart note. It is a description of the music - the
 * when, and how much is happening there - and it knows nothing about lanes,
 * note types, difficulty or how the player will be asked to hit it. Everything
 * that decides *what the player has to do* belongs to {@link ChartBuilder}.
 */
public final class Beat {
    /** Seconds since capture started, on the same clock the game plays on. */
    public final double time;
    /** Index of this position on the grid, counting from the phase origin. */
    public final int positionIndex;
    /** Position within the bar, so patterns can treat beat 1 differently from beat 3. */
    public final int slot;
    /** Position within the beat: 0 is the beat itself, 1..n the places after it. */
    public final int subdivision;
    /**
     * Whether the music is known to play a figure on this beat, and whether this
     * position is part of it. The figure is the unit the chart predicts: without
     * one, positions are taken one at a time on their own evidence.
     */
    public final boolean figureKnown;
    public final boolean inFigure;
    /**
     * Learned energy for this position: how much usually happens here. -1 while
     * this part of the bar has not been heard often enough to have an opinion.
     */
    public final double energy;
    /** The same, for the two bands separately, or -1 while unknown. */
    public final double kick;
    public final double snare;
    /** Learned energy across all slots, for comparing against. */
    public final double averageEnergy;

    Beat(double time, int positionIndex, int slot, int subdivision,
         boolean figureKnown, boolean inFigure,
         double energy, double kick, double snare, double averageEnergy) {
        this.time = time;
        this.positionIndex = positionIndex;
        this.slot = slot;
        this.subdivision = subdivision;
        this.figureKnown = figureKnown;
        this.inFigure = inFigure;
        this.energy = energy;
        this.kick = kick;
        this.snare = snare;
        this.averageEnergy = averageEnergy;
    }

    /** Whether this position usually carries a kick, a snare, or nothing. */
    public boolean kickDominant() {
        return kick >= 0 && kick >= snare;
    }

    /**
     * Whether anything usually happens on this beat, relative to the rest of the
     * bar. Silence on a beat is knowledge too - it is how a chart avoids asking
     * the player to hit nothing.
     *
     * Before the bar has been heard often enough to have an opinion, only the
     * beat itself is taken. "Do not filter what is not known" was written when
     * the only positions were the beats, where it meant "four notes a bar"; on a
     * sixteenth grid the same rule means "sixteen notes a bar", which is not an
     * opinion about the music but the absence of one.
     */
    public boolean carriesHit(double share) {
        if (energy < 0 || averageEnergy <= 0) return subdivision == 0;
        return energy >= averageEnergy * share;
    }
}
