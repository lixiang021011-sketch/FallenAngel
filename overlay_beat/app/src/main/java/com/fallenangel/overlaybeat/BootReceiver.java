package com.fallenangel.overlaybeat;

import android.content.BroadcastReceiver;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.service.notification.NotificationListenerService;
import android.util.Log;

/**
 * Re-attaches the media watcher after a reboot or an update.
 *
 * MIUI refuses to bind a notification listener for an app it has never seen
 * autostart, and an update drops the previous binding - so without this the
 * feature silently disappears and looks unimplemented. Declaring the receiver
 * also makes the app appear in the system's autostart list, which is the switch
 * the user actually has to flip on MIUI.
 *
 * The panel and the audio capture cannot be restored here: a media projection is
 * per-session and needs the user to approve it, so this only heals the read-only
 * half.
 */
public class BootReceiver extends BroadcastReceiver {
    private static final String TAG = "OverlayBeat";

    @Override
    public void onReceive(Context context, Intent intent) {
        String action = intent == null ? null : intent.getAction();
        if (!Intent.ACTION_BOOT_COMPLETED.equals(action)
                && !Intent.ACTION_MY_PACKAGE_REPLACED.equals(action)) {
            return;
        }
        try {
            NotificationListenerService.requestRebind(
                    new ComponentName(context, MediaWatcher.class));
            Log.i(TAG, "asked the system to rebind the media watcher after " + action);
        } catch (Exception e) {
            Log.w(TAG, "could not request a rebind", e);
        }
    }
}
