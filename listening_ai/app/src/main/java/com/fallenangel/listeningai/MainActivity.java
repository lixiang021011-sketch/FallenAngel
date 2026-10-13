package com.fallenangel.listeningai;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.media.projection.MediaProjectionManager;
import android.net.Uri;
import android.os.Bundle;
import android.provider.Settings;
import android.util.Log;
import android.view.Gravity;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

/**
 * The launcher: collect the permissions, start listening, and get out of the way.
 *
 * The app is meant to be invisible while music plays, so this screen does three
 * things and nothing else: it says what is missing, it starts the capture, and
 * it shows what has been asked so far. Everything the system does happens in the
 * floating layer, over whatever the person is already listening with.
 */
public class MainActivity extends Activity {
    private static final String TAG = "ListeningAI";
    private static final int REQUEST_PROJECTION = 3001;
    private static final int REQUEST_MIC = 3002;
    private static final int REQUEST_OVERLAY = 3003;
    private static final int REQUEST_NOTIFY = 3004;

    private TextView status;
    private TextView log;
    private final android.os.Handler handler =
            new android.os.Handler(android.os.Looper.getMainLooper());

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
        title.setText("Listening AI");
        title.setTextColor(Color.WHITE);
        title.setTextSize(22f);
        root.addView(title);

        TextView body = new TextView(this);
        body.setText("Listens to whatever is playing and, when you pause on something, "
                + "asks a short question about why it caught your ear.\n\n"
                + "Starting needs the usual three permissions: draw over other apps, "
                + "notification access (to read the player's own actions), and the "
                + "system's audio capture dialog.");
        body.setTextColor(Color.parseColor("#FF99A2B8"));
        body.setTextSize(14f);
        body.setPadding(0, dp(10), 0, dp(18));
        root.addView(body);

        status = new TextView(this);
        status.setTextColor(Color.WHITE);
        status.setTextSize(14f);
        status.setPadding(0, 0, 0, dp(14));
        root.addView(status);

        // Two ways in, because the app is two things and they pull against each other:
        // a question covers the chord box, and the chord box is a distraction while
        // annotating. Which one this session is for is a decision the listener makes
        // before pressing, not something to find out by being interrupted.
        Button listen = new Button(this);
        listen.setText("聆听（只显示和弦，不提问）");
        listen.setOnClickListener(v -> begin(false));
        root.addView(listen);

        Button annotate = new Button(this);
        annotate.setText("提问（听到变化会停下来问）");
        annotate.setOnClickListener(v -> begin(true));
        root.addView(annotate);

        // Where the explanations come from, if anywhere. The app works without
        // this - the local rules ask the question - so it is a setting rather
        // than a requirement, and an empty field means "stay local".
        TextView cloudTitle = new TextView(this);
        cloudTitle.setText("Reasoning model (optional)");
        cloudTitle.setTextColor(Color.WHITE);
        cloudTitle.setTextSize(15f);
        cloudTitle.setPadding(0, dp(22), 0, dp(4));
        root.addView(cloudTitle);

        android.content.SharedPreferences prefs =
                getSharedPreferences(ListeningService.PREFS, MODE_PRIVATE);
        endpoint = cloudField(root, "https://…/v1/chat/completions",
                prefs.getString(ListeningService.PREF_ENDPOINT, ""));
        apiKey = cloudField(root, "api key", prefs.getString(ListeningService.PREF_KEY, ""));
        model = cloudField(root, "model", prefs.getString(ListeningService.PREF_MODEL,
                "gpt-4o-mini"));

        Button save = new Button(this);
        save.setText("Save");
        save.setOnClickListener(v -> {
            getSharedPreferences(ListeningService.PREFS, MODE_PRIVATE).edit()
                    .putString(ListeningService.PREF_ENDPOINT, endpoint.getText().toString().trim())
                    .putString(ListeningService.PREF_KEY, apiKey.getText().toString().trim())
                    .putString(ListeningService.PREF_MODEL, model.getText().toString().trim())
                    .apply();
            status.setText(endpoint.getText().toString().trim().isEmpty()
                    ? "Saved. Explanations come from the local analysis."
                    : "Saved. Explanations will come from the model.");
        });
        root.addView(save);

