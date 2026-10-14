package com.fallenangel.listeningai;

import android.content.ComponentName;
import android.content.Context;
import android.app.Notification;
import android.media.MediaMetadata;
import android.media.session.MediaController;
import android.media.session.MediaSessionManager;
import android.media.session.PlaybackState;
import android.os.Build;
import android.os.SystemClock;
import android.provider.Settings;
import android.service.notification.NotificationListenerService;
import android.service.notification.StatusBarNotification;
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
    private static final String TAG = "ListeningAI";
    /** A reported position further from the expected one than this is a seek. */
    private static final double SEEK_THRESHOLD_SECONDS = 1.5;

    /** The bound instance, so the capture loop can ask it to look again. */
    private static volatile MediaWatcher instance;
    private final android.os.Handler main =
            new android.os.Handler(android.os.Looper.getMainLooper());

    private MediaSessionManager manager;
    private MediaController controller;
    private MediaController.Callback callback;
    private MediaSessionManager.OnActiveSessionsChangedListener sessionsListener;
    private double expectedPosition;
    private long expectedAtMs;
    private boolean expectedPlaying;
    /** The package the fallback title came from, so a track change is still seen. */
    private volatile String notificationPackage = "";

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
        instance = this;
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
        instance = null;
        detach();
        Playback.available = false;
    }

    @Override
    public void onDestroy() {
        connected = false;
        instance = null;
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
        // How many the system is willing to show us at all: on MIUI this comes back
        // empty often enough that "no player found" needs a number next to it, or it
        // is indistinguishable from "the app did not look".
        Log.i(TAG, "media sessions: " + (controllers == null ? 0 : controllers.size()));
        MediaController chosen = choose(controllers);
        if (chosen == null) {
            detach();
            Playback.available = false;
            return;
        }
        attach(chosen);
    }

    /**
     * Look again, now.
     *
     * A media session is only guaranteed to report its state when it feels like
     * it, and on this phone a player that pauses can leave the callback unsent
     * while the app goes on believing the music is playing - which is the one
     * thing this project cannot survive, because pausing is the whole signal.
     * Asking once a second costs a query and removes the dependence on being
     * told.
     *
     * Posted to the main thread: registering a controller callback needs a
     * Looper, and the caller is the capture worker.
     */
    public static void refresh() {
        MediaWatcher watcher = instance;
        if (watcher != null) watcher.main.post(watcher::rechoose);
    }

    private void rechoose() {
        if (!connected || manager == null) return;
        try {
            ComponentName component = new ComponentName(this, MediaWatcher.class);
            onSessionsChanged(manager.getActiveSessions(component));
        } catch (Exception e) {
            // Losing the list is not losing the truth; the next query tries again.
        }
    }

    /**
     * Which session is the one being listened to.
     *
     * Playing wins. When nothing is playing the answer is the session the system
     * touched most recently - that is the one the person just paused. Keeping
     * the controller already held, which is what the first version did, is how a
     * pause went unnoticed: the old session still said PLAYING after the player
     * had moved on to another one.
     */
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
        MediaController newest = null;
        long newestAt = Long.MIN_VALUE;
        for (MediaController candidate : usable) {
            PlaybackState state = candidate.getPlaybackState();
            long at = state == null ? 0 : state.getLastPositionUpdateTime();
            if (at >= newestAt) {
                newestAt = at;
                newest = candidate;
            }
        }
        return newest != null ? newest : usable.get(0);
    }

    private static boolean isPlaying(MediaController controller) {
        PlaybackState state = controller.getPlaybackState();
        return state != null && state.getState() == PlaybackState.STATE_PLAYING;
    }

    /**
     * The title, read from the player's own notification when the session is silent.
     *
     * MIUI hands a notification listener its notifications but often refuses the
     * media session list, and a player with no readable session is exactly the case
     * where the song name went missing - the app could hear the music and not know
     * what it was. The notification says the same title the person sees on their
     * lock screen, so it is used as a fallback and never overrides a session.
     */
    @Override
    public void onNotificationPosted(StatusBarNotification posted) {
        try {
            if (posted == null) return;
            String pkg = posted.getPackageName();
            if (pkg == null || getPackageName().equals(pkg)) return;
            Notification notification = posted.getNotification();
            if (notification == null || notification.extras == null) return;
            // A readable session is better information than a notification, so never
            // talk over it. And a notification from a package that never gave us a
            // title is not a music player: a chat message must not rename the song.
            if (Playback.available) return;
            boolean first = Playback.trackTitle == null || Playback.trackTitle.isEmpty();
            if (!first && !pkg.equals(notificationPackage)) return;
            CharSequence title = notification.extras.getCharSequence(Notification.EXTRA_TITLE);
            if (title == null || title.length() == 0) return;
            CharSequence artist = notification.extras.getCharSequence(Notification.EXTRA_TEXT);
            if (artist == null) artist = notification.extras.getCharSequence(Notification.EXTRA_SUB_TEXT);
            String name = title.toString().trim();
            if (name.isEmpty()) return;
            String by = artist == null ? "" : artist.toString().trim();
            String key = "notif|" + by + "|" + name;
            if (key.equals(Playback.trackKey)) return;
            Playback.trackKey = key;
            Playback.trackTitle = name;
            Playback.trackArtist = by;
            notificationPackage = pkg;
            Log.i(TAG, "media track (from the notification, " + pkg + "): " + key);
        } catch (Throwable failure) {
            // A notification that does not parse is not a reason to stop listening.
            Log.w(TAG, "could not read a notification", failure);
        }
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
            // The position in a PlaybackState is a snapshot taken at its own update time,
            // not at the moment it is read. Filling in the difference is the documented
            // way to read it, and without it every reading is behind by however long the
            // player has been playing since it last spoke - which is exactly the error
            // the bar flash used to sit on.
            if (playing) {
                long updated = state.getLastPositionUpdateTime();
                if (updated > 0 && now > updated) position += (now - updated) / 1000.0;
            }
            Playback.positionSeconds = position;
            Playback.positionAtRealtime = now;
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
