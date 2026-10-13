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
    static volatile long positionGeneration;
    static volatile double durationSeconds = -1;

    private Playback() {}
}