        log = new TextView(this);
        log.setTextColor(Color.parseColor("#FF99A2B8"));
        log.setTextSize(13f);
        log.setPadding(0, dp(20), 0, 0);
        root.addView(log);

        setContentView(scroll);
    }

    private android.widget.EditText cloudField(LinearLayout root, String hint, String value) {
        android.widget.EditText field = new android.widget.EditText(this);
        field.setHint(hint);
        field.setText(value);
        field.setSingleLine(true);
        field.setTextColor(Color.WHITE);
        field.setHintTextColor(Color.parseColor("#FF66708A"));
        field.setTextSize(13f);
        root.addView(field, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT));
        return field;
    }

    private android.widget.EditText endpoint, apiKey, model;

    @Override
    protected void onResume() {
        super.onResume();
        healWatcher();
        handler.post(statusLoop);
    }

    @Override
    protected void onPause() {
        handler.removeCallbacks(statusLoop);
        super.onPause();
    }

    /**
     * Access can be granted while the listener is not bound - most reliably right
     * after an install, and on MIUI until the app is allowed to autostart. Ask
     * the system to rebind rather than waiting for something else to wake it.
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

    private final Runnable statusLoop = new Runnable() {
        @Override
        public void run() {
            status.setText(ListeningService.running
                    ? "Listening. Pause on anything worth asking about."
                    : ListeningService.status);
            if (ListeningService.running && !Playback.available) {
                status.setText("Listening, but the player cannot be read, so pausing "
                        + "in the player will not be noticed. Allow the app to autostart "
                        + "in the system settings, then reopen this screen.");
            }
            log.setText(String.format(java.util.Locale.US,
                    "Questions asked: %d\nAnswers given: %d\n\nLast question:\n%s",
                    ListeningService.questionsAsked, ListeningService.answersGiven,
                    ListeningService.lastQuestion.isEmpty()
                            ? "(none yet)" : ListeningService.lastQuestion));
            handler.postDelayed(this, 500);
        }
    };

    /** Which of the two sessions the listener is starting; see the entry buttons. */
    private boolean pendingInterrupts;

    private void begin(boolean interrupts) {
        pendingInterrupts = interrupts;
        if (!Settings.canDrawOverlays(this)) {
            status.setText("Grant \"display over other apps\" first.");
            startActivityForResult(new Intent(
                            Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                            Uri.parse("package:" + getPackageName())),
                    REQUEST_OVERLAY);
            return;
        }
        if (!MediaWatcher.hasAccess(this)) {
            status.setText("Grant notification access so the app can see pauses.");
            startActivityForResult(new Intent(
                            Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS),
                    REQUEST_NOTIFY);
            return;
        }
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO)
                != PackageManager.PERMISSION_GRANTED) {
            status.setText("Microphone permission is required to capture playback.");
            requestPermissions(new String[]{Manifest.permission.RECORD_AUDIO}, REQUEST_MIC);
            return;
        }
        status.setText("Approve the system dialog.");
        MediaProjectionManager manager =
                (MediaProjectionManager) getSystemService(MEDIA_PROJECTION_SERVICE);
        startActivityForResult(manager.createScreenCaptureIntent(), REQUEST_PROJECTION);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQUEST_PROJECTION) {
            if (requestCode == REQUEST_OVERLAY && !Settings.canDrawOverlays(this)) {
                status.setText("Without that permission there is nowhere to ask.");
            }
            return;
        }
        if (resultCode != RESULT_OK || data == null) {
            status.setText("Playback capture needs the projection permission.");
            return;
        }
        Intent service = new Intent(this, ListeningService.class)
                .putExtra(ListeningService.EXTRA_RESULT_DATA, data)
                .putExtra(ListeningService.EXTRA_INTERRUPTS, pendingInterrupts);
        try {
            startForegroundService(service);
            ListeningService.status = "starting";
        } catch (Exception e) {
            status.setText("Could not start capture: " + e.getMessage());
        }
    }

    private int dp(int value) {
        return Math.round(getResources().getDisplayMetrics().density * value);
    }
}
