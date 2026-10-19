package com.fallenangel.overlaybeat;

import android.content.ComponentName;
import android.content.Context;
import android.media.MediaMetadata;
import android.media.session.MediaController;
import android.media.session.MediaSessionManager;
import android.media.session.PlaybackState;
import android.os.Build;
import android.os.SystemClock;
import android.provider.Settings;
import android.service.notification.NotificationListenerService;
import android.util.Log;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/**
 * Reads what the music player is doing and hands it to the game as events.
 *
 * Track changes, pause and seek are things the player knows exactly and the
 * audio can only hint at after the fact, so the game reacts to these instead of
 * inferring them. Reading a media session is gated behind notification access;
 * without it the game still works, it just falls back to guessing from sound.
 */
public class MediaWatcher extends NotificationListenerService {
    private static final String TAG = "OverlayBeat";
    /** A reported position further from the expected one than this is a seek. */
    private static final double SEEK_THRESHOLD_SECONDS = 1.5;

    private MediaSessionManager manager;
    private MediaController controller;
    private MediaController.Callback callback;
    private MediaSessionManager.OnActiveSessionsChangedListener sessionsListener;
    private double expectedPosition;
    private long expectedAtMs;
    private boolean expectedPlaying;

    /**
     * Whether the watcher is actually running.
     *
     * Notification access being granted is not the same as the service being
     * bound - on MIUI a freshly installed app is granted access and then refused
     * the binding until it is allowed to autostart, which looks exactly like the
     * feature being broken.
     */
    private static volatile boolean connected;

    static boolean isConnected() {
        return connected;
    }

    static boolean hasAccess(Context context) {
        String enabled;
        try {
            enabled = Settings.Secure.getString(context.getContentResolver(),
                    "enabled_notification_listeners");
        } catch (Exception e) {
            return false;
        }
        if (enabled == null) return false;
        String flat = new ComponentName(context, MediaWatcher.class).flattenToString()
                .toLowerCase(Locale.ROOT);
        for (String entry : enabled.split(":")) {
            if (entry.trim().toLowerCase(Locale.ROOT).equals(flat)) return true;
        }
        return false;
    }

    @Override
    public void onListenerConnected() {
        super.onListenerConnected();
        connected = true;
        Log.i(TAG, "media watcher connected");
        try {
            manager = (MediaSessionManager) getSystemService(Context.MEDIA_SESSION_SERVICE);
            ComponentName component = new ComponentName(this, MediaWatcher.class);
            sessionsListener = this::onSessionsChanged;
            manager.addOnActiveSessionsChangedListener(sessionsListener, component);
            onSessionsChanged(manager.getActiveSessions(component));
        } catch (Exception e) {
            Log.w(TAG, "watcher could not start", e);
            Playback.available = false;
        }
    }

    @Override
    public void onListenerDisconnected() {
        super.onListenerDisconnected();
        connected = false;
        detach();
        Playback.available = false;
    }

    @Override
    public void onDestroy() {
        connected = false;
        if (manager != null && sessionsListener != null) {
            try {
                manager.removeOnActiveSessionsChangedListener(sessionsListener);
            } catch (Exception ignored) {
            }
        }
        detach();
        Playback.available = false;
        super.onDestroy();
    }

    private void onSessionsChanged(List<MediaController> controllers) {
        MediaController chosen = choose(controllers);
        if (chosen == null) {
            detach();
            Playback.available = false;
            return;
        }
        attach(chosen);
    }

    /** Playing wins; otherwise keep following what we have. */
    private MediaController choose(List<MediaController> controllers) {
        List<MediaController> usable = new ArrayList<>();
        for (MediaController candidate : controllers) {
            if (candidate == null) continue;
            if (getPackageName().equals(candidate.getPackageName())) continue;
            if (candidate.getMetadata() == null && candidate.getPlaybackState() == null) continue;
            usable.add(candidate);
        }
        if (usable.isEmpty()) return null;
        for (MediaController candidate : usable) {
            if (isPlaying(candidate)) return candidate;
        }
        for (MediaController candidate : usable) {
            if (controller != null
                    && candidate.getSessionToken().equals(controller.getSessionToken())) {
                return controller;
            }
        }
        return usable.get(0);
    }

    private static boolean isPlaying(MediaController controller) {
        PlaybackState state = controller.getPlaybackState();
        return state != null && state.getState() == PlaybackState.STATE_PLAYING;
    }

    private void attach(MediaController next) {
        if (controller != null && controller.getSessionToken().equals(next.getSessionToken())) {
            publishMetadata();
            publishState();
            return;
        }
        detach();
        controller = next;
        expectedAtMs = 0;
        callback = new MediaController.Callback() {
            @Override
            public void onMetadataChanged(MediaMetadata metadata) {
                publishMetadata();
            }

            @Override
            public void onPlaybackStateChanged(PlaybackState state) {
                publishState();
            }

            @Override
            public void onSessionDestroyed() {
                detach();
                Playback.available = false;
            }
        };
        controller.registerCallback(callback);
        Playback.available = true;
        publishMetadata();
        publishState();
    }

    private void detach() {
        if (controller != null && callback != null) {
            try {
                controller.unregisterCallback(callback);
            } catch (Exception ignored) {
            }
        }
        controller = null;
        callback = null;
    }

    private void publishMetadata() {
        if (controller == null) return;
        MediaMetadata metadata = controller.getMetadata();
        if (metadata == null) return;
        CharSequence title = metadata.getText(MediaMetadata.METADATA_KEY_TITLE);
        CharSequence artist = metadata.getText(MediaMetadata.METADATA_KEY_ARTIST);
        long durationMs = metadata.getLong(MediaMetadata.METADATA_KEY_DURATION);
        if (durationMs > 0) Playback.durationSeconds = durationMs / 1000.0;
        String key = String.format(Locale.US, "%s|%s|%d",
                artist == null ? "" : artist, title == null ? "" : title, durationMs / 1000);
        if (!key.equals(Playback.trackKey)) {
            Playback.trackKey = key;
            Playback.trackTitle = title == null ? "" : title.toString();
            Playback.trackArtist = artist == null ? "" : artist.toString();
            Log.i(TAG, "media track: " + key);
        }
    }

    private void publishState() {
        if (controller == null) return;
        PlaybackState state = controller.getPlaybackState();
        if (state == null) return;
        boolean playing = state.getState() == PlaybackState.STATE_PLAYING;

        // Positions arrive in milliseconds and are only re-published when the
        // player feels like it, so a jump is measured against an extrapolation.
        double position = state.getPosition() / 1000.0;
        long now = SystemClock.elapsedRealtime();
        if (position >= 0) {
            Playback.positionSeconds = position;
            Playback.positionGeneration++;
        }
        if (expectedAtMs != 0 && position >= 0) {
            double expected = expectedPosition;
            if (expectedPlaying) expected += (now - expectedAtMs) / 1000.0;
            if (Math.abs(position - expected) > SEEK_THRESHOLD_SECONDS) {
                Playback.seekGeneration++;
                Log.i(TAG, String.format(Locale.US, "media seek: %.1f -> %.1f",
                        expected, position));
            }
        }
        if (position >= 0) {
            expectedPosition = position;
            expectedAtMs = now;
        }
        expectedPlaying = playing;
        if (playing != Playback.playing) {
            Playback.playing = playing;
            Log.i(TAG, "media " + (playing ? "playing" : "paused"));
        }
    }
}
