package com.fallenangel.overlaybeat;

import android.content.Context;
import android.content.SharedPreferences;

import java.util.Locale;

/**
 * What the game has learned about this device and these songs.
 *
 * Two different kinds of memory:
 *  - the latency offset is a property of the *device* (audio output, display,
 *    the player's own reaction), so it is stored once and reused everywhere;
 *  - the tempo of a song is a property of the *song*, so it is stored against
 *    the track the media session reports, and a song played a second time locks
 *    immediately instead of waiting ten seconds to be re-estimated.
 */
public final class SongMemory {
    private static final String PREFS = "overlay_beat_memory";
    private static final String KEY_DELAY = "visual_delay_ms";
    private static final String TEMPO_PREFIX = "bpm:";

    private SongMemory() {}

    private static SharedPreferences prefs(Context context) {
        return context.getApplicationContext().getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    public static int loadDelayMs(Context context) {
        return prefs(context).getInt(KEY_DELAY, 0);
    }

    public static void saveDelayMs(Context context, int delayMs) {
        prefs(context).edit().putInt(KEY_DELAY, delayMs).apply();
    }

    /** Tempo previously established for this track, or 0 when it is new to us. */
    public static double loadBpm(Context context, String trackKey) {
        if (trackKey == null || trackKey.isEmpty()) return 0;
        String stored = prefs(context).getString(TEMPO_PREFIX + trackKey, null);
        if (stored == null) return 0;
        try {
            return Double.parseDouble(stored);
        } catch (NumberFormatException e) {
            return 0;
        }
    }

    public static void saveBpm(Context context, String trackKey, double bpm) {
        if (trackKey == null || trackKey.isEmpty() || bpm <= 0) return;
        prefs(context).edit()
                .putString(TEMPO_PREFIX + trackKey, String.format(Locale.US, "%.3f", bpm))
                .apply();
    }
}
