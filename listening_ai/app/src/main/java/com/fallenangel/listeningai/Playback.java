package com.fallenangel.listeningai;

/**
 * What the player is doing, as reported by the system rather than guessed from
 * the audio.
 *
 * Silence in the captured stream can mean "paused", "between tracks", "quiet
 * passage" or "the app stopped capturing". Volume alone cannot tell them apart,
 * and guessing cost seconds of wrong notes on every song change. The media
 * session states it outright, so the game listens to that instead.
 *
 * The capture loop reads these fields; the watcher service writes them.
 */
public final class Playback {
    /** True while a player is actually being observed through the media session. */
    static volatile boolean available;
    /** Changes whenever the track changes; artist|title|duration. */
    static volatile String trackKey = "";
    static volatile String trackTitle = "";
    static volatile String trackArtist = "";
    static volatile boolean playing = true;
    /** Bumped when the playhead jumps, so the grid can be re-phased. */
    static volatile long seekGeneration;
    /**
     * Where the song is, in seconds.
     *
     * This is what lets a pre-made chart line up with music the game does not
     * own: the chart counts from the start of the song, and so does the player.
     * It only needs to be roughly right - the constant part of the error is the
     * same latency the calibration already fits - and `positionGeneration` marks
     * each fresh report so a stale one is never trusted twice.
     */
    static volatile double positionSeconds = -1;
    /** When that position was written, on the same clock as SystemClock.elapsedRealtime. */
    static volatile long positionAtRealtime;
    static volatile long positionGeneration;
    static volatile double durationSeconds = -1;

    /**
     * Where the song is now, with the player's own reports filled in between.
     *
     * A player publishes a position when it feels like it, and between those reports
     * the raw number stands still: measured 2026-10-14, that is a staircase about a
     * second wide. Anything that has to land on a beat - the bar flash above all -
     * reads this instead, which walks with the clock in between and snaps back to the
     * player the moment it says something again.
     */
    static double positionNow() {
        double at = positionSeconds;
        if (at < 0) return at;
        if (!playing || positionAtRealtime == 0) return at;
        double since = (android.os.SystemClock.elapsedRealtime() - positionAtRealtime) / 1000.0;
        // A player that has said nothing for two seconds has stopped talking, and
        // walking on without it would be inventing a playhead rather than following one.
        if (since <= 0) return at;
        return at + Math.min(since, 2.0);
    }

    private Playback() {}
}
