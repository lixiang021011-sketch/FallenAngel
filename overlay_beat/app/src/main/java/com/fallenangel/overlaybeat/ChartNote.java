package com.fallenangel.overlaybeat;

/**
 * One thing the player has to do.
 *
 * The fields beyond `time` exist from the start even though this version only
 * ever produces a tap on lane 0. Adding holds, extra lanes or accents later
 * should mean filling these in, not rebuilding the pipeline that produces and
 * plays them.
 */
public final class ChartNote {
    public enum Kind {
        /** A single tap. */
        TAP,
        /** Press and hold for `duration` seconds. */
        HOLD
    }

    public final double time;
    public final int lane;
    public final Kind kind;
    /** A real hit lands here, as opposed to a beat that merely usually has one. */
    public final boolean accent;
    /** Only meaningful for HOLD. */
    public final double duration;

    public ChartNote(double time, int lane, Kind kind, boolean accent, double duration) {
        this.time = time;
        this.lane = lane;
        this.kind = kind;
        this.accent = accent;
        this.duration = duration;
    }

    public static ChartNote tap(double time, int lane, boolean accent) {
        return new ChartNote(time, lane, Kind.TAP, accent, 0);
    }
}
