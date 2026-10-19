package com.fallenangel.overlaybeat;

import android.Manifest;
import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.media.projection.MediaProjectionManager;
import android.net.Uri;
import android.os.Bundle;
import android.provider.Settings;
import android.util.Log;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

/**
 * The launcher: collect the three permissions the panel needs, start capture,
 * and get out of the way.
 *
 * The game itself lives in the floating panel, so this screen deliberately does
 * little more than explain what is about to happen and let the calibration be
 * nudged afterwards.
 */
public class GameActivity extends Activity {
    private static final String TAG = "OverlayBeat";
    private static final int REQUEST_PROJECTION = 2001;
    private static final int REQUEST_MIC = 2002;
    private static final int REQUEST_OVERLAY = 2003;
    private static final int REQUEST_NOTIFY = 2004;

    private TextView status;
    private TextView offsetLabel;
    private TextView densityLabel;
    private TextView figureLabel;
    private final android.os.Handler handler = new android.os.Handler(android.os.Looper.getMainLooper());

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        ScrollView scroll = new ScrollView(this);
        scroll.setBackgroundColor(Color.parseColor("#FF0B0D12"));

        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(24), dp(48), dp(24), dp(24));
        scroll.addView(root);

        TextView title = new TextView(this);
        title.setText("Overlay Beat");
        title.setTextColor(Color.WHITE);
        title.setTextSize(22f);
        root.addView(title);

        TextView body = new TextView(this);
        body.setText("A single lane that floats over whatever you are listening to. "
                + "Tap the panel when a note reaches the line; drag it to move it.\n\n"
                + "Starting needs three taps from you: draw-over-other-apps, microphone, "
                + "and the system's projection dialog. After that this screen can be closed.");
        body.setTextColor(Color.parseColor("#FF99A2B8"));
        body.setTextSize(14f);
        body.setPadding(0, dp(10), 0, dp(18));
        root.addView(body);

        status = new TextView(this);
        status.setTextColor(Color.WHITE);
        status.setTextSize(14f);
        status.setPadding(0, 0, 0, dp(14));
        root.addView(status);

        Button start = new Button(this);
        start.setText("Start listening");
        start.setOnClickListener(v -> begin());
        root.addView(start);

        offsetLabel = new TextView(this);
        offsetLabel.setTextColor(Color.parseColor("#FF99A2B8"));
        offsetLabel.setTextSize(13f);
        offsetLabel.setPadding(0, dp(20), 0, dp(6));
        root.addView(offsetLabel);

        LinearLayout offsetRow = new LinearLayout(this);
        offsetRow.setOrientation(LinearLayout.HORIZONTAL);
        Button earlier = new Button(this);
        earlier.setText("earlier");
        earlier.setOnClickListener(v -> nudge(-10));
        Button later = new Button(this);
        later.setText("later");
        later.setOnClickListener(v -> nudge(10));
        offsetRow.addView(earlier);
        offsetRow.addView(later);

        Button resetOffset = new Button(this);
        resetOffset.setText("reset offset");
        resetOffset.setOnClickListener(v -> {
            CaptureService.visualDelayMs = 0;
            SongMemory.saveDelayMs(this, 0);
            CaptureService.calibrationGeneration++;
            updateOffsetLabel();
        });
        offsetRow.addView(resetOffset);

        root.addView(offsetRow);

        densityLabel = new TextView(this);
        densityLabel.setTextColor(Color.WHITE);
        densityLabel.setTextSize(14f);
        densityLabel.setPadding(0, dp(18), 0, dp(6));
        root.addView(densityLabel);

        LinearLayout gridRow = new LinearLayout(this);
        gridRow.setOrientation(LinearLayout.HORIZONTAL);
        gridRow.addView(densityButton("1 / beat", 1));
        gridRow.addView(densityButton("2 / beat", 2));
        gridRow.addView(densityButton("4 / beat", 4));
        root.addView(gridRow);

        LinearLayout shareRow = new LinearLayout(this);
        shareRow.setOrientation(LinearLayout.HORIZONTAL);
        shareRow.addView(certaintyButton("loose", 0.30));
        shareRow.addView(certaintyButton("medium", 0.45));
        shareRow.addView(certaintyButton("strict", 0.60));
        root.addView(shareRow);

        figureLabel = new TextView(this);
        figureLabel.setTextColor(Color.WHITE);
        figureLabel.setTextSize(14f);
        figureLabel.setTypeface(android.graphics.Typeface.MONOSPACE);
        figureLabel.setPadding(0, dp(18), 0, dp(6));
        root.addView(figureLabel);

        TextView hint = new TextView(this);
        hint.setText("\"Earlier / later\" shifts every note by 10 ms. Use it if the notes "
                + "arrive consistently before or after the drum you hear.\n\n"
                + "Density is how many places per beat the chart may write a note in. "
                + "Measured on one song: 1 gives about 1.3 notes a second, 2 about 2.4, "
                + "4 about 4.5 - the song decides how close it gets to that, and both "
                + "settings apply to the next note, not the next song. Changing the "
                + "density makes the analysis relearn the bar, so a couple of bars go "
                + "quiet. Certainty is how much usually has to happen on a place for it "
                + "to earn a note: loose writes more of them, strict writes fewer.");
        hint.setTextColor(Color.parseColor("#FF99A2B8"));
        hint.setTextSize(12f);
        hint.setPadding(0, dp(10), 0, 0);
        root.addView(hint);

        setContentView(scroll);
        updateOffsetLabel();
        updateDensityLabel();
        updateFigureLabel();
    }

    private Button densityButton(String text, int subdivisions) {
        Button button = new Button(this);
        button.setText(text);
        button.setOnClickListener(v ->
                applyDensity(BeatDetector.POSITIONS_PER_BEAT / subdivisions,
                        CaptureService.slotShare));
        return button;
    }

    private Button certaintyButton(String text, double share) {
        Button button = new Button(this);
        button.setText(text);
        button.setOnClickListener(v -> applyDensity(CaptureService.chartStride, share));
        return button;
    }

    /**
     * Changing this mid-song is the point: the person listening is the only one
     * who can say whether the chart is too busy, and waiting for a restart to
     * find out would hide the answer.
     */
    private void applyDensity(int stride, double share) {
        CaptureService.chartStride = stride;
        CaptureService.slotShare = share;
        getSharedPreferences(CaptureService.PREFS, MODE_PRIVATE).edit()
                .putInt(CaptureService.PREF_CHART_STRIDE, stride)
                .putFloat(CaptureService.PREF_SLOT_SHARE, (float) share)
                .apply();
        CaptureService.chartBuilder.setStride(stride);
        CaptureService.chartBuilder.setSlotShare(share);
        updateDensityLabel();
    }

    private void updateDensityLabel() {
        int perBeat = BeatDetector.POSITIONS_PER_BEAT / CaptureService.chartStride;
        densityLabel.setText(String.format(java.util.Locale.US,
                "Note density: %d place%s per beat · certainty %.2f",
                perBeat, perBeat == 1 ? "" : "s",
                CaptureService.slotShare));
    }

    /**
     * The figure the analysis expects at each beat of the bar.
     *
     * X is a place the music plays, . a place it does not, ? a beat it has not
     * seen enough of yet. This is what the chart is being written from, so it
     * says whether a wrong chart is wrong analysis or wrong policy.
     */
    private void updateFigureLabel() {
        BeatDetector detector = CaptureService.detector;
        figureLabel.setText(detector == null
                ? "Figures per beat: waiting for the grid"
                : "Figures per beat: " + detector.describeFigures());
    }

    @Override
    protected void onResume() {
        super.onResume();
        healWatcher();
        handler.post(statusLoop);
    }

    /**
     * Access can be granted while the listener is not bound - most reliably
     * right after an install, and on MIUI until the app is allowed to autostart.
     * Ask the system to rebind rather than waiting for something else to wake it.
     */
    private void healWatcher() {
        if (!MediaWatcher.hasAccess(this) || MediaWatcher.isConnected()) return;
        try {
            android.service.notification.NotificationListenerService.requestRebind(
                    new android.content.ComponentName(this, MediaWatcher.class));
        } catch (Exception e) {
            Log.w(TAG, "rebind request failed", e);
        }
    }

    @Override
    protected void onPause() {
        handler.removeCallbacks(statusLoop);
        super.onPause();
    }

    private final Runnable statusLoop = new Runnable() {
        @Override
        public void run() {
            status.setText(CaptureService.running
                    ? "Running. The panel is on your screen.\nNotes: " + sourceLabel()
                    : CaptureService.status);
            if (CaptureService.running && !Playback.available) {
                status.setText("Running, but the player cannot be read, so there is no track "
                        + "name and no track-change detection. Allow Overlay Beat to "
                        + "autostart in the system settings, then reopen this screen.");
            }
            updateOffsetLabel();
            updateFigureLabel();
            handler.postDelayed(this, 500);
        }
    };

    /**
     * Where the notes are coming from, in words.
     *
     * Without this the three sources are indistinguishable from the outside: a
     * chart you cannot hear, a MIDI file you cannot see, and the audio analysis
     * all look like the same falling notes.
     */
    private String sourceLabel() {
        String mode = CaptureService.mode;
        if (mode.startsWith("chart:")) return "FallenAngel chart " + mode.substring(6);
        if (mode.startsWith("midi:")) return "MIDI file " + mode.substring(5);
        if (mode.startsWith("cached:")) return "this song's own audio, charted in full";
        return "the audio (no chart or MIDI for this song)";
    }

    private void updateOffsetLabel() {
        offsetLabel.setText(String.format(java.util.Locale.US,
                "Latency offset: %+d ms (auto-fitted from your taps)",
                CaptureService.visualDelayMs));
    }

    private void nudge(int deltaMs) {
        CaptureService.visualDelayMs = Math.max(-300, Math.min(300,
                CaptureService.visualDelayMs + deltaMs));
        updateOffsetLabel();
    }

    private void begin() {
        if (!Settings.canDrawOverlays(this)) {
            status.setText("Grant \"display over other apps\" first.");
            try {
                startActivityForResult(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                        Uri.parse("package:" + getPackageName())), REQUEST_OVERLAY);
            } catch (Exception e) {
                startActivityForResult(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION), REQUEST_OVERLAY);
            }
            return;
        }
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.RECORD_AUDIO}, REQUEST_MIC);
            return;
        }
        if (!MediaWatcher.hasAccess(this)) {
            status.setText("Grant notification access so the game can see track changes, "
                    + "pauses and seeks.");
            startActivityForResult(new Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS),
                    REQUEST_NOTIFY);
            return;
        }
        askForProjection();
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] results) {
        super.onRequestPermissionsResult(requestCode, permissions, results);
        if (requestCode == REQUEST_MIC) {
            if (results.length > 0 && results[0] == PackageManager.PERMISSION_GRANTED) askForProjection();
            else status.setText("Microphone permission is required to capture playback.");
        }
    }

    private void askForProjection() {
        status.setText("Approve the system dialog.");
        MediaProjectionManager manager =
                (MediaProjectionManager) getSystemService(Context.MEDIA_PROJECTION_SERVICE);
        startActivityForResult(manager.createScreenCaptureIntent(), REQUEST_PROJECTION);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == REQUEST_OVERLAY) {
            if (Settings.canDrawOverlays(this)) begin();
            else status.setText("Without that permission there is nowhere to draw the lane.");
            return;
        }
        if (requestCode == REQUEST_NOTIFY) {
            // Optional: without it the game falls back to guessing from sound.
            status.setText(MediaWatcher.hasAccess(this)
                    ? "Notification access granted."
                    : "Continuing without notification access.");
            begin();
            return;
        }
        if (requestCode != REQUEST_PROJECTION) return;
        if (resultCode != RESULT_OK || data == null) {
            status.setText("Playback capture needs the projection permission.");
            return;
        }
        Intent service = new Intent(this, CaptureService.class);
        service.putExtra(CaptureService.EXTRA_RESULT_DATA, data);
        try {
            startForegroundService(service);
            status.setText("Listening. Start some music.");
        } catch (Exception e) {
            Log.e(TAG, "could not start capture", e);
            status.setText("Could not start capture: " + e.getMessage());
        }
    }

    private int dp(int value) {
        return Math.round(getResources().getDisplayMetrics().density * value);
    }
}

