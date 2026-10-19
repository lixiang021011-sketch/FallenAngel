package com.fallenangel.overlaybeat;

import android.app.Activity;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ServiceInfo;
import android.graphics.PixelFormat;
import android.media.AudioAttributes;
import android.media.AudioFormat;
import android.media.AudioPlaybackCaptureConfiguration;
import android.media.AudioRecord;
import android.media.projection.MediaProjection;
import android.media.projection.MediaProjectionManager;
import android.os.Build;
import android.os.IBinder;
import android.provider.Settings;
import android.util.Log;
import android.view.Gravity;
import android.view.WindowManager;

import java.io.File;

/**
 * Everything the game needs to be alive: it captures playback, turns it into
 * notes, and hosts the floating panel that shows them.
 *
 * One service does all three on purpose. Capture already requires a foreground
 * service of type mediaProjection, and an overlay window needs nothing more than
 * the draw-over-other-apps permission - so there is no reason to run a second
 * service, and no risk of two foreground notifications fighting.
 */
public class CaptureService extends Service {
    public static final String EXTRA_RESULT_DATA = "resultData";
    public static final String PREFS = "overlay";
    public static final String PREF_X = "x";
    public static final String PREF_Y = "y";
    public static final String PREF_CHART_STRIDE = "chartStride";
    public static final String PREF_SLOT_SHARE = "slotShare";

    private static final String TAG = "OverlayBeat";
    private static final String CHANNEL_ID = "overlay_beat";
    private static final int NOTIFICATION_ID = 6101;
    /** Longer than this is a podcast or a video, not a song to chart. */
    private static final double MAX_CACHE_SECONDS = 8 * 60;

    /**
     * How much later than its beat a note is shown, and judged, in ms.
     *
     * Every device has a fixed delay between "the audio was captured" and "the
     * player saw the note and reacted": audio output latency, display latency,
     * and their own reaction time. A note drawn at its exact capture time is
     * therefore always early from the player's point of view, and they report
     * it as "I cannot land on the beat".
     *
     * The value is fitted from the player's own tapping rather than asked for,
     * because a number they cannot feel is a number they cannot give.
     */
    static volatile int visualDelayMs;
    /**
     * Calibration is a step, not a background process.
     *
     * While playing, a tap can mean "I am timing the beat" or it can mean "that
     * drum fill was great" - the data cannot tell them apart. Letting those taps
     * keep voting on the offset let a handful of casual hits drag the whole lane
     * off the music. So the offset is fitted once from a run of taps and then
     * left alone; a fresh calibration can be asked for explicitly.
     */
    static volatile boolean offsetFrozen;
    /** Bumped to ask the panel to start a fresh calibration. */
    static volatile long calibrationGeneration;
    static volatile BeatDetector detector;
    /** The chart policy. The panel reads notes from here, not from the analysis. */
    static final ChartBuilder chartBuilder = new ChartBuilder();
    /** A pre-made chart, when one exists for what is playing. */
    static final ChartPlayer chartPlayer = new ChartPlayer();
    /** What the game is going off right now, for the app screen to show. */
    static volatile String mode = "audio";
    static volatile String status = "idle";
    static volatile boolean running;
    /**
     * How dense the chart generated from the audio is allowed to be, as a stride
     * over the bar's sixteen positions: 1 takes them all, 4 takes the beats.
     *
     * This is the one number that decides whether the chart is playable at all,
     * and it is not something the audio can answer: a song that has something on
     * every sixteenth will get a note on every sixteenth. Whoever is listening
     * has to choose it. It applies only to the audio's own chart - a bar the
     * player taught already carries their own density.
     */
    static volatile int chartStride = 4;
    /** How much usually has to happen on a position for it to earn a note. */
    static volatile double slotShare = ChartBuilder.DEFAULT_SLOT_SHARE;

    private SharedPreferences prefs;
    private WindowManager windowManager;
    private GameView panel;
    private WindowManager.LayoutParams params;
    private int dragStartX;
    private int dragStartY;
    private Thread worker;
    private volatile boolean stopping;

