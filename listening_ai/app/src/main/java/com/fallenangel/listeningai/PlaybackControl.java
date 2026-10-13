package com.fallenangel.listeningai;

import android.content.Context;
import android.media.AudioManager;
import android.os.SystemClock;
import android.view.KeyEvent;

/**
 * Stopping and starting somebody else's player.
 *
 * The app is meant to ask about a moment while the person is listening, and the
 * asking has to fit into the music rather than interrupt it - so the app has to
 * be able to hold the music still for as long as the question is on screen.
 *
 * Pause and play are sent as media *keys*, which the system routes to whichever
 * session is playing. That needs no permission at all: it is the same thing a
 * headset button does, and it is why this works even on a phone that refuses the
 * app notification access.
 *
 * Seeking to a position is a different matter - there is no key for "go to
 * 1:42" - and it needs the media session itself, which is the path that is
 * gated behind notification access. So the honest split is: transport is free,
 * position is not.
 */
public final class PlaybackControl {
    private PlaybackControl() {
    }

    /**
     * When the app itself stopped the music.
     *
     * A pause is the signal this app asks about, and a pause the app caused is
     * not a signal at all. Anything that stops the music on purpose has to say
     * so, or the question it is about to ask would be about itself.
     */
    static volatile long selfIssuedUntilMillis;

    /** True when the music stopping was our doing rather than the listener's. */
    public static boolean pausedByUs() {
        return System.currentTimeMillis() < selfIssuedUntilMillis;
    }

    /** Hold the music for a question, and remember that it was us. */
    public static void pause(Context context) {
        selfIssuedUntilMillis = System.currentTimeMillis() + 15_000;
        send(context, KeyEvent.KEYCODE_MEDIA_PAUSE);
    }

    /**
     * Let it go again.
     *
     * The blame does not stop the moment the music does: for a few seconds
     * afterwards the stream is still catching up with the gap we caused, and
     * treating that tail as a pause of the listener's would ask them about our
     * own question - which is exactly what happened the first time this worked.
     */
    public static void resume(Context context) {
        send(context, KeyEvent.KEYCODE_MEDIA_PLAY);
        selfIssuedUntilMillis = System.currentTimeMillis() + SELF_ISSUED_TAIL_MILLIS;
    }

    /** How long the music is still ours after we start it again. */
    private static final long SELF_ISSUED_TAIL_MILLIS = 3_000;

    private static void send(Context context, int keyCode) {
        AudioManager audio = (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);
        if (audio == null) return;
        long now = SystemClock.uptimeMillis();
        audio.dispatchMediaKeyEvent(new KeyEvent(now, now, KeyEvent.ACTION_DOWN, keyCode, 0));
        audio.dispatchMediaKeyEvent(new KeyEvent(now, now, KeyEvent.ACTION_UP, keyCode, 0));
    }
}