    @Override
    public void onCreate() {
        super.onCreate();
        prefs = getSharedPreferences(PREFS, MODE_PRIVATE);
        windowManager = (WindowManager) getSystemService(WINDOW_SERVICE);
        // The latency offset belongs to this device, so it is restored rather
        // than re-learned on every launch.
        visualDelayMs = SongMemory.loadDelayMs(this);
        chartStride = prefs.getInt(PREF_CHART_STRIDE, chartStride);
        slotShare = prefs.getFloat(PREF_SLOT_SHARE, (float) slotShare);
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        startForegroundCompat();
        // The panel is added here, on the main thread. Window operations cannot
        // run on the capture worker: addView() builds view roots and needs a
        // Looper, which is why the first attempt threw
        // "Can't create handler inside thread that has not called Looper.prepare()"
        // and no panel ever appeared.
        showOverlay();

        Intent resultData = intent == null ? null : resultDataOf(intent);
        if (resultData == null) {
            status = "no projection result";
            stopSelf();
            return START_NOT_STICKY;
        }
        if (worker == null) {
            stopping = false;
            worker = new Thread(() -> run(resultData), "capture");
            worker.start();
        }
        return START_NOT_STICKY;
    }

    @Override
    public void onDestroy() {
        stopping = true;
        if (worker != null) {
            try {
                worker.join(500);
            } catch (InterruptedException ignored) {
                Thread.currentThread().interrupt();
            }
            worker = null;
        }
        hideOverlay();
        running = false;
        detector = null;
        stopForeground(STOP_FOREGROUND_REMOVE);
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    @SuppressWarnings("deprecation") // the typed overload only exists on API 33+
    private static Intent resultDataOf(Intent intent) {
        return Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU
                ? intent.getParcelableExtra(EXTRA_RESULT_DATA, Intent.class)
                : intent.getParcelableExtra(EXTRA_RESULT_DATA);
    }

    private void startForegroundCompat() {
        NotificationManager manager = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        if (manager.getNotificationChannel(CHANNEL_ID) == null) {
            manager.createNotificationChannel(new NotificationChannel(
                    CHANNEL_ID, "Overlay Beat", NotificationManager.IMPORTANCE_LOW));
        }
        Notification notification = new Notification.Builder(this, CHANNEL_ID)
                .setContentTitle("Overlay Beat is listening")
                .setContentText("Tap the notes as they reach the line")
                .setSmallIcon(android.R.drawable.ic_media_play)
                .setOngoing(true)
                .build();
        startForeground(NOTIFICATION_ID, notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION);
    }

    // ------------------------------------------------------------------ overlay

    private void showOverlay() {
        if (panel != null) return;
        if (!Settings.canDrawOverlays(this)) {
            status = "overlay permission missing";
            return;
        }

        panel = new GameView(this);
        panel.setDragListener(new GameView.DragListener() {
            @Override
            public void onDragStart() {
                dragStartX = params.x;
                dragStartY = params.y;
            }

            @Override
            public void onDragBy(float dx, float dy) {
                params.x = dragStartX + Math.round(dx);
                params.y = dragStartY + Math.round(dy);
                try {
                    windowManager.updateViewLayout(panel, params);
                } catch (Exception ignored) {
                }
            }

            @Override
            public void onDragEnd() {
                prefs.edit().putInt(PREF_X, params.x).putInt(PREF_Y, params.y).apply();
            }
        });

        params = new WindowManager.LayoutParams(
                WindowManager.LayoutParams.WRAP_CONTENT,
                WindowManager.LayoutParams.WRAP_CONTENT,
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                        | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL
                        | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
                PixelFormat.TRANSLUCENT);
        params.gravity = Gravity.TOP | Gravity.START;
        params.x = prefs.getInt(PREF_X, dp(10));
        params.y = prefs.getInt(PREF_Y, dp(180));

        try {
            windowManager.addView(panel, params);
            status = "playing";
        } catch (Exception e) {
            Log.e(TAG, "could not add the overlay", e);
            status = "overlay failed: " + e.getClass().getSimpleName();
            panel = null;
        }
    }

    private void hideOverlay() {
        if (panel == null) return;
        try {
            windowManager.removeView(panel);
        } catch (Exception ignored) {
        }
        panel = null;
    }

    // ------------------------------------------------------------------ capture

    private void run(Intent resultData) {
        MediaProjection projection = null;
        AudioRecord record = null;
        SongCache.Recorder recorder = null;
        boolean wantToRecord = false;
        double lastRecorderPosition = -1;
        try {
            MediaProjectionManager manager =
                    (MediaProjectionManager) getSystemService(MEDIA_PROJECTION_SERVICE);
            projection = manager.getMediaProjection(Activity.RESULT_OK, resultData);
            if (projection == null) {
                status = "projection unavailable";
                return;
            }

            AudioPlaybackCaptureConfiguration config =
                    new AudioPlaybackCaptureConfiguration.Builder(projection)
                            .addMatchingUsage(AudioAttributes.USAGE_MEDIA)
                            .addMatchingUsage(AudioAttributes.USAGE_GAME)
                            .addMatchingUsage(AudioAttributes.USAGE_UNKNOWN)
                            .build();
            AudioFormat format = new AudioFormat.Builder()
                    .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                    .setSampleRate(BeatDetector.SAMPLE_RATE)
                    .setChannelMask(AudioFormat.CHANNEL_IN_MONO)
                    .build();
            int minimum = AudioRecord.getMinBufferSize(BeatDetector.SAMPLE_RATE,
                    AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT);
            record = new AudioRecord.Builder()
                    .setAudioFormat(format)
                    .setBufferSizeInBytes(Math.max(minimum * 2, BeatDetector.SAMPLE_RATE / 2))
                    .setAudioPlaybackCaptureConfig(config)
                    .build();
            if (record.getState() != AudioRecord.STATE_INITIALIZED) {
                status = "AudioRecord failed to initialise";
                return;
            }

            BeatDetector local = new BeatDetector();
            chartBuilder.setStride(chartStride);
            chartBuilder.setSlotShare(slotShare);
            detector = local;
            running = true;
            record.startRecording();

            short[] buffer = new short[2048];
            long lastReport = 0;
            String lastTrack = Playback.trackKey;
            long lastSeek = Playback.seekGeneration;
            boolean lastPlaying = Playback.playing;
            String learnedTrack = null;
            ChartLibrary library = new ChartLibrary(getExternalFilesDir("charts"));
            library.scan();
            MidiLibrary midi = new MidiLibrary(midiDirectory());
            SongCache cache = new SongCache(pcmDirectory());
            Log.i(TAG, "songs with cached audio: " + cache.size());
            Log.i(TAG, "charts on this phone: " + library.size()
                    + (library.failed() > 0 ? " (" + library.failed() + " unreadable)" : ""));
            Log.i(TAG, "MIDI files on this phone: " + midi.count());
            String recordingKey = null;
            pickChart(library, midi);
            while (!stopping) {
                int read = record.read(buffer, 0, buffer.length);
                if (read > 0) {
                    local.push(buffer, read);
                    // Keep the audio of this pass, so the next one can be
                    // charted from the whole song instead of guessed.
                    if (wantToRecord && recorder == null) {
                        // Start only once the song is actually at its beginning.
                        // A track change reports the position it had a moment
                        // ago, and a recording started there is a recording that
                        // says it began at ten seconds in - which is exactly what
                        // the first version of this rejected every time.
                        double duration = Playback.durationSeconds;
                        if (Playback.positionSeconds >= -0.5 && Playback.positionSeconds <= 2
                                && duration > 0 && duration <= MAX_CACHE_SECONDS) {
                            recorder = cache.begin(recordingKey, Playback.trackTitle,
                                    Playback.positionSeconds);
                            lastRecorderPosition = Playback.positionSeconds;
                            Log.i(TAG, "recording this pass for the cache ("
                                    + Math.round(duration) + "s)");
                        } else if (duration > MAX_CACHE_SECONDS) {
                            wantToRecord = false;
                            Log.i(TAG, "not caching this: " + Math.round(duration) + "s long");
                        }
                    }
                    // The session's playing flag is what says the song is moving.
                    // The position is not usable for this: this phone's player
                    // reports a position once and then leaves it at zero for the
                    // whole song, so waiting for it to advance recorded nothing
                    // at all. A seek is still caught, by the file and the position
                    // disagreeing.
                    if (recorder != null && Playback.playing) {
                        recorder.write(buffer, read);
                        lastRecorderPosition = Playback.positionSeconds;
                    }
                }

                // Analysis produces beats; the chart builder decides what the
                // player has to do about them - unless a real chart is playing,
                // in which case the lane belongs to that chart and a second
                // stream of notes on top of it would be two songs at once.
                for (Beat beat : local.drainBeats()) {
                    // Only stand aside for a chart that can actually place its
                    // notes: without a song position it has nothing to go on,
                    // and silence would be worse than the analysis.
                    if (!chartPlayer.active() || Playback.positionSeconds < 0) {
                        chartBuilder.accept(beat);
                    }
                }

                // The player's own actions, straight from the system: a track
                // change means a new tempo, a seek means the same tempo in a new
                // place, and a pause means hold everything.
                if (Playback.available) {
                    if (!Playback.trackKey.equals(lastTrack)) {
                        // Only tempos the player established are worth keeping;
                        // a wrong automatic lock would otherwise be remembered
                        // and served back as if it were knowledge.
                        if (learnedTrack != null && !learnedTrack.isEmpty() && local.manualLock()) {
                            SongMemory.saveBpm(this, learnedTrack, local.bpm());
                            Log.i(TAG, "remembered " + local.bpm() + " bpm for " + learnedTrack);
                        }
                        lastTrack = Playback.trackKey;
                        local.onTrackChanged();
                        Log.i(TAG, "track changed, re-acquiring the beat");
                        double known = SongMemory.loadBpm(this, lastTrack);
                        if (known > 0) {
                            local.lockTempo(known, true);
                            Log.i(TAG, "known song, locking to " + known + " bpm");
                        }
                        learnedTrack = lastTrack;
                        if (recorder != null) {
                            recorder.finish(Playback.durationSeconds, Playback.trackTitle);
                            recorder = null;
                        }
                        wantToRecord = false;
                        recordingKey = SongCache.keyFor(Playback.trackKey);
                        if (!playCachedChart(cache, recordingKey)) {
                            pickChart(library, midi);
                            wantToRecord = true;
                        }
                    }
                    if (Playback.seekGeneration != lastSeek) {
                        lastSeek = Playback.seekGeneration;
                        local.onSeek();
                        if (recorder != null) {
                            // A player that has just finished a track resets its
                            // position to zero, and that arrives here looking
                            // exactly like a rewind. It is not one if the
                            // recording already holds the whole song - and
                            // throwing that away is how a complete two hundred
                            // second recording was destroyed by the song ending
                            // normally.
                            if (coversWholeSong(recorder)) {
                                keepRecording(recorder);
                                recorder = null;
                                wantToRecord = false;
                            } else {
                                recorder.seek(Playback.positionSeconds);
                            }
                        }
                        Log.i(TAG, "seek, re-phasing the grid");
                    }
                    if (Playback.playing != lastPlaying) {
                        lastPlaying = Playback.playing;
                        local.setPlaying(lastPlaying);
                        if (!lastPlaying && recorder != null && coversWholeSong(recorder)) {
                            keepRecording(recorder);
                            recorder = null;
                            wantToRecord = false;
                        }
                    }
                }

                long nowMs = android.os.SystemClock.elapsedRealtime();
                chartPlayer.poll(local.audioTime(), Playback.positionSeconds);
                if (nowMs - lastReport > 2000) {
                    lastReport = nowMs;
                    Log.i(TAG, String.format(java.util.Locale.US,
                            "state mode=%s bpm=%.1f period=%d locked=%s quiet=%s level=%.4f "
                                    + "clock=%.2f lostToLead=%d figure=%s record=%s pos=%.1f "
                                    + "playing=%s",
                            mode,
                            local.bpm(), local.periodFrames(), local.locked(), local.quiet(),
                            local.level(), local.audioTime(), local.droppedForLead(),
                            local.describeFigures(),
                            recorder == null ? (wantToRecord ? "waiting" : "off")
                                    : (recorder.usable() ? "on" : "void"),
                            Playback.positionSeconds, Playback.playing));
                }
            }
        } catch (Throwable t) {
            Log.e(TAG, "capture failed", t);
            status = "capture failed: " + t.getClass().getSimpleName();
        } finally {
            // A pass that was still being recorded when the service stopped is
            // finished properly rather than left behind: it is kept if it turned
            // out to be the whole song and deleted if it did not. The first
            // version simply let the file sit there - the phone had a two second
            // fragment of a song in the cache directory with nothing in the
            // index to say what it was.
            if (recorder != null) recorder.finish(Playback.durationSeconds, Playback.trackTitle);
            if (record != null) {
                try {
                    record.stop();
                } catch (Exception ignored) {
                }
                record.release();
            }
            if (projection != null) projection.stop();
            running = false;
        }
    }

    /**
     * Prefer a real chart, then a MIDI file, then the audio itself.
     *
     * The order is by how much the score can be trusted: a chart is what an
     * author wanted the player to hit, a MIDI is what the notes are, and the
     * analysis is what the drums happened to sound like.
     */
    private void pickChart(ChartLibrary library, MidiLibrary midi) {
        ChartFile match = library.find(Playback.trackArtist, Playback.trackTitle);
        if (match != null) {
            chartPlayer.load(match.notes, match.songName);
            mode = "chart:" + match.songName;
            Log.i(TAG, "chart found: " + match.songName + " (" + match.notes.size()
                    + " notes, " + match.difficulty + ")");
            return;
        }

        File file = midi.find(Playback.trackArtist, Playback.trackTitle);
        if (file != null) {
            try {
                MidiChart chart = MidiChart.fromFile(file, midiConfig());
                if (!chart.notes.isEmpty()) {
                    chartPlayer.load(chart.notes, file.getName());
                    mode = "midi:" + file.getName();
                    Log.i(TAG, "midi found: " + file.getName() + " (" + chart.summary() + ")");
                    return;
                }
                Log.i(TAG, "midi " + file.getName() + " left no playable notes");
            } catch (Exception e) {
                Log.w(TAG, "could not read " + file.getName() + ": " + e.getMessage());
            }
        }

        chartPlayer.unload();
        if (panel != null) panel.setWaveform(null);
        mode = "audio";
        Log.i(TAG, "no chart or MIDI for this song, generating notes from the audio");
    }

    /** What a MIDI file has to become for this game to play it. */
    private static MidiChart.Config midiConfig() {
        MidiChart.Config config = new MidiChart.Config();
        config.lanes = 1;          // the panel is one lane wide
        config.minGapSeconds = MidiChart.DEFAULT_MIN_GAP_SECONDS;
        config.holdSeconds = MidiChart.DEFAULT_HOLD_SECONDS;
        config.accentVelocity = MidiChart.DEFAULT_ACCENT_VELOCITY;
        return config;
    }

    /**
     * Play this song from the chart made for it last time.
     *
     * The recording was of the audio itself, so the analysis here is the same
     * one a file would get: the grid fitted to the whole song, every bar read as
     * the bar it is, no lead time to respect because there is nothing left to
     * predict.
     *
     * @return true when there was a usable recording and its chart is playing
     */
    /**
     * Whether this pass already holds the song.
     *
     * Nine tenths of the reported length is the bar, the same one the cache
     * itself uses: a recording that reaches it is the song, and whatever the
     * player does with its position afterwards cannot make it less of one.
     */
    private boolean coversWholeSong(SongCache.Recorder recorder) {
        double duration = Playback.durationSeconds;
        return duration > 0 && recorder.secondsWritten() >= duration * 0.9;
    }

    /** Take the recording as the answer, and say so in the log. */
    private void keepRecording(SongCache.Recorder recorder) {
        boolean kept = recorder.finish(Playback.durationSeconds, Playback.trackTitle);
        Log.i(TAG, kept
                ? String.format(java.util.Locale.US,
                        "cached %.0fs of \"%s\" - the next play of it will be charted",
                        recorder.secondsWritten(), Playback.trackTitle)
                : "threw this pass away: it was not the whole song");
    }

    private boolean playCachedChart(SongCache cache, String key) {
        if (key == null) return false;
        short[] pcm = cache.read(key);
        if (pcm == null) return false;
        try {
            OfflineChart chart = OfflineChart.analyse(pcm, OfflineChart.Difficulty.NORMAL);
            if (chart.notes.isEmpty()) return false;
            chartPlayer.load(chart.notes, Playback.trackTitle);
            mode = "cached:" + Playback.trackTitle;
            // From here the lane draws the song's own drums. It also answers
            // "was this one cached?" without opening a screen: a waveform means
            // yes, bricks mean it is still being predicted.
            if (panel != null) panel.setWaveform(chart.envelope);
            cache.touch(key);
            Log.i(TAG, "played from cached audio: " + chart.summary());
            return true;
        } catch (Throwable t) {
            Log.w(TAG, "could not chart the cached audio", t);
            return false;
        }
    }

    /** Where the audio of played songs is kept. */
    private File pcmDirectory() {
        File dir = getExternalFilesDir("pcm");
        if (dir != null && !dir.exists()) dir.mkdirs();
        return dir;
    }

    /**
     * Where MIDI files go, created so that it can be found without the app.
     *
     * Android/data/com.fallenangel.overlaybeat/files/midi is reachable from a
     * file manager and from adb, which is the whole point: the game cannot
     * download charts for the music a person already owns.
     */
    private File midiDirectory() {
        File dir = getExternalFilesDir("midi");
        if (dir != null && !dir.exists()) dir.mkdirs();
        return dir;
    }

    private int dp(int value) {
        return Math.round(getResources().getDisplayMetrics().density * value);
    }
}
