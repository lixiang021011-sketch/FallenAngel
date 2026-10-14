package com.fallenangel.listeningai;
import android.app.Activity;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.graphics.PixelFormat;
import android.media.AudioAttributes;
import android.media.AudioFormat;
import android.media.AudioPlaybackCaptureConfiguration;
import android.media.AudioRecord;
import android.media.projection.MediaProjection;
import android.media.projection.MediaProjectionManager;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.provider.Settings;
import android.util.Log;
import android.view.Gravity;
import android.view.View;
import android.view.WindowManager;
import com.fallenangel.listeningai.listening.ListeningAnalyzer;
import com.fallenangel.listeningai.listening.ListeningEvent;
import com.fallenangel.listeningai.listening.PauseQuestion;
import com.fallenangel.listeningai.listening.QuestionTally;
import com.fallenangel.listeningai.listening.RecentAudio;
import com.fallenangel.listeningai.clip.ExplanationClient;
import com.fallenangel.listeningai.clip.ModelHost;
import com.fallenangel.listeningai.clip.ModelSpec;
import com.fallenangel.listeningai.clip.OnnxEventEngine;
import com.fallenangel.listeningai.clip.StyleReading;
import com.fallenangel.listeningai.listening.CollisionNote;
import com.fallenangel.listeningai.listening.CandidateFilter;
import com.fallenangel.listeningai.listening.ChordChangeGate;
import com.fallenangel.listeningai.listening.ChordDisplay;
import com.fallenangel.listeningai.listening.ChordTimeline;
import com.fallenangel.listeningai.listening.ChordTrack;
import com.fallenangel.listeningai.listening.DrumPattern;
import com.fallenangel.listeningai.listening.Harmony;
import com.fallenangel.listeningai.listening.HeldPause;
import com.fallenangel.listeningai.listening.NoveltyGate;
import com.fallenangel.listeningai.listening.query.EventCandidate;
import com.fallenangel.listeningai.listening.query.FeedbackRecord;
import com.fallenangel.listeningai.listening.query.LocalQueryPolicy;
import com.fallenangel.listeningai.listening.query.MusicMoment;
import com.fallenangel.listeningai.listening.query.QueryContext;
import com.fallenangel.listeningai.listening.query.QueryDecision;
import com.fallenangel.listeningai.listening.query.QueryDump;
import com.fallenangel.listeningai.listening.query.QueryPolicy;
import com.fallenangel.listeningai.listening.query.QueryPolicyConfig;
import com.fallenangel.listeningai.listening.query.TemplateQuestionRenderer;
import com.fallenangel.listeningai.listening.questions.QuestionBank;
import com.fallenangel.listeningai.listening.questions.QuestionPicker;
import com.fallenangel.listeningai.listening.questions.QuestionTemplate;
import com.fallenangel.listeningai.listening.StyleFeedback;
import com.fallenangel.listeningai.localai.InferenceSession;
import com.fallenangel.listeningai.localai.MusicObservation;
import com.fallenangel.listeningai.localai.SectionObservation;
import com.fallenangel.listeningai.localai.SectionSession;
import com.fallenangel.listeningai.localai.StreamingLocalInferenceEngine;
import java.io.File;
import java.util.List;
/**
* Listens to what is playing, and asks about the moments the listener stops on.
 *
 * Everything here exists to serve one loop: a person pauses because something
 * mattered, the seconds before that are handed to the analysis, the analysis
 * proposes the explanations it can defend, the person picks one, and the
 * proposal and the answer are both written down against the music's own clock.
 *
 * The app never decides what is important. It decides what it is *uncertain
*
 * about, and asks. */
 public class ListeningService extends Service {
    public static final String EXTRA_RESULT_DATA = "resultData";
    /**
     * Which of the two things this session is for.
     *
     * Chosen on the way in rather than discovered by being interrupted: a question
     * covers the chord box, and the chord box is a distraction while annotating, so
     * "may I interrupt you" is a question about the session and belongs on the button
     * that starts it.
     */
    public static final String EXTRA_INTERRUPTS = "interrupts";
    

/** Where the button was left, so it comes back there. */
    static final String PREFS = "listening";
    private static final String PREF_X = "widgetX";
    private static final String PREF_Y = "widgetY";
    

/** Where the reasoning model is, if there is one configured. */
    static final String PREF_ENDPOINT = "cloudEndpoint";
    static final String PREF_KEY = "cloudKey";
    static final String PREF_MODEL = "cloudModel";
    /**
     * Whether the app may interrupt at all - questions and remarks both.
     *
     * Off on this branch, and that is the point of it: what this branch is for is
     * hearing the music with the harmony on screen, and a card that appears over it
     * is the app interrupting the thing it is supposed to accompany. Measured
     * 2026-10-13, "提问会干扰听歌".
     *
     * A preference rather than a constant because the answer is not the same every
     * day: on a listening evening the harmony is the feature and the questions are
     * noise, and on an annotating evening it is the other way round. Which of the two
     * is the default is a decision about this branch, not about the person.
     */
    static final String PREF_INTERRUPTS = "interrupts";
    private static final String TAG = "ListeningAI";
    private static final String CHANNEL_ID = "listening_ai";
    private static final int NOTIFICATION_ID = 6201;
    

/** How much audio is kept so a pause can be asked about. */
    private static final double BUFFER_SECONDS = 10;
    

/** How much of it a question is about. */
    private static final double WINDOW_SECONDS = 8;
    

/**
     * Asked at most this often, however much the listener pauses.
  *
  * It only has to be long enough that one pause cannot be mistaken for two -
  * the app's own pause and the listener's, a stutter in the audio. Thirty
  * seconds looked like politeness and behaved like a fault: a listener who
  * paused twice in a row inside it got asked once and then nothing, with no
  * sign of why.  */
    private static final long QUESTION_GAP_MILLIS = 8_000;
    /** 兜底：thinking 卡片最多停留这么久，超时就收起来并说明（见 askAbout） */
    // How long the thinking card is allowed to stand before it is taken down and
    // the music given back. It has to outlast whatever is being waited for, and
    // that changed when the model moved onto this desk: a local 7B has to encode
    // the eight seconds of audio before it can say anything, which is ten seconds
    // on its own. At fifteen the net was pulling the card down while a local model
    // was still hearing the clip - the question never came, and nothing said why.
    private static final long BUSY_STAGE_TIMEOUT_MILLIS = 60_000;
    

/**
     * How long after a track starts the app may interrupt on its own.
  *
  * The first seconds of a song are a change by definition - the model has
  * just been handed music it has never heard - so asking about them would be
  * asking about the song starting.  */
    private static final long SELF_ASK_SETTLE_MILLIS = 20_000;
    

/**
     * How long the music waits for an answer to a question the app started.
  *
  * The listener did not ask to be stopped, so if they ignore the card the
  * music has to come back on its own - a question the app began must not be
  * able to leave the music stopped for good.  */
    private static final long SELF_ASK_PATIENCE_MILLIS = 20_000;
    

/**
     * How long the app waits between noticing a change and asking about it.
  *
  * A change is noticed the moment its first half second arrives, and the
  * analysis reads the last eight seconds: asked at that instant, the window
  * still held the music before the change and the analysis found nothing to
  * say - measured, at 202.8s, "no explanation" and then a second later
  * "编曲密度增加 0.57" from the same audio.  */
    private static final long SELF_ASK_LEAD_MILLIS = 1_500;
    

/** Above this, a novelty is written down as it happens, for tuning the gate. */
    private static final double NOTEWORTHY_NOVELTY = 0.08;
    

/**
     * What a question of the app's own answers to, and how far apart two are.
  * One trigger, because one signal is actually measured. The bank also holds
  * questions for instrument changes and for section boundaries, and there is
  * no honest source for either yet - a question fired from a signal nobody
  * measured is a guess with a label on it, and the answers to it would say
  * something about the guess rather than about the music.  */
    private static final String SELF_ASK_TRIGGER = "NOVELTY_HIGH";
    

/** The listener's own rule for how often the app may ask. */
    private static final int MIN_QUESTION_GAP_BARS = 4;
    

/** What the launcher screen shows. */
    static volatile String status = "idle";
    static volatile boolean running;
    static volatile int questionsAsked, answersGiven;
    /**
     * What this song's questions came to. Kept beside the static counters rather
     * than in the log, because it is read while the song is still going: how long
     * the listener took to decide is a property of this song, not of the file.
     */
    private final QuestionTally tally = new QuestionTally();
    /** When the question on screen appeared, for the answer time. */
    private long questionShownAtMillis;
    private static final long REMARK_GAP_MILLIS = 45_000;
    /** How long a remark stays on the card before it takes itself away. */
    private static final long REMARK_VISIBLE_MILLIS = 12_000;
    /**
     * Below this share of the chroma explained by one triad, the app does not
     * claim a chord.
     *
     * Real music measured 0.33 to 0.50 on a track where every window came out
     * diatonic; clean triads come out above 0.9. A reading under a third means
     * the harmony is buried deep enough that saying it would be a guess dressed
     * as a measurement.
     */
    private static final double HARMONY_MIN_CONFIDENCE = 0.30;
    /**
     * How much better another key has to fit before this song is said to be in it.
     *
     * Neighbouring keys share six of their seven notes, so the best-matching one
     * moves on noise. A tenth of a correlation is about the distance between a
     * key and its neighbours, and well short of what a modulation looks like.
     */
    private static final double KEY_MARGIN = 0.10;
    /**
     * The song's chroma so far, which is what the key is read from.
     *
     * A key is a statement about a piece, not about eight seconds: the same four
     * chords are in C major, G major or A minor depending on what came before
     * them. Accumulating means the degree gets steadier the longer a song runs,
     * which is the right way round - the app is more sure of the key in the
     * second verse than in the first bar.
     */
    private final double[] songChroma = new double[12];
    /** The key this song is in, held until the evidence clearly moves. */
    private Harmony.Key songKey;
    /** What the chord box is showing, so it is only touched when it changes. */
    private String shownChord = "";
    /** The chord before the one on screen, for the second line of the box. */
    private String previousChord = "";
    /** Whether what is in the box came from an earlier listen. */
    /** Where the chord on screen came from: "" measured, "缓存" an earlier listen, "文件" the Workbench. */
    private String shownChordSource = "";
    /**
     * What this song's chords were last time, if it has been played before.
     *
     * Read once per song from the record of an earlier listen and then used instead
     * of measuring: the second time through, the app knows what is coming and can
     * show it on time, which measuring cannot do - see {@link ChordTimeline}.
     */
    private ChordTimeline timeline;
    private boolean lookedForTimeline;
    /** Whether the moment being listened to was last answered from the earlier listen. */
    private boolean cacheWasOn;
    /**
     * The song's chords as the Workbench worked them out, if it has handed a file over.
     *
     * Read once per song and preferred over both the cache and measuring: it is the
     * same answer, worked out with the whole song in front of whoever made it.
     */
    private ChordTrack chordTrack;
    private boolean lookedForChordTrack;
    /** When the last seek happened, so a pause caused by one is not read as attention. */
    private long lastSeekMillis;
    private static final long SEEK_QUIET_MILLIS = 2_500;
    /**
     * How long the app stops asking a model that has just been unreachable.
     *
     * Without this, every question pays the connection timeout again: measured
     * 2026-10-13, with the server on this desk stopped, each question showed
     * "正在问模型…" for ten seconds before falling back to the rules, and a seek that
     * produced two questions produced twenty.
     */
    private static final long MODEL_SILENT_MILLIS = 60_000;
    private long modelSilentUntilMillis;
    /**
     * Whether the grid in use came from a file rather than from tracking.
     *
     * Held so that the tempo can be shown in another colour: the number looks the
     * same either way, and the difference between a guess that drifts and a value
     * worked out in advance is the whole reason for importing anything.
     */
    private boolean gridFromFile;
    /**
     * The grid in charge, when it came from a file.
     *
     * The tracker keeps running and keeps its own opinion, and from here on none of
     * it is read: position, bar, beat and the tempo on screen all come from these
     * beat times. That is the difference between importing a grid and persuading the
     * tracker for a few seconds - measured 2026-10-14, a lock of 136 was back to 90.7
     * four seconds later.
     */
    private com.fallenangel.listeningai.listening.WorkbenchGrid importedGrid;
    private String gridSource = "";
    /** The song whose file has been looked for already, so it is looked for once. */
    private String gridLookedUpFor = "";

    /**
     * Read this song's grid from a file, if one is there, and put it in charge.
     *
     * Called wherever a song turns out to have no grid - at the start, after a
     * silence, after a track change - because all three are the same situation: the
     * app is about to guess, and a file may already know.
     */
    private void importGridForThisSong(BeatDetector grid) {
        // Every refusal is written down: a reading that was in the file and was not
        // used is the difference between "there is no file" and "the file said
        // something I did not take", and only one of those is a bug.
        java.util.List<String> journal = new java.util.ArrayList<>();
        com.fallenangel.listeningai.listening.WorkbenchGrid imported = workbenchGrid(song, journal);
        for (String line : journal) Log.i(TAG, "读法: " + line);
        if (imported == null) {
            gridFromFile = false;
            gridSource = "";
            return;
        }
        grid.setMeter(Math.max(1, imported.beatsPerBar));
        lastMeter = Math.max(1, imported.beatsPerBar);
        grid.lockTempoAndDownbeat(imported.bpm, imported.firstBeat(), true);
        anchoredBpm = imported.bpm;
        gridFromFile = true;
        gridSource = imported.reading;
        importedGrid = imported;
        fileFlashes = 0;
        lastFlashedBar = Long.MIN_VALUE;
        if (song != null) {
            song.event(gridSourceLine(imported), imported.firstBeat());
            song.say("记", "网格来自 " + imported.reading
                    + "（" + imported.beatCount() + " 个拍点"
                    + (imported.caution ? "，谨慎" : "") + "）", imported.firstBeat());
        }
    }
    /**
     * The listener's pause that arrived while a question was up.
     *
     * Held until the question is out of the way, and only for as long as an answer
     * could plausibly still be about that moment: a pause from two minutes ago is
     * not what they are thinking about now, and asking about it would be the app
     * talking about itself.
     */
    private RecentAudio.Window heldPauseWindow;
    private long heldPauseAtMillis;
    private double heldPauseSeconds;
    private static final long HELD_PAUSE_PATIENCE_MILLIS = 45_000;
    /** The rule for what to do with a held pause, in one readable place. */
    private final HeldPause heldPause = new HeldPause(HELD_PAUSE_PATIENCE_MILLIS);
    /** Speaking about the harmony: on the change, not on a timer. */
    private final ChordChangeGate chordChanges = new ChordChangeGate(20_000);
    /**
     * What the chord box is allowed to show.
     *
     * The display keeps its own cadence: a reading every second, three in a row
     * before it reaches the screen, and a clear lead before it replaces what is
     * already up. Sharing the remark's twenty-second gap made the box follow the
     * music at the speed of a sentence, which is not a speed anything is played at.
     */
    /**
     * Two readings in a row, which is two seconds at the rate this loop runs.
     *
     * Three was a second too slow and the reason is arithmetic: the loop is one pass
     * per second, so agreement over three is three seconds before a change is
     * allowed through at all.
     */
    private final ChordDisplay chordBox = new ChordDisplay(3);
    /**
     * The window the harmony is read from: two seconds, not the question's eight.
     *
     * The eight was borrowed from the pause question, where there has to be enough
     * music to say what was going on. For a chord it is the wrong tool - a chord
     * that lasts one bar is a quarter of that window, so the reading is a mixture
     * and takes seconds to tip after the music has already changed. Measured on 说
     * 说Crystal — 月: the display was frozen or late by roughly the window plus the
     * agreement, about ten seconds, which is most of a bar.
     *
     * Shorter is noisier, and the noise is real: at two seconds the top two chords
     * sit within a few thousandths of each other. That is what the two-readings
     * agreement is for.
     */
    private static final double HARMONY_WINDOW_SECONDS = 1.2;
    /**
     * How long after a bar line a change may still be attributed to that line.
     *
     * Chord changes happen *on* bar lines, and the app learns about them a moment
     * later - the window has to fill with the new chord before the reading moves.
     * Waiting for the next bar line then costs a whole bar, which is exactly the
     * complaint: "反应慢一小节". This grace is the difference between attributing a
     * change to the line it happened on and attributing it to the next one.
     */
    private static final long BEAT_GRACE_MILLIS = 250;
    private long lastBeatMillis;
    private long lastBarLineMillis;
    /**
     * How often the app may say something nobody asked for.
     *
     * The one number the whole "chatting" question comes down to: ten seconds is
     * a companion, three minutes is a machine that speaks occasionally. It is
     * three quarters of a minute because that is where it starts to be noticeable
     * without being constant - the listener's own answer, after hearing it, is
     * what should set it.
     */
    private final com.fallenangel.listeningai.listening.RemarkPolicy remarks =
            new com.fallenangel.listeningai.listening.RemarkPolicy(REMARK_GAP_MILLIS);
    static volatile String lastQuestion = "";
    private WindowManager windowManager;
    private QuestionCard card;
    private WindowManager.LayoutParams cardParams;
    private float dragStartX, dragStartY;
    

/** Views may only be touched from here, and a pause is noticed elsewhere. */
    private final Handler main = new Handler(Looper.getMainLooper());
    private Thread worker;
    private volatile boolean stopping;
    private static volatile long lastQuestionMillis;
    private static volatile int questionCount;
    

/** The local audio model, when one could be loaded. Never required. */
    private OnnxEventEngine onnx;
    private String onnxVersion = "";
    

/** The last thing the model said, in words, for the button and the card. */
    private volatile com.fallenangel.listeningai.clip.StyleReading reading;
    

/** The model noticed something; the capture loop does the asking about it. */
    private volatile boolean noticedSomething;
    

/**
     * When the model last said it heard a change.
  *
  * Kept after the question is asked, because the interesting thing about a
  * collision is that it happened at all - the listener stopping on the same
  * second the app noticed, in either order.  */
    private long lastNoticedMillis;
    

/** When the policy was last consulted. */
    

/** When the model noticed a change, so the asking can wait for the evidence. */
    private long noticedAtMillis;
    

/** What the floating window is showing, and whether anybody owes an answer. */
    private final CardStage stage = new CardStage();
    

/** When the app may decide on its own to stop the music and ask. */
    private final com.fallenangel.listeningai.listening.NoveltyGate selfAsk = com.fallenangel.listeningai.listening.NoveltyGate.standard();
    private long trackStartedMillis;
    

/** True while the music is held for a question the app started. */
    private boolean holdingMusic;
    private String styleProposalId = "";
    

/**
     * The song being listened to, and its two files.
  *
  * Everything written down goes here, so that what the app knows is grouped
  * by song rather than by evening, and times are counted from the start of
  * the song rather than from when somebody pressed start.  */
    private volatile com.fallenangel.listeningai.listening.SongLog song;
    

/** Our own clock at the moment the current song started. */
    private double songStartAudioTime;
    

/** Where in the song the capture loop is, for the writers off that thread. */
    private volatile double atSeconds;
    

/** How many songs this run has seen: what an unnamed one is called. */
    private int songNumber;
    

/** The last thing the model said, so the readable log is not a transcript of it. */
    private String lastReadableReading = "";
    

/** One call at a time, off the capture thread: the music must not wait. */
    private java.util.concurrent.ExecutorService cloud = java.util.concurrent.Executors.newSingleThreadExecutor();
    

/** Decides whether a moment becomes a question; thresholds come from the asset. */
    private QueryPolicyConfig queryConfig = QueryPolicyConfig.defaults();
    private QueryPolicy queryPolicy = new LocalQueryPolicy(QueryPolicyConfig.defaults());
    private final TemplateQuestionRenderer queryRenderer = new TemplateQuestionRenderer();
    

/** The questions this build may ask, read from the asset once. */
    private QuestionBank questionBank;
    /** The words a candidate may use, read once from the asset. */
    private com.fallenangel.listeningai.listening.CandidateVocabulary vocabulary;
    /** What has been offered and taken so far, read back from the record. */
    private com.fallenangel.listeningai.listening.AnswerMemory memory;
    

/** Which of them may be asked right now. One song's worth; replaced per song. */
    private QuestionPicker questions;
    

/** What this run has asked, for the policy's own manners. */
    private final java.util.ArrayDeque<String> askedTypes = new java.util.ArrayDeque<>();
    private final java.util.ArrayDeque<Long> askedAtMillis = new java.util.ArrayDeque<>();
    

/** Questions shown and never answered. */
    private volatile int dismissCount;
    

/** The capture loop's beat grid, reachable from the button that calibrates it. */
    private volatile BeatDetector grid;
    

/** An anchoring attempt in progress, if any. */
    private com.fallenangel.listeningai.listening.BeatAnchor anchor = new com.fallenangel.listeningai.listening.BeatAnchor();
    private volatile boolean tapping;
    

/**
     * The presses of the attempt in progress, and the grids this song locked.
  * Both are what the export is made of: the presses because only they can say
  * what the listener actually did, the grids because the desktop validator
  * draws hypotheses and a grid is one.  */
    private final java.util.List<org.json.JSONObject> tapLines = new java.util.ArrayList<>();
    private final java.util.List<org.json.JSONObject> calibrations = new java.util.ArrayList<>();
    /** Which songs this run has played, and how often, for "I came back to this". */
    private final java.util.Set<String> playedTitles = new java.util.LinkedHashSet<>();
    private final java.util.Map<String, Integer> playCounts = new java.util.HashMap<>();
    

/** The last meter a person's bar-taps implied, 4 until somebody says otherwise. */
    private volatile int lastMeter = 4;
    

/**
     * The tempo a person gave this song, when they gave one.
  * The pill shows this rather than the tracker's own estimate: once somebody
  * has said what the tempo is, showing a different number underneath is the
  * app arguing with the person who could hear the music.  */
    private volatile double anchoredBpm;
    

/** The listener's tempo, the beat one they marked, and whether the grid still has them. */
    private volatile double anchorDownbeatSeconds = -1;
    private volatile boolean anchorInGrid;
    

/**
     * The song's bar 1, worked back from the anchor to the start of the song.
  * Not a second thing to ask the listener for: take the anchor's position in the
  * song, count backwards in whole bars, and round to the nearest bar line. The
  * rounding is what makes it stable - a hand is good to about a third of a beat
  * and half a bar is the tolerance, so two sessions of the same song land on the
  * same numbering even though neither tapping is exactly alike.
  * Recorded as "extrapolated", not as a fact: the way the music begins (an
  * intro, a pickup, a first bar that starts before the file does) is something
  * this cannot know, and a later mark or model can supersede it by appending
  * another record rather than by rewriting this one.  */
    private volatile double barOneSongSeconds = -1;
    private volatile int anchorBarNumber;
    private volatile double anchorSongSeconds = -1;
    

/** What tempo music can plausibly be: outside this, the taps measured fingers. */
    private static final double MIN_BEAT_BPM = 40;
    private static final double MAX_BEAT_BPM = 240;
    @Override public void onCreate() {
        super.onCreate();
        windowManager = (WindowManager) getSystemService(WINDOW_SERVICE);
    }
    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        startForegroundCompat();
        showCardLayer();
        Intent resultData = intent == null ? null : resultDataOf(intent);
        if (resultData == null) {
            status = "no projection result";
            stopSelf();
            return START_NOT_STICKY;
        }
        // Written down rather than held, so that the overlay and the settings screen
        // agree about what this session is, and so it survives the service being
        // restarted by the system mid-song.
        getSharedPreferences(PREFS, MODE_PRIVATE).edit()
                .putBoolean(PREF_INTERRUPTS, intent.getBooleanExtra(EXTRA_INTERRUPTS, false))
                .apply();
        if (worker == null) {
            stopping = false;
            worker = new Thread(() -> run(resultData), "capture");
            worker.start();
        }
        return START_NOT_STICKY;
    }
    @Override public void onDestroy() {
        stopping = true;
        if (worker != null) {
            try {
                worker.join(500);
            }
 catch (InterruptedException ignored) {
                Thread.currentThread().interrupt();
            }
            worker = null;
        }
        hideCard();
        running = false;
        stopForeground(STOP_FOREGROUND_REMOVE);
        super.onDestroy();
    }
    @Override public IBinder onBind(Intent intent) {
        return null;
    }
    @SuppressWarnings("deprecation") // the typed overload only exists on API 33+
    private static Intent resultDataOf(Intent intent) {
        return Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU ? intent.getParcelableExtra(EXTRA_RESULT_DATA, Intent.class) : intent.getParcelableExtra(EXTRA_RESULT_DATA);
    }
    private void startForegroundCompat() {
        NotificationManager manager = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        if (manager.getNotificationChannel(CHANNEL_ID) == null) {
            manager.createNotificationChannel(new NotificationChannel( CHANNEL_ID, "Listening AI", NotificationManager.IMPORTANCE_LOW));
        }
        Notification notification = new Notification.Builder(this, CHANNEL_ID) .setContentTitle("Listening AI is listening") .setContentText("Pause on anything that catches your ear") .setSmallIcon(android.R.drawable.ic_media_play) .setOngoing(true) .build();
        startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION);
    }
    // ------------------------------------------------------------------ overlay
    private void showCardLayer() {
        if (card != null) return;
        if (!Settings.canDrawOverlays(this)) {
            status = "overlay permission missing";
            return;
        }
        card = new QuestionCard(this);
        card.setVisibility(View.GONE);
        card.setOnOpen(() -> {
            // The resting button is the listener's own way in: if there is
            // nothing to answer, it shows what the app thinks it is hearing and
            // lets them say whether that is right.
            com.fallenangel.listeningai.clip.StyleReading current = reading; if (stage.tapShowsWhatItHears() && current != null && current.said()) {
                showWhatItHears(current);
            }
 else {
                card.open();
            }
            resizeCard(true);
        }
);
        card.setOnTapTempo(this::beginTapTempo);
        card.setDragListener(new QuestionCard.DragListener() {
            @Override public void onDragStart() {
                dragStartX = cardParams.x; dragStartY = cardParams.y;
            }
            @Override public void onDragBy(float dx, float dy) {
                cardParams.x = clampX(Math.round(dragStartX + dx)); cardParams.y = Math.max(0, Math.round(dragStartY + dy)); try {
                    windowManager.updateViewLayout(card, cardParams);
                }
 catch (Exception ignored) {
                }
            }
            @Override public void onDragEnd() {
                // Where it was left is where it comes back: the window sits over
                // somebody else's app, and having to move it again every time the
                // music starts would be worse than not moving it at all.
                getSharedPreferences(PREFS, MODE_PRIVATE).edit() .putInt(PREF_X, cardParams.x) .putInt(PREF_Y, cardParams.y) .apply();
            }
        }
);
        cardParams = new WindowManager.LayoutParams( dp(280), WindowManager.LayoutParams.WRAP_CONTENT, WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY, WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL, PixelFormat.TRANSLUCENT);
        cardParams.gravity = Gravity.TOP | Gravity.START;
        android.content.SharedPreferences prefs = getSharedPreferences(PREFS, MODE_PRIVATE);
        cardParams.x = prefs.getInt(PREF_X, dp(16));
        cardParams.y = prefs.getInt(PREF_Y, dp(140));
        try {
            windowManager.addView(card, cardParams);
            status = "listening";
        }
 catch (Exception e) {
            Log.e(TAG, "could not add the question card", e);
            status = "overlay failed: " + e.getClass().getSimpleName();
            card = null;
        }
        resizeCard(false);
        announce();
    }
    

/**
     * Grow or shrink the floating window with its content.
  *
  * The resting button and the question are the same window at two sizes; a
  * window that stayed the width of the widest thing it might say would sit
  * over somebody else's music being mostly empty.  */
    private void resizeCard(boolean expanded) {
        if (card == null || cardParams == null) return;
        int width = expanded ? dp(280) : WindowManager.LayoutParams.WRAP_CONTENT;
        if (cardParams.width == width) return;
        cardParams.width = width;
        // Growing at the right edge would push the window off the screen, so the
        // position is brought back into range at every size change.
        cardParams.x = clampX(cardParams.x);
        try {
            windowManager.updateViewLayout(card, cardParams);
        }
 catch (Exception ignored) {
        }
    }
    

/** Keep the window on screen, however wide it currently is. */
    private int clampX(int x) {
        int screen = getResources().getDisplayMetrics().widthPixels;
        int width = cardParams == null || cardParams.width <= 0 ? dp(280) : cardParams.width;
        return Math.max(0, Math.min(x, Math.max(0, screen - width)));
    }
    

/**
     * Show the window once, so that "it is running" has somewhere to be seen.
  *
  * Without this the first sign of the app working is a question, which may be
  * minutes away - and until then a floating-window app with no floating
  * window looks broken. It says where the questions will appear and what
  * makes one appear; pressing it is the same single tap the questions use.  */
    private void announce() {
        if (card == null) return;
        long token = stage.show(CardStage.Content.MESSAGE);
        main.postDelayed(() -> {
            if (card == null || !stage.stillShowing(token)) return; card.showMessage("正在监听。", "你在播放器里暂停时，我会在这个窗口问你一句" + "「刚才可能注意到的是什么」；我自己听到明显变化时也会问一句，" + "问完会把音乐接回去。平时点这个按钮，可以看到我觉得现在像什么，" + "对或不对都可以告诉我。", "知道了", choice -> {
                        card.collapse(); resizeCard(false);
            }
); resizeCard(true); card.setVisibility(View.VISIBLE);
        }
, 600);
    }
    private void hideCard() {
        if (card == null) return;
        try {
            windowManager.removeView(card);
        }
 catch (Exception ignored) {
        }
        card = null;
    }
    // ------------------------------------------------------------------ capture
    private void run(Intent resultData) {
        MediaProjection projection = null;
        AudioRecord record = null;
        try {
            MediaProjectionManager manager = (MediaProjectionManager) getSystemService(MEDIA_PROJECTION_SERVICE);
            projection = manager.getMediaProjection(Activity.RESULT_OK, resultData);
            if (projection == null) {
                status = "projection unavailable";
                return;
            }
            AudioPlaybackCaptureConfiguration config = new AudioPlaybackCaptureConfiguration.Builder(projection) .addMatchingUsage(AudioAttributes.USAGE_MEDIA) .addMatchingUsage(AudioAttributes.USAGE_GAME) .addMatchingUsage(AudioAttributes.USAGE_UNKNOWN) .build();
            AudioFormat format = new AudioFormat.Builder() .setEncoding(AudioFormat.ENCODING_PCM_16BIT) .setSampleRate(SAMPLE_RATE) .setChannelMask(AudioFormat.CHANNEL_IN_MONO) .build();
            int minimum = AudioRecord.getMinBufferSize(SAMPLE_RATE, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT);
            record = new AudioRecord.Builder() .setAudioFormat(format) .setBufferSizeInBytes(Math.max(minimum * 2, SAMPLE_RATE / 2)) .setAudioPlaybackCaptureConfig(config) .build();
            if (record.getState() != AudioRecord.STATE_INITIALIZED) {
                status = "AudioRecord failed to initialise";
                return;
            }
            InferenceSession inference = new InferenceSession(new StreamingLocalInferenceEngine());
            SectionSession sections = new SectionSession();
            BeatDetector grid = new BeatDetector();
            this.grid = grid;
            startModel();
            RecentAudio recent = new RecentAudio(BUFFER_SECONDS);
            startSong(grid, Playback.trackTitle);
            short[] buffer = new short[2048];
            String lastTrack = Playback.trackKey;
            long lastSeek = Playback.seekGeneration;
            boolean lastPlaying = Playback.playing;
            String sectionLabel = "";
            long sectionBoundarySample = -1;
            long observations = 0;
            long lastReport = 0;
            long lastRefresh = 0;
            long lastRebind = 0;
            boolean announcedReady = false;
            boolean warnedAboutWatcher = false;
            boolean playingNow = true;
            running = true;
            trackStartedMillis = android.os.SystemClock.elapsedRealtime();
            queryConfig = readQueryConfig();
            queryPolicy = new LocalQueryPolicy(queryConfig);
            Log.i(TAG, "query policy: " + queryConfig.toJson());
            // Read once, here, so a bank that does not parse says so at the start
            // rather than in the middle of a song.
            questionBank();
            record.startRecording();
            while (!stopping) {
                int read = record.read(buffer, 0, buffer.length);
                if (read > 0) {
                    grid.push(buffer, read);
                    grid.drainBeats();
                    feedModel(buffer, read);
                    if (Playback.playing) {
                        // Only audio that was playing, so the buffer's timeline is
                        // the music's and a pause does not fill it with silence.
                        recent.write(buffer, 0, read, Math.round(grid.audioTime() * SAMPLE_RATE));
                    }
                    for (MusicObservation observation : inference.accept(buffer, 0, read, Playback.available, Playback.trackKey, Playback.seekGeneration, Playback.playing)) {
                        observations++;
                    }
                    for (SectionObservation section : sections.accept(buffer, 0, read, Playback.available, Playback.trackKey, Playback.seekGeneration, Playback.playing)) {
                        sectionLabel = section.label;
                        sectionBoundarySample = Math.round(section.boundarySeconds() * SAMPLE_RATE);
                        Log.i(TAG, String.format(java.util.Locale.US, "[Sections] %s %s->%s at=%.2fs score=%.3f", section.kind, section.previousLabel, section.label, section.boundarySeconds(), section.novelty));
                    }
                }
                if (Playback.available) {
                    if (!Playback.trackKey.equals(lastTrack)) {
                        lastTrack = Playback.trackKey;
                        sectionLabel = "";
                        // A new song is not a change in the last one.
                        selfAsk.reset();
                        forgetTheOldSong(grid);
                        // A new song is not the same song further along: the tempo
                        // hypothesis from the last one is worthless here.
                        grid.hardReset("track");
                        // And so is the tempo a person gave for the last one.
                        anchoredBpm = 0;
                        lastMeter = 4;
                        trackStartedMillis = android.os.SystemClock.elapsedRealtime();
                        Log.i(TAG, "track: " + Playback.trackTitle);
                        String previous = song == null ? "" : song.label();
                        if (song != null) {
                            // When they left, not where the clock already is: by now
                            // the player is on the next song and reports 00:00.0.
                            double leftAt = Math.max(lastKnownSongSeconds(), 0);
                            song.say("听", "切到下一首", leftAt);
                            // The behaviour goes into the machine log too, not only
                            // the sentences: the desktop validator reads the JSON,
                            // and "they switched away here" is a fact about the
                            // session that a grid cannot show.
                            song.event(songChangeLine(Playback.trackTitle), leftAt);
                        }
                        startSong(grid, Playback.trackTitle);
                        if (song != null) {
                            song.event(songEnterLine(previous), 0);
                        }
                        // Unless somebody already worked this song out - and only now,
                        // because until startSong ran, `song` was still the *previous*
                        // one. Importing before it looked up the last song's file and
                        // handed its grid to this one: measured 2026-10-14, "接口接错
                        // 了，是不是导入了多余的错网格" - it did exactly that, and the
                        // GRID_SOURCE line went into the record of the song before.
                        // The file is looked for from the loop, wherever this song
                        // turns out to have no grid - see importGridForThisSong.
                        gridFromFile = false;
                        importedGrid = null;
                        gridSource = "";
                        // And the fact that this song's file was looked for: a second
                        // play of the same song is a new song, and it goes and looks
                        // again. Measured 2026-10-14: without this the file was read on
                        // the first play only, and every replay tracked the beat itself.
                        gridLookedUpFor = "";
                        Log.i(TAG, "grid hard reset: session=" + grid.gridSessionId() + " reason=" + grid.lastResetReason());
                    }
                    if (Playback.seekGeneration != lastSeek) {
                        lastSeek = Playback.seekGeneration;
                        lastSeekMillis = System.currentTimeMillis();
                        // Neither is a jump within a song: the seconds before it
                        // are not the seconds this moment is being compared with.
                        selfAsk.reset();
                        forgetTheOldSong(grid);
                        // Same song, so the tempo still holds; the phase and the
                        // bar identity do not, and must be found again.
                        grid.onSeek();
                        if (song != null) {
                            double at = whereInSong(grid);
                            song.say("听", String.format(java.util.Locale.US, "倒带或跳转（保留速度假设 %.0f，重建相位），现在在 %s", grid.bpm(), com.fallenangel.listeningai.listening.SongLog.clock(at)), at);
                            // The jump is recorded where they jumped *from*, which
                            // is where the record has got to, not where they landed.
                            song.event(seekLine(lastKnownSongSeconds(), at),
                                    Math.max(lastKnownSongSeconds(), at));
                        }
                    }
                    if (Playback.playing != lastPlaying) {
                        boolean wasPlaying = lastPlaying;
                        lastPlaying = Playback.playing;
                        if (wasPlaying && !Playback.playing) {
                            if (PlaybackControl.pausedByUs() && stage.awaitingAnswer()) {
                                Log.i(TAG, "the music is being held for our own question");
                            }
 else if (justSeeked()) {
                                // A pause that is part of a jump is not attention. Seeking
                                // backwards stops the player for a moment, and reading that
                                // as "the listener stopped here" put a question about a
                                // moment nobody chose in front of them - measured
                                // 2026-10-13, twice in a row after one seek.
                                Log.i(TAG, "the music stopped just after a seek; not a moment of theirs");
                            }
 else {
                                askAbout(Asked.BY_LISTENER, grid, recent, sectionLabel, sectionBoundarySample);
                            }
                        }
                    }
                }
                // What counts as a pause does not have to come from the player.
                // MIUI refuses to bind the notification listener for a freshly
                // installed app until it is allowed to autostart, and the media
                // session is the only thing that says "paused" directly - so the
                // trigger falls back to the audio itself: when the music stops,
                // what is being captured goes quiet, and staying quiet is what a
                // pause is. The session is still preferred when it is readable,
                // because it also knows the track and the position.
                long now = android.os.SystemClock.elapsedRealtime();
                boolean heardPlaying = Playback.available ? Playback.playing : !grid.quiet();
                atSeconds = whereInSong(grid);
                boolean barLine = flashOnBarLine(grid);
                if (barLine) lastBarLineMillis = System.currentTimeMillis();
                boolean beatLine = onBeatLine(grid);
                if (beatLine) lastBeatMillis = System.currentTimeMillis();
                // A grid from a file is looked for whenever this song has none - not
                // only when the track changes. Measured 2026-10-14: starting to listen
                // while the song was already playing, or a grid dropped by a silence,
                // meant the import never ran at all, and the tempo on screen was the
                // tracker's guess while the file that knew better sat unread.
                if (!gridFromFile && song != null && !grid.locked()
                        && !song.label().equals(gridLookedUpFor)) {
                    gridLookedUpFor = song.label();
                    importGridForThisSong(grid);
                }
                keepTheAnchor(grid);
                // Asking on our own: the model said something changed, and the
                // asking happens here rather than where that was noticed,
                // because this is where the audio of the last seconds is.
                // Asking on our own, off the model's own change signal. The query
                // policy is built and tested but not in this path: it decides from
                // confidences nobody has calibrated yet, so it would be a decision
                // layer reading a ruler with no marks on it.
                if (noticedSomething) {
                    noticedSomething = false;
                    noticedAtMillis = now;
                }
                if (noticedAtMillis > 0 && now - noticedAtMillis >= SELF_ASK_LEAD_MILLIS) {
                    noticedAtMillis = 0;
                    boolean settled = now - trackStartedMillis > SELF_ASK_SETTLE_MILLIS;
                    if (heardPlaying && settled && !stage.awaitingAnswer() && !PlaybackControl.pausedByUs()) {
                        Log.i(TAG, "the model heard a change; holding the music to ask about it");
                        if (song != null) {
                            song.say("我", String.format(java.util.Locale.US, "听到变化（%.2f），暂停下来问一句", lastNovelty), atSeconds);
                        }
                        PlaybackControl.pause(this);
                        askAbout(Asked.BY_US, grid, recent, sectionLabel, sectionBoundarySample);
                    }
                }
                // Saying something without asking: the other half of the
                // interaction. It does not stop the music and it does not wait for
                // an answer, so the only thing it spends is attention.
                //
                // The trigger is the harmony moving, not the clock: a remark that
                // lands mid-chord can only name the chord the listener is already
                // hearing, while a remark at the change says something that just
                // happened. With no chord to read - low confidence, or no audio -
                // the app falls back to speaking now and then about what it hears.
                boolean busy = stage.awaitingAnswer() || holdingMusic;
                // A song heard before is read from its own record instead of measured
                // again: the readings are the same, and having them in advance is what
                // removes the delay rather than shrinking it.
                if (!lookedForTimeline && song != null) {
                    lookedForTimeline = true;
                    timeline = findTimeline(song);
                }
                // And a song the Workbench has already worked out is read from that file
                // instead of either: a measurement of the same thing, made with the whole
                // song in front of it, is not something to measure again on a phone.
                if (!lookedForChordTrack && song != null) {
                    lookedForChordTrack = true;
                    chordTrack = chordTrack(song);
                }
                Harmony.Reading heardNow = null;
                String harmonyNow;
                boolean fromCache = false;
                boolean fromFile = false;
                double atNow = whereInSong(grid);
                ChordTrack.Chord written = chordTrack == null ? null : chordTrack.at(atNow);
                boolean fileHere = written != null;
                boolean cacheHere = !fileHere && timeline != null && timeline.useful() && Playback.available
                        && timeline.covers(atNow);
                if (fileHere) {
                    harmonyNow = written.label;
                    fromFile = true;
                } else if (cacheHere) {
                    harmonyNow = timeline.at(atNow);
                    fromCache = true;
                } else {
                    heardNow = harmonyNow(recent);
                    harmonyNow = heardNow == null ? "" : Harmony.sentence(heardNow, songKey);
                }
                // Which half bar is worth a listener's ear, said the way the box says
                // things: the file's three readings disagreeing about a chord is the one
                // thing in it that is a question rather than an answer.
                boolean doubtedChord = fileHere && written.disagreed;
                // Where the earlier listen runs out - at the end of a song that was cut
                // short, or in a hole in the middle of one - is asked again every moment
                // rather than remembered as an ending: measured 2026-10-14, a cache with
                // a gap in it was held over the gap, and the box sat on one chord for
                // fifty-two seconds while the music went on ("有字，但不跟着音乐变").
                // Both directions are said out loud, because a cache that quietly stops
                // and one that quietly starts again look the same from the box.
                if (timeline != null && timeline.useful() && Playback.available
                        && cacheHere != cacheWasOn) {
                    cacheWasOn = cacheHere;
                    Log.i(TAG, cacheHere
                            ? "the earlier listen covers " + (int) atNow + "s; reading it back"
                            : "nothing in the earlier listen at " + (int) atNow
                                    + "s; measuring until it does");
                }
                // Shown whether or not anything is being said: a chord is a state,
                // and the box is where a state belongs. The remark below is an
                // event, and it is about the change, not about the chord.
                // A change may land on the beat itself, or in the moment just after
                // it - which is when the app finds out about a change that happened
                // on that beat. Everything else waits for the next beat, which is at
                // most half a second away.
                boolean canSwitch = beatLine
                        || System.currentTimeMillis() - lastBeatMillis < BEAT_GRACE_MILLIS
                        // The tracker's lock state decides only when the tracker owns
                        // the grid: with a file in charge the beats above are the only
                        // moments, whatever the tracker thinks it has locked.
                        || (!gridFromFile && (grid == null || !grid.locked()))
                        // A written chord track is the one case where the box does not
                        // have to wait for a beat: the moment the chord changes is in the
                        // file, to the millisecond, and it is a musical moment already.
                        || fileHere;
                String boxNow = chordBox.consider(harmonyNow,
                        heardNow == null ? 1.0 : heardNow.margin(), canSwitch);
                showChord(boxNow, fromFile ? "文件" : (fromCache ? "缓存" : ""),
                        doubtedChord);
                // Written either way, with where it came from. A listen that reads a
                // cache used to leave an empty record behind, and since the newest
                // record is the one the next listen reads, an empty one poisoned the
                // cache for the listen after it - the app's own file, hiding the good
                // one behind it.
                if (heardPlaying) {
                    writeChordReading(heardNow, harmonyNow, boxNow,
                            fromFile ? "file" : (fromCache ? "cache" : "measured"));
                }
                boolean due = !boxNow.isEmpty()
                        ? chordChanges.due(now, heardPlaying, busy, boxNow)
                        : remarks.due(now, heardPlaying, busy) && reading != null && reading.said();
                // Never while they are counting: the tap button is in the same card
                // the remark would take over, and being talked to while tapping is
                // being interrupted in the middle of an answer.
                // And never when this app is set not to interrupt: a remark covers the
                // same pill the harmony box lives in, so it interrupts the very thing
                // this branch exists to show.
                if (tapping || !interrupts()) due = false;
                if (due) {
                    if (!boxNow.isEmpty()) {
                        chordChanges.said(now, boxNow);
                    } else {
                        remarks.spoke(now);
                    }
                    saySomething(reading, recent);
                }
                // The pause that was held under our own question: asked about now
                // that the card is clear, with the audio it actually happened in.
                // Only while it is still recent - a pause from two minutes ago is
                // not what they are thinking about, and asking about it would be
                // the app talking about itself.
                if (heldPauseWindow != null && !stage.awaitingAnswer() && !holdingMusic) {
                    long nowHeld = System.currentTimeMillis();
                    HeldPause.Decision decision = heldPause.decide(nowHeld, heldPauseAtMillis,
                            stage.awaitingAnswer(), holdingMusic);
                    if (decision == HeldPause.Decision.TOO_OLD) {
                        Log.i(TAG, String.format(java.util.Locale.US,
                                "the held pause is %.0fs old; it stays in the record, unasked",
                                (nowHeld - heldPauseAtMillis) / 1000.0));
                        heldPauseWindow = null;
                    } else if (decision == HeldPause.Decision.ASK && !PlaybackControl.pausedByUs()) {
                        RecentAudio.Window window = heldPauseWindow;
                        heldPauseWindow = null;
                        Log.i(TAG, "asking about the pause that was held under our own question");
                        askAbout(Asked.BY_LISTENER, grid, recent, sectionLabel,
                                sectionBoundarySample, window);
                    }
                }
                if (heardPlaying != playingNow) {
                    boolean wasPlaying = playingNow;
                    playingNow = heardPlaying;
                    if (wasPlaying && !heardPlaying) {
                        if (PlaybackControl.pausedByUs() && stage.awaitingAnswer()) {
                            // The music stopped because we stopped it, and the
                            // question it was stopped for is still on screen.
                            // Nothing happened that the listener needs telling
                            // about, and a note here would be a note on top of
                            // the question.
                            Log.i(TAG, "the music is being held for our own question");
                        }
 else if (Playback.available) {
                            // The media path above already asked.} else {
                            Log.i(TAG, "the music stopped (heard, not reported)");
                            askAbout(Asked.BY_LISTENER, grid, recent, sectionLabel, sectionBoundarySample);
                        }
                    }
 else if (!wasPlaying && heardPlaying && !announcedReady) {
                        announcedReady = true;
                        announce();
                    }
                }
                // Ask the watcher to look again rather than trusting that it was
                // told: a missed pause callback is a missed question, and the
                // query is cheap.
                if (now - lastRefresh > 1000) {
                    lastRefresh = now;
                    MediaWatcher.refresh();
                    if (Playback.available && !announcedReady) {
                        // The player only becomes readable once MIUI has bound the
                        // listener, which can be a while after the start; say so
                        // when it happens rather than leaving it to be discovered.
                        announcedReady = true;
                        announce();
                    }
                }
                if (!Playback.available && now - lastRebind > 30_000) {
                    lastRebind = now;
                    requestMediaAccess();
                }
                // 授权在名单里、服务却没被绑定：这台 ROM 上重装应用之后就会这样，
                // 之后 song 只能是"未知曲目"、暂停也读不到。这件事必须说出来，
                // 否则它和"功能坏了"一模一样——而它其实只要把开关关掉再打开一次。
                if (!warnedAboutWatcher && !Playback.available && !MediaWatcher.isConnected()
                        && MediaWatcher.hasAccess(this) && now - trackStartedMillis > 15_000
                        && !stage.awaitingAnswer()) {
                    warnedAboutWatcher = true;
                    Log.i(TAG, "notification access is granted but the watcher is not bound: asking the person to toggle it");
                    main.post(() -> {
                        if (card == null) return;
                        long token = stage.show(CardStage.Content.MESSAGE);
                        card.showMessage("读不到播放器（歌名、暂停）",
                                "系统里这项权限显示已允许，但它没有把服务接上——小米重装应用后常见的状态。"
                                        + "把「通知使用权」里的这一项关掉再打开一次就会接上；如果关开也没用（这台机器就是），"
                                        + "重启一次手机一定会接上。歌名和暂停信号都靠它。",
                                "去打开设置", choice -> {
                                    if (card == null) return;
                                    card.collapse();
                                    resizeCard(false);
                                    try {
                                        Intent page = new Intent(
                                                android.provider.Settings.ACTION_NOTIFICATION_LISTENER_DETAIL_SETTINGS);
                                        page.putExtra(
                                                android.provider.Settings.EXTRA_NOTIFICATION_LISTENER_COMPONENT_NAME,
                                                new android.content.ComponentName(this, MediaWatcher.class)
                                                        .flattenToString());
                                        page.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                                        startActivity(page);
                                    } catch (Exception fallback) {
                                        try {
                                            startActivity(new Intent(
                                                    android.provider.Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS)
                                                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                                        } catch (Exception ignored) {
                                        }
                                    }
                                });
                        resizeCard(true);
                        card.setVisibility(View.VISIBLE);
                    });
                }
                if (now - lastReport > 5000) {
                    lastReport = now;
                    // The resting button says what the app is hearing, so that
                    // "is it working?" is answered by looking rather than by
                    // pausing to find out.
                    final boolean heard = heardPlaying;
                    // The tempo on screen is the file's when a file is in charge: the
                    // tracker's own number is a different opinion, and showing it beside
                    // a green marker would say "this came from the file" about a value
                    // that did not.
                    final double bpm = gridFromFile && importedGrid != null
                            ? importedGrid.localBpm(atSeconds) : grid.bpm();
                    final com.fallenangel.listeningai.clip.StyleReading said = reading;
                    final String songName = song == null ? "" : song.label();
                    final double where = atSeconds;
                    // What the model says about the song is worth a line in the
                    // readable log when it changes, and nothing at all when it
                    // does not: a line every five seconds is not a record, it is
                    // a transcript.
                    if (said != null && said.said()) {
                        String sentence = said.sentence();
                        if (!sentence.equals(lastReadableReading) && song != null) {
                            lastReadableReading = sentence;
                            song.say("模型", sentence, where);
                        }
                    }
                    main.post(() -> {
                        if (card == null) return; card.setPillSong(songName); if (!heard) {
                            card.setPillText("没声音"); return;
                        }
                        // The line under the name: whether anything is being
                        // heard, how fast, and what the model makes of it.
                        StringBuilder text = new StringBuilder("在听"); // What is shown is the tempo a person gave when there is
                        // one, and the tracker's own estimate only until then.
                        double shown = anchoredBpm > 0 ? anchoredBpm : bpm;
                        int tempoFrom = -1;
                        int tempoTo = -1;
                        if (shown > 0) {
                            text.append(" · ");
                            tempoFrom = text.length();
                            text.append(Math.round(shown));
                            tempoTo = text.length();
                        }
                        text.append(" · ").append(lastMeter).append("/4");
                        // Which grid this is: a file somebody worked out, or the
                        // tracker's own guess. Worth a word on screen as well as the
                        // colour on the tempo, because "why is the number green" should
                        // be answerable without opening logcat.
                        text.append(gridSourceTag(where));
                        // What the model makes of it, next to the fact that it is
                        // hearing something: the two together are the whole
                        // answer to "is this working, and is it right".
                        if (said != null && said.said()) text.append(" · ").append(said.shortPhrase());
                        final int from = tempoFrom;
                        final int to = tempoTo;
                        final boolean green = gridFromFile;
                        card.setPillText(text.toString(), from, to, green);
                    }
);
                    Log.i(TAG, String.format(java.util.Locale.US, "state song=%s playing=%s source=%s quiet=%s bpm=%.1f held=%.1fs " + "events=%d asked=%d answered=%d said=%d model=%s" + "(%d, %dms, nov=%.2f %d/%d, %s)" + " grid=%d/%s locked=%s half=%s taps=%d phase=%.0f" + " meter=%d/4 anchored=%.1f drift=%.0f/%d fit=%d/%d" + " low=%d%% high=%d%% drum=%.2f lag=%.2f" + " read=%s/%s%s", songName, heardPlaying, Playback.available ? "player" : "audio", grid.quiet(), grid.bpm(), recent.availableSeconds(), observations, questionsAsked, answersGiven, tally.remarkedCount(), onnx == null ? "off" : onnxVersion, modelObservations, onnx == null ? 0 : onnx.lastRunMillis(), lastNovelty, selfAsk.run(), selfAsk.required(), said == null ? "-" : said.sentence(), grid.gridSessionId(), grid.lastResetReason(), grid.locked(), grid.halfTempo(), grid.tapTempoCount(), grid.phaseFrames(), lastMeter, anchoredBpm, grid.driftFrames(), grid.corrections(), grid.alignedOnsets(), grid.alignedOnsets() + grid.misfitOnsets(), percent(grid.lowAligned(), grid.lowHits()), percent(grid.highAligned(), grid.highHits()), grid.drumScore(), grid.lagBeats(), importedGrid == null ? (gridFromFile ? "文件?" : "-") : importedGrid.reading + ":" + importedGrid.shape, importedGrid == null ? "-" : (importedGrid.audioSource.isEmpty() ? "素材未写" : importedGrid.audioSource), importedGrid != null && importedGrid.caution ? "(谨慎)" : ""));
                }
            }
            inference.close();
            sections.close();
        }
 catch (Throwable t) {
            Log.e(TAG, "capture failed", t);
            status = "capture failed: " + t.getClass().getSimpleName();
        }
 finally {
            if (record != null) {
                try {
                    record.stop();
                }
 catch (Exception ignored) {
                }
                record.release();
            }
            if (projection != null) projection.stop();
            writeTheExport(song, true);
            running = false;
        }
    }
    

/**
     * The listener stopped the music. That is the whole signal: it says *this
  * mattered
* and nothing about why, so the seconds before it are handed to
  * the analysis. If the analysis has nothing to offer, nothing is asked -
  * asking anyway is how a person learns that the questions are noise.  */
    

/** Who decided to ask. The difference is whether the music is ours to give back. */
    private enum Asked {
        BY_LISTENER, BY_US
    }
    /**
     * Which bank triggers fit what the analysis just heard.

     * The analyser's candidates are labels about the audio ("鼓型发生变化"); the bank's
     * triggers are labels about the question ("NOVELTY_HIGH"). This is the translation,
     * and it is deliberately a table rather than cleverness: it is the piece a person
     * can read and disagree with. Several triggers are tried in order, so a moment that
     * fits two of them still gets a question instead of silence.
     */
    private java.util.List<String> bankTriggersFor(PauseQuestion analysed) {
        String top = "";
        double best = -1;
        for (PauseQuestion.Candidate candidate : analysed.candidates) {
            if ("none".equals(candidate.id)) continue;
            if (candidate.confidence > best) { best = candidate.confidence; top = candidate.id; }
        }
        switch (top) {
            case "pattern":
            case "fill":
                return java.util.Arrays.asList("INSTRUMENT_CHANGE", "NOVELTY_HIGH");
            case "timbre":
                return java.util.Arrays.asList("INSTRUMENT_CHANGE", "NOVELTY_HIGH");
            case "section":
                return java.util.Arrays.asList("STRUCTURE_CANDIDATE", "NOVELTY_HIGH");
            case "level_up":
            case "level_down":
            case "density_up":
            case "density_down":
            case "harmony":
            case "anticipation":
                return java.util.Arrays.asList("NOVELTY_HIGH", "STRUCTURE_CANDIDATE");
            default:
                return java.util.Arrays.asList("NOVELTY_HIGH", "INSTRUMENT_CHANGE", "STRUCTURE_CANDIDATE");
        }
    }

    private void askAbout(Asked asked, BeatDetector grid, RecentAudio recent, String sectionLabel, long sectionBoundarySample) {
        // Nothing is asked while the app is not supposed to interrupt. Handing the
        // music back is not a formality: a question the app started has already
        // stopped the music, and a guard that returned without giving it back would
        // leave the listener with silence and no card - the worst of both.
        if (!interrupts()) {
            Log.i(TAG, "not asking: this app is set not to interrupt while listening");
            giveBackMusic(asked);
            return;
        }
        // Counting is the one interaction this app asks a listener to perform, and it
        // takes both hands and their attention. A card appearing over it replaces the
        // thing they are using - measured 2026-10-13, "不要打断我对拍子" - so nothing
        // is asked while they are counting, and it says so rather than going quiet.
        if (tapping) {
            Log.i(TAG, "not asking: the listener is counting");
            giveBackMusic(asked);
            return;
        }
        askAbout(asked, grid, recent, sectionLabel, sectionBoundarySample, null);
    }

    /**
     * The same, asking about a window that was captured earlier.
     *
     * A pause of the listener's that arrived while a question was already up has
     * nothing left to listen to by the time that question is finished - the audio
     * buffer has moved on - so it is asked about with the seconds it actually
     * happened in, kept in memory for the minute or so that answers take.
     */
    private void askAbout(Asked asked, BeatDetector grid, RecentAudio recent, String sectionLabel, long sectionBoundarySample, RecentAudio.Window captured) {
        // A pause the app caused is not something the listener noticed.
        if (asked == Asked.BY_LISTENER && PlaybackControl.pausedByUs()) {
            Log.i(TAG, "not asking: the app stopped the music itself");
            giveBackMusic(asked);
            explain("这次暂停是我按的，不算你的注意。");
            return;
        }
        long nowMillis = System.currentTimeMillis();
        long sinceLast = nowMillis - lastQuestionMillis;
        // The gap exists to stop *us* chattering. A pause the listener made is the
        // strongest signal this app ever gets, so it must not be dropped on account of
        // a question we asked a moment earlier - that is the app talking over the one
        // thing it should be listening to, and it read as "0 秒前刚问过一次".
        if (asked == Asked.BY_US && sinceLast < QUESTION_GAP_MILLIS) {
            // Never silent. Every question that is not asked has to say why, or
            // a policy like this is indistinguishable from a feature that
            // stopped working - which is exactly how it looked.
            Log.i(TAG, String.format(java.util.Locale.US, "not asking: the last question was %.0fs ago", sinceLast / 1000.0));
            giveBackMusic(asked);
            explain(String.format(java.util.Locale.US, "%.0f 秒前刚问过一次，不想连着打扰。", sinceLast / 1000.0));
            return;
        }
        if (asked == Asked.BY_LISTENER && stage.awaitingAnswer()) {
            // Their pause landed on a question that is already up. Not stacking a
            // second card on the first is right; dropping the moment is not. This is
            // the listener's own attention, the most valuable thing this app ever
            // hears, and until now it went into the human log and nowhere else - no
            // event, no question, nothing to count afterwards.
            Log.i(TAG, "the listener paused while our own question is on screen: keeping theirs, not stacking");
            holdTheirPause(grid, recent);
            return;
        }
        double duration = Playback.durationSeconds;
        if (duration > 0 && Playback.positionSeconds > duration - 3) {
            Log.i(TAG, "not asking: the track is ending");
            giveBackMusic(asked);
            explain("这首歌快结束了。");
            return;
        }
        // A window captured earlier, when the moment being asked about is not the
        // one happening now: the listener's pause that arrived under a question.
        RecentAudio.Window window = captured != null ? captured : recent.tail(WINDOW_SECONDS);
        if (window == null) {
            Log.i(TAG, String.format(java.util.Locale.US, "paused with only %.1fs held, nothing to ask about", recent.availableSeconds()));
            giveBackMusic(asked);
            explain("刚开听，还没攒够 8 秒。");
            return;
        }
        // A question the app starts comes from the bank; a pause the listener
        // made is still read by the analysis. Two different moments, so two
        // different sources - and the card does not need to know which, because
        // both hand it the same thing.
        // Only now, once every reason not to ask has been handled, does the card say
        // it is thinking. Showing it earlier meant a guard that returned early left
        // "正在看刚才那 8 秒…" on screen with nothing to replace it - a card that
        // says nothing and never goes away is worse than no card.
        long showing = stage.show(CardStage.Content.BUSY);
        main.post(() -> {
            if (card == null || !stage.stillShowing(showing)) return;
            card.showBusy(asked == Asked.BY_US ? "我听到刚才有变化，正在看这 8 秒…" : "正在看刚才那 8 秒…");
            resizeCard(true); card.setVisibility(View.VISIBLE);
        });
        // And a net under it: if anything below fails to resolve the card - an
        // exception, a path nobody thought of - this takes it down and says so.
        main.postDelayed(() -> {
            if (card == null || !stage.stillShowing(showing)) return;
            Log.w(TAG, "the thinking card was still up after " + BUSY_STAGE_TIMEOUT_MILLIS + "ms: closing it");
            giveBackMusic(asked);
            explain("这次没看完，先不打扰。");
        }, BUSY_STAGE_TIMEOUT_MILLIS);
        QuestionTemplate template = null;
        int songBar = 0;
        if (asked == Asked.BY_US) {
            // Read here as well as at the start of listening: the bank is what a
            // question of our own comes from, so there is no state of the app in
            // which not having read it is the right answer.
            QuestionBank bank = questionBank();
            if (questions != null && !bank.all().isEmpty()) {
                songBar = questionBar();
                if (songBar > 0) {
                    template = questions.pick(SELF_ASK_TRIGGER, songBar);
                    if (template == null) {
                        // Not a fault and not silence: the difference between
                        // "the music has not changed" and "I asked a moment ago"
                        // is invisible from the window, and one of them is a bug.
                        Log.i(TAG, "the bank has nothing to ask here: it is less than " + MIN_QUESTION_GAP_BARS + " bars since the last one (bar " + songBar + ")");
                        giveBackMusic(asked);
                        explain("刚问过一次，让它再走几小节。");
                        return;
                    }
                }
            }
        }
        PauseQuestion analysed = ListeningAnalyzer.analyse(window.pcm, 0, window.pcm.length, window.startSample, gridFor(grid), sectionLabel, sectionBoundarySample);
        // A pause the listener made is the same kind of moment as one we chose, so it
        // gets the same bank of questions - with the analysis deciding *which* trigger
        // applies, not what to ask. Without this the listener's own pauses always came
        // back as the analyser's own card, and on this repertoire that meant the drum
        // candidate every single time: eight questions in the bank, one of them asked.
        // Whose pause is it. A pause the listener made is the one moment the
        // analysis cannot read and a model can: the pause is theirs, the music is
        // already stopped, so waiting for a model costs them nothing they have not
        // already paid. A question we start is different - the music is being held
        // down, and half a minute of held music is worse than a plainer question -
        // so that one stays with the bank, and the bank answers either one when no
        // model is configured at all.
        if (template == null && !modelReadsPauses() && questions != null) {
            songBar = questionBar();
            if (songBar > 0) {
                for (String trigger : bankTriggersFor(analysed)) {
                    template = questions.pick(trigger, songBar);
                    if (template != null) {
                        Log.i(TAG, "the pause reads as " + trigger + " -> asking " + template.id);
                        break;
                    }
                }
            }
        }
        PauseQuestion question = template == null ? analysed : new PauseQuestion(template.text, analysed.targetSample, analysed.sectionLabel, analysed.bar, analysed.beat, analysed.subdivision, bankOptions(template));
        if (template == null && question.candidates.size() < 2) {
            Log.i(TAG, "paused, and the analysis has no explanation to offer");
            giveBackMusic(asked);
            // Not the same thing said twice: when the listener paused, "nothing
            // changed" is an answer. When the app stopped the music, it already
            // told them something changed - so it has to admit it cannot say what.
            explain(asked == Asked.BY_US ? "我听到这里有些变化，但还说不出是哪一种。" : "这几秒很平稳，我没有能说出口的变化。");
            return;
        }
        lastQuestionMillis = nowMillis;
        questionsAsked++;
        tally.asked();
        questionShownAtMillis = nowMillis;
        // Marked here rather than when the card appears: the music is already
        // held (or the listener has already stopped it), and the loop must not
        // treat our own pause as a pause of theirs and talk over the question.
        stage.show(CardStage.Content.BUSY);
        // The two triggers are not the same data: one is a moment the listener
        // stopped on, the other a moment the app decided was worth stopping for,
        // and a record that cannot tell them apart cannot answer either question.
        String version = template != null ? "bank:" + template.id : asked == Asked.BY_US ? "local:novelty" : PauseQuestion.VERSION;
        ListeningEvent proposed = ListeningEvent.proposed("q" + (++questionCount) + "-" + Long.toHexString(nowMillis), question, grid.audioTime(), grid.locked() ? 0.9 : 0.4, nowMillis, version);
        double at = whereInSong(grid);
        if (song != null) {
            song.event(proposed.toJson(), at);
            if (template != null) song.event(bankLine("ASKED", template, songBar, at, null), at);
        }
        StringBuilder summary = new StringBuilder();
        if (template == null) {
            for (PauseQuestion.Candidate candidate : question.candidates) {
                summary.append(candidate.label).append(' ') .append(String.format(java.util.Locale.US, "%.2f", candidate.confidence)) .append(" | ");
            }
        }
 else {
            summary.append('[').append(template.id).append("] ");
        }
        lastQuestion = question.question + " " + summary;
        holdingMusic = asked == Asked.BY_US;
        // When the listener stopped on the same moment the app had just heard
        // change in, that is the most interesting thing about this question and
        // the one thing nobody was being told.
        String note = asked == Asked.BY_LISTENER ? CollisionNote.of(lastNoticedMillis, nowMillis) : "";
        if (!note.isEmpty()) {
            Log.i(TAG, "the listener stopped where the model had just heard a change");
        }
        if (song != null) {
            song.say("我", template == null ? "停下来问：" + question.question + summary : "问：" + question.question + "（" + template.id + "）", at);
        }
        Log.i(TAG, String.format(java.util.Locale.US, "asking about %.2fs (bar %d beat %d %s, section %s): %s", question.targetSeconds(), question.bar + 1, question.beat, question.subdivisionName(), question.sectionLabel, summary));
        if (template != null) {
            showQuestion(asked, note, question, proposed, template, songBar, at);
        }
 else {
            askTheModel(asked, note, question, proposed, window.pcm, sectionLabel);
        }
    }
    

/**
     * Start the music again, when it was the app that stopped it.
  *
  * A question the app started is a question the app owes an answer to: the
  * listener did not ask to be stopped, so every way out of asking - including
  * deciding not to - has to hand the music back.  */
    private void giveBackMusic(Asked asked) {
        if (asked != Asked.BY_US) return;
        holdingMusic = false;
        if (song != null) song.say("我", "继续播放", atSeconds);
        PlaybackControl.resume(this);
    }
    

    /**
     * Whether a model is configured to read the listener's own pauses.
     *
     * Read at the moment of asking rather than at start-up, because the endpoint
     * is typed into the settings screen while the app is running, and a value
     * cached at listening time would make the model appear to do nothing until
     * the next restart.
     */
    private boolean modelReadsPauses() {
        android.content.SharedPreferences prefs = getSharedPreferences(PREFS, MODE_PRIVATE);
        return ExplanationClient.configured(
                prefs.getString(PREF_ENDPOINT, ""), prefs.getString(PREF_KEY, ""));
    }

    /**
     * Hand the clip to the reasoning model, or fall straight through to what the
  * local rules found.
  *
  * The local candidates are already good enough to ask with - they are what
  * the app has been built on - so the model is an improvement, not a
  * dependency: no key, no network, or a bad answer all end in the same place,
  * which is the question the analysis could already ask.  */
    private void askTheModel(Asked asked, String note, PauseQuestion local, ListeningEvent proposed, short[] clip, String sectionLabel) {
        // Whichever song this question belongs to; the cloud can take seconds to
        // answer and the listener can change tracks in the meantime.
        final com.fallenangel.listeningai.listening.SongLog about = song;
        android.content.SharedPreferences prefs = getSharedPreferences(PREFS, MODE_PRIVATE);
        String endpoint = prefs.getString(PREF_ENDPOINT, "");
        String key = prefs.getString(PREF_KEY, "");
        if (!ExplanationClient.configured(endpoint, key)) {
            showQuestion(asked, note, local, proposed);
            return;
        }
        if (System.currentTimeMillis() < modelSilentUntilMillis) {
            // Asked a moment ago and could not be reached. Asking again costs the
            // connection timeout again, and the answer would be the same.
            Log.i(TAG, "the model was unreachable a moment ago; asking with the rules");
            showQuestion(asked, note, local, proposed);
            return;
        }
        String model = prefs.getString(PREF_MODEL, ExplanationClient.DEFAULT_MODEL);
        long thinking = stage.show(CardStage.Content.BUSY);
        main.post(() -> {
            if (card != null && stage.stillShowing(thinking)) {
                // Not "云端" any more: the model this reaches may be on this desk,
                // and a card that says where the answer is coming from when it is
                // not is the kind of detail that makes the whole thing look broken.
                card.showBusy("正在问模型…"); resizeCard(true); card.setVisibility(View.VISIBLE);
            }
        }
);
        cloud.execute(() -> {
            List<PauseQuestion.Candidate> candidates = null; String why = null; try {
                candidates = new ExplanationClient(endpoint, key, model, ExplanationClient::post)
                        .explain(clip, 0, clip.length, SAMPLE_RATE, facts(local, sectionLabel),
                                vocabulary().instruction());
            }
 catch (Exception e) {
                why = e.getClass().getSimpleName() + ": " + e.getMessage();
            }
            if (candidates == null) {
                // A model that could not be reached is not a model that answered
                // badly: the first is worth waiting out, the second is worth asking
                // again. Only the first sets the silence.
                if (why != null && why.startsWith("IOException")) {
                    modelSilentUntilMillis = System.currentTimeMillis() + MODEL_SILENT_MILLIS;
                    Log.i(TAG, "the model could not be reached; not asking it again for "
                            + (MODEL_SILENT_MILLIS / 1000) + "s");
                }
                // Said on the card, not only in the log. An endpoint that is set
                // and a model that answers nothing look identical from the
                // listener's side, and the difference is theirs to act on - this
                // exact failure (cleartext refused by the platform, twenty
                // seconds of nothing, then the rules) already cost one evening
                // of "it must be the model".
                Log.i(TAG, "the model did not answer (" + why + "), asking with the rules");
                String missed = "模型没答：" + (why == null ? "没有回答" : why);
                showQuestion(asked, note == null || note.isEmpty() ? missed : note + " ｜ " + missed,
                        local, proposed);
                return;
            }
            if (!stage.stillShowing(thinking)) {
                // Something else took the window while the model was thinking:
                // its answer is about a moment that is no longer what the
                // listener is looking at, and the music must not stay held for a
                // question nobody is going to see.
                Log.i(TAG, "the cloud answered after the window had moved on"); giveBackMusic(asked); return;
            }
            // Cleaned before it reaches a person: labels the vocabulary does not
            // have are dropped, and three phrasings of one fact become one button.
            // The record keeps the raw answer - the cleaning is about what the
            // listener is asked, not about what was heard.
            List<PauseQuestion.Candidate> offered =
                    CandidateFilter.clean(candidates, vocabulary(), answerMemory());
            Log.i(TAG, "the model proposed " + (candidates.size() - 1) + " explanations, "
                    + (offered.size() - 1) + " of them offered");
            if (offered.size() < 2) {
                // Everything it said was outside the vocabulary or a repeat of
                // something else. One button is not a question.
                Log.i(TAG, "nothing usable survived the vocabulary and the dedupe; asking with the rules");
                showQuestion(asked, note, local, proposed);
                return;
            }
            modelSilentUntilMillis = 0;   // it answered, so it is there
            PauseQuestion fromModel = ListeningAnalyzer.withCandidates(local, offered); // The rules' proposal stays in the record and the model's is added
            // beside it: which of them the person was answering is part of the
            // data, and a record that keeps only the last one cannot say.
            ListeningEvent byModel = ListeningEvent.proposed( proposed.id + "-m", fromModel, proposed.interactionSeconds, proposed.alignmentConfidence, System.currentTimeMillis(), "cloud:" + model); if (about != null) about.event(byModel.toJson(), atSeconds); showQuestion(asked, note, fromModel, byModel);
        }
);
    }
    

/** What the model is told was measured, in the terms the analysis has. */
    private String facts(PauseQuestion local, String sectionLabel) {
        StringBuilder out = new StringBuilder();
        out.append(String.format(java.util.Locale.US, "位置：歌曲时间 %.2f 秒，小节 %d，第 %d 拍，细分 %s，段落 %s。%n", local.targetSeconds(), local.bar + 1, local.beat, local.subdivisionName(), sectionLabel == null || sectionLabel.isEmpty() ? "未知" : sectionLabel));
        // The model's own reading, as words rather than numbers: what it makes
        // of the music is what an explanation has to be built on, and a column
        // of 50 logits is not something to reason about aloud.
        com.fallenangel.listeningai.clip.StyleReading said = reading;
        if (said != null && said.said()) {
            out.append(String.format(java.util.Locale.US, "本地音乐模型听到：%s（它自己的把握 %.2f，与它听到的最近几秒相比：%s）。%n", said.claim(), said.confidence, said.change()));
        }
        out.append("本地分析测到的事实：").append('\n');
        for (PauseQuestion.Candidate candidate : local.candidates) {
            if ("none".equals(candidate.id)) continue;
            out.append(String.format(java.util.Locale.US, "- %s（%s，把握 %.2f）%n", candidate.label, candidate.evidence, candidate.confidence));
        }
        out.append("这些只是测量，不一定是这个人注意到的原因。");
        out.append("请针对这段音频给出你自己的候选解释。");
        return out.toString();
    }
    private void showQuestion(Asked asked, String note, PauseQuestion question, ListeningEvent proposed) {
        showQuestion(asked, note, question, proposed, null, 0, 0);
    }
    

/**
     * The same, for a question from the bank.
  * A bank question carries its own id, and the id is what makes the answers
  * countable: two differently-worded questions do not have the same answers.
  * So the ask and the answer each get a line of their own next to the
  * ordinary event, and an answer nobody gave is written down as well - a
  * question that was ignored is one of the four things this round is read by.  */
    private void showQuestion(Asked asked, String note, PauseQuestion question, ListeningEvent proposed, QuestionTemplate template, int songBar, double songSeconds) {
        stage.show(CardStage.Content.QUESTION);
        // The song this question is about, not whichever song is playing when the
        // answer arrives: a question asked at the end of a track can be answered
        // after the next one has started, and an answer filed under the wrong
        // song is worse than no answer.
        final com.fallenangel.listeningai.listening.SongLog about = song;
        main.post(() -> {
            if (card == null) return; card.show(question, note, choice -> {
                answersGiven++;
                // Counted only into the song it was asked about: a question asked at
                // the end of a track can be answered after the next one started, and
                // an answer that lands in the wrong song's tally is worse than an
                // answer that is not counted.
                if (about == song) {
                    tally.answered((System.currentTimeMillis() - questionShownAtMillis) / 1000.0);
                }
                recordWhereThePressLanded(about, "question_answer", System.currentTimeMillis()); if (about != null) {
                    about.event(proposed.answered(choice, System.currentTimeMillis()).toJson(), atSeconds); about.say("你", "选了：" + choice.label, atSeconds); if (template != null) {
                        about.event(bankLine("ANSWERED", template, songBar, songSeconds, choice.label), atSeconds);
                    }
                }
                card.collapse(); // The card keeps the question it was shown until it is told
                // otherwise, and the button asks it. Leaving it there is what
                // made the button reopen a question that had been answered.
                card.clearQuestion(); stage.answered(); resizeCard(false); Log.i(TAG, "answered: " + choice.label + " (" + choice.confidence + ")"); giveBackMusic(asked);
            }
); resizeCard(true); card.setVisibility(View.VISIBLE);
        }
);
        if (asked == Asked.BY_US) {
            main.postDelayed(() -> {
                if (card == null || !stage.awaitingAnswer() || !holdingMusic) return; Log.i(TAG, "no answer to our own question; giving the music back" + (template == null ? "" : " (" + template.id + ")")); // Nothing is built on a question nobody answered, and it is not
                // asked again next time either - the listener has already said,
                // by not saying anything, that they were not answering.
                if (about == song) tally.ignored();
                if (template != null) {
                    if (questions != null) questions.unanswered(template); if (about != null) {
                        about.event(bankLine("IGNORED", template, songBar, songSeconds, null), atSeconds);
                    }
                }
                stage.answered(); giveBackMusic(Asked.BY_US); card.collapse(); resizeCard(false);
            }
, SELF_ASK_PATIENCE_MILLIS);
        }
    }
    

/**
     * Where this song's grid came from, written into the record.
     *
     * The grid is the coordinate system every other line refers to, so which one was
     * used is not a detail: a bar number means nothing without it, and a later
     * correction has to be able to say what it corrected.
     */
    private org.json.JSONObject gridSourceLine(
            com.fallenangel.listeningai.listening.WorkbenchGrid grid) {
        org.json.JSONObject line = new org.json.JSONObject();
        try {
            line.put("eventType", "GRID_SOURCE");
            line.put("state", "AUTO_FLAGGED");
            line.put("source", "workbench:" + grid.reading);
            line.put("generatedBy", grid.generatedBy);
            line.put("bpm", grid.bpm);
            line.put("beatsPerBar", grid.beatsPerBar);
            line.put("beats", grid.beatCount());
            line.put("coveredUntil", grid.beat(grid.beatCount() - 1));
            line.put("shape", grid.shape);
            line.put("audioSource", grid.audioSource);
            line.put("caution", grid.caution);
            org.json.JSONArray reasons = new org.json.JSONArray();
            for (String note : grid.notes) reasons.put(note);
            line.put("readingNotes", reasons);
            line.put("title", grid.title);
        } catch (org.json.JSONException ignored) {
            // A JSONObject is a map; only out-of-memory throws here.
        }
        return line;
    }

    /**
     * The word about where this song's grid came from, for the resting button.
     *
     * "谨慎" is on screen as well as in the file: a grid built from a reading the
     * validator doubted, or from the drums rather than the mix, is still the best
     * thing here, and the one thing that must not happen is it looking like a clean
     * one. It is asked per moment, because a reading the validator only doubted over
     * one stretch is doubted over one stretch - telling the listener "this whole song
     * is shaky" would be its own kind of lie.
     */
    private String gridSourceTag(double seconds) {
        if (gridFromFile) {
            return " · 网格:" + (gridSource.isEmpty() ? "文件" : gridSource)
                    + (importedGrid != null && importedGrid.shakyAt(seconds) ? "(谨慎)" : "");
        }
        return " · 网格:自追";
    }

    /**
     * Whether the app may say anything at all right now.
     *
     * Read at the moment of speaking rather than cached, because it is a setting the
     * listener can change between two songs and a value read at start-up would make
     * the switch look broken until the next restart.
     */
    private boolean interrupts() {
        return getSharedPreferences(PREFS, MODE_PRIVATE).getBoolean(PREF_INTERRUPTS, false);
    }

    /**
     * Whether the music stopped as part of a jump rather than because they stopped it.
     */
    private boolean justSeeked() {
        return System.currentTimeMillis() - lastSeekMillis < SEEK_QUIET_MILLIS;
    }

    /**
     * Keep the moment the listener stopped on, when we were already asking.
     *
     * Two things, because they fail differently. The moment is written into the
     * record at once, so it survives whatever happens next - a pause that was
     * noticed and then lost is worse than one that was never noticed, because it
     * looks like nothing happened. And the eight seconds of audio are kept in
     * memory, because a question about it can still be asked once the question
     * that is up has been dealt with, and by then the buffer has moved on.
     */
    private void holdTheirPause(BeatDetector grid, RecentAudio recent) {
        RecentAudio.Window window = recent == null ? null : recent.tail(WINDOW_SECONDS);
        double at = whereInSong(grid);
        if (song != null) {
            song.say("听", "在我提问的同时按了暂停（记下了，等这张问完再问这个）", at);
            song.event(collisionLine(grid, at), at);
        }
        if (window == null) {
            Log.i(TAG, "their pause came with no audio behind it; recorded, not asked about");
            return;
        }
        heldPauseWindow = window;
        heldPauseAtMillis = System.currentTimeMillis();
        heldPauseSeconds = at;
        Log.i(TAG, String.format(java.util.Locale.US,
                "held their pause at %.2fs (%d seconds of audio kept)", at, window.pcm.length / SAMPLE_RATE));
    }

    /** What the collided pause looked like, written down where it cannot be lost. */
    private org.json.JSONObject collisionLine(BeatDetector grid, double at) {
        org.json.JSONObject line = new org.json.JSONObject();
        try {
            line.put("eventType", "PAUSE_DURING_QUESTION");
            line.put("state", "USER_CONFIRMED");
            line.put("createdAt", System.currentTimeMillis());
            // The time is the anchor, not a bar number: this is the raw layer, and
            // the bar a moment belongs to is derived from the grid afterwards - the
            // same rule the chord stream follows, and the reason the grid cannot
            // rewrite history by changing.
            line.put("bpm", grid == null ? 0 : grid.bpm());
            line.put("locked", grid != null && grid.locked());
            line.put("aiVersion", "collision-v1");
            line.put("note", "the listener stopped while our own question was on screen");
        } catch (org.json.JSONException ignored) {
            // A JSONObject is a map; only out-of-memory throws here.
        }
        return line;
    }

    /**
     * Why there is no question this time, in the floating layer.
  *
  * Also hides itself after a few seconds: a note about a moment that has
  * passed has nothing to sit there for, and the window is over somebody
  * else's music.  */
    private void explain(String reason) {
        long token = stage.show(CardStage.Content.MESSAGE);
        if (song != null) song.say("我", "这次不提问：" + reason, atSeconds);
        main.post(() -> {
            if (card == null) return; card.showMessage("这次不提问。", reason, "知道了", choice -> {
                card.collapse(); resizeCard(false);
            }
); resizeCard(true); card.setVisibility(View.VISIBLE);
        }
);
        main.postDelayed(() -> {
            // Only its own note: a style card shown in the meantime has a null
            // question too, and used to be tidied away by this.
            if (card == null || !stage.stillShowing(token)) return; if (card.question() == null) {
                card.collapse(); resizeCard(false);
            }
        }
, 7000);
    }
    

/**
     * What the model is hearing, shown so it can be agreed with or denied.
  *
  * The listener taps the button whenever they like - nothing is being
  * interrupted and no question is being answered - and what they get is the
  * app's current opinion of the music, in words, with two buttons. Both
  * answers are recorded: a tag the person says is wrong is the more
  * interesting half of this data, because it is where a model trained on
  * somebody else's library and a person listening to their own music differ.  */
    private void showWhatItHears(com.fallenangel.listeningai.clip.StyleReading current) {
        stage.show(CardStage.Content.STYLE);
        styleProposalId = com.fallenangel.listeningai.listening.StyleFeedback.newId( System.currentTimeMillis());
        final String id = styleProposalId;
        final com.fallenangel.listeningai.listening.SongLog here = song;
        if (here != null) {
            here.event(com.fallenangel.listeningai.listening.StyleFeedback.proposed( id, onnxVersion, whereInSong(null), current, System.currentTimeMillis()), atSeconds);
            here.say("模型", current.sentence(), atSeconds);
        }
        main.post(() -> {
            if (card == null) return; card.showStyle("现在听起来：" + current.claim(), String.format(java.util.Locale.US, "把握 %.2f · %s", current.confidence, current.change()), this::beginTapTempo, this::nudgeBarLine, () -> {
                        card.collapse(); resizeCard(false);
            }
); resizeCard(true); card.setVisibility(View.VISIBLE);
        }
);
    }
    // ------------------------------------------------------------ tap tempo
    

/**
     * The listener is going to tap the beat.
  *
  * This is the one place where the grid is told something by a person who
  * could hear the music. Everything else about the grid is inferred, and the
  * inference has been wrong in ways only a listener could see - the same song
  * reporting 134 and then 74 - so the tap is both a correction and the
  * reference the automatic tracker can later be measured against.  */
    private void beginTapTempo() {
        anchor = new com.fallenangel.listeningai.listening.BeatAnchor();
        tapping = true;
        tapLines.clear();
        if (song != null) song.say("我", "开始对拍：小节头按第一个按钮，其余正拍按第二个", atSeconds);
        main.post(() -> {
            if (card == null) return; card.showTapTempo("跟着音乐按两个按钮", "每小节第 1 拍按「小节头」，其余每拍按「其余正拍」；数两小节就够。", new QuestionCard.TapListener() {
                        @Override public void onDownbeat() {
                            onePress(com.fallenangel.listeningai.listening.BeatAnchor.Kind .DOWNBEAT);
                }
                        @Override public void onBeat() {
                            onePress(com.fallenangel.listeningai.listening.BeatAnchor.Kind.BEAT);
                }
                        @Override public void onFinish() {
                            finishTapTempo();
                }
            }
); resizeCard(true); card.setVisibility(View.VISIBLE);
        }
);
    }
    

/** One press, written down as it happened and fed to the count. */
    private void onePress(com.fallenangel.listeningai.listening.BeatAnchor.Kind kind) {
        if (!tapping) return;
        BeatDetector beat = grid;
        // Tapping along with nothing playing measures the clock, not the music:
        // the audio time stops advancing when the capture goes quiet, and the
        // taps then look impossibly fast - measured, five taps in a silent
        // stretch produced 315 BPM.
        if (beat == null || !Playback.playing || beat.quiet()) {
            Log.i(TAG, "tap ignored: nothing is playing to tap along with");
            if (song != null) song.say("我", "对拍：现在没有音乐在播，这一下没算", atSeconds);
            return;
        }
        long audio = beat == null ? -1 : Math.round(beat.audioTime() * 1000);
        long client = System.currentTimeMillis();
        anchor.tap(kind, audio, client);
        int index = anchor.pressCount() - 1;
        if (song != null) {
            // The raw press, with which button it was, not the numbers it implies:
            // a better reading later should be able to re-read these rather than
            // trust what this moment worked out.
            org.json.JSONObject line = new org.json.JSONObject();
            try {
                line.put("kind", "ANCHOR_TAP");
                line.put("tapIndex", index);
                line.put("button", kind.name());
                line.put("audioTimeMs", audio);
                line.put("clientTime", client);
                line.put("anchorType", "bar_head_and_beats");
                line.put("sessionId", beat == null ? -1 : beat.gridSessionId());
                line.put("songId", song.label());
            }
 catch (Exception ignored) {
            }
            song.event(line, atSeconds);
            tapLines.add(line);
        }
        Log.i(TAG, String.format(java.util.Locale.US, "tap %d at %.0fms (bpm %.1f)", anchor.pressCount(), (double) audio, anchor.beatBpm()));
        final int count = anchor.pressCount();
        final double beatBpm = anchor.beatBpm();
        final double beatMillis = beatBpm > 0 ? 60_000.0 / beatBpm : 0;
        final int meter = anchor.meter();
        main.post(() -> {
            if (card != null) card.setTapProgress(count, beatMillis, beatBpm, meter);
        }
);
        // Deliberately not finishing when the taps first agree: four taps agree
        // by accident often enough that ending there would lock a wrong tempo and
        // call it the listener's answer. They decide when they are done.
    }
    

/**
     * What the grid is counting in, as a person writes it.
  *
  * The detector counts a fixed number of beats per bar and has no opinion
  * about the music's meter, so this is the assumption the app is running on
  * rather than something it worked out. The beat is a quarter by convention in
  * this grid, which makes the denominator 4.  */
    private String meter() {
        BeatDetector beat = grid;
        if (beat == null) return "";
        // What the last calibration established, or 4 until somebody says
        // otherwise: the detector itself has no opinion about the meter.
        return lastMeter + "/4";
    }
    

/** Fits as a whole number of percent, or -1 when nothing has been heard. */
    private static long percent(long aligned, long total) {
        return total <= 0 ? -1 : Math.round(100.0 * aligned / total);
    }
    

/**
     * Where a press lands in the music, and how far it is from the beat it lands on.
  * The anchor's real job is this: turning "the finger pressed at 06:12.418" into
  * "beat 3 of bar 27", so that answers in different songs and different weeks can
  * be compared. The exact millisecond is worthless for that and worth keeping
  * anyway, so both are written: the raw audio time, the snapped position, and
  * the distance between them in beats. The distance is what a later analysis
  * needs to decide whether to trust the snap - at a hundred and twenty beats a
  * minute a hand is good to about a third of a beat, which is inside a beat and
  * well outside a sixteenth.  */
    private void recordWhereThePressLanded(com.fallenangel.listeningai.listening.SongLog where, String what, long clientMillis) {
        BeatDetector beat = grid;
        if (where == null || beat == null || !beat.locked()) return;
        int perBar = beat.positionsPerBar();
        if (perBar <= 0) return;
        int perBeat = Math.max(1, perBar / Math.max(1, lastMeter));
        double audioSeconds = beat.audioTime();
        int position = beat.positionAt(audioSeconds);
        if (position == Integer.MIN_VALUE) return;
        int inBar = Math.floorMod(position, perBar);
        double intoBeat = (inBar % perBeat) / (double) perBeat;
        org.json.JSONObject line = new org.json.JSONObject();
        try {
            line.put("kind", "PRESS_SNAP");
            line.put("what", what);
            line.put("clientTime", clientMillis);
            line.put("audioTimeMs", Math.round(audioSeconds * 1000));
            line.put("position", position);
            line.put("bar", Math.floorDiv(position, perBar) + 1);
            line.put("songBar", songBarNumber());
            line.put("beat", inBar / perBeat + 1);
            line.put("intoBeat", Math.round(intoBeat * 1000) / 1000.0);
            line.put("anchoredBpm", Math.round(anchoredBpm * 10) / 10.0);
            line.put("meter", lastMeter);
        }
 catch (Exception ignored) {
        }
        where.event(line, atSeconds);
    }
    

/**
     * "How many beats are in the bar you just tapped?"  /*
*
  * "The flash is not on the one." Move the bar line by a beat.
  * Tempo tracking cannot tell a bar from its second beat: every phase it finds
  * is equally consistent with the onsets, which is why a grid can be perfectly
  * in time and still put beat one on the wrong beat. A person listening can
  * hear which one it is, and one press moves the line by one beat - with the
  * flash as the answer, so they can press again until it lands.  */
    private void nudgeBarLine() {
        // Asked rather than done. Whether the beat being counted is the beat a
        // person is following is the one thing the tracker cannot hear: every
        // phase it finds fits the onsets equally well. And the answer is not a
        // fact about the past - it is what to do now, which is also how a person
        // actually behaves when they lose the beat: they find a new one.
        main.post(() -> {
            if (card == null) return; card.ask("你还在跟这个拍子吗？", "闪的位置就是它数的小节头", new String[]{
"跟得上（挪一拍）", "这里是新的一小节", "我丢了，重来吧", "这里变三拍", "这里变四拍"
            }
, new Runnable[]{
() -> barAnswer("shift_one"), () -> barAnswer("bar_starts"), () -> barAnswer("lost_the_beat"), () -> barAnswer("meter_3"), () -> barAnswer("meter_4")
            }
); resizeCard(true); card.setVisibility(View.VISIBLE);
        }
);
    }
    

/**
     * What the listener said about the bar, and what it does to the grid.
  * All three are corrections of the present: one moves the bar line a beat, one
  * says a bar starts here, and one says the person has lost the beat and wants a
  * new one found - which is the same thing a seek does, and the tracker already
  * knows how to do it (keep the tempo, find the phase again).  */
    private void barAnswer(String choice) {
        BeatDetector beat = grid;
        if (beat == null) return;
        if ("meter_3".equals(choice) || "meter_4".equals(choice)) {
            // A meter change is an event at a time, not a property of the song:
            // from this moment the bar is three beats (or four again), and the
            // record says where. That is exactly what a song that changes meter
            // partway through needs - and the only thing a person can supply that
            // no tracker can.
            int meter = "meter_3".equals(choice) ? 3 : 4;
            beat.setMeter(meter);
            lastMeter = meter;
            if (song != null) {
                song.say("我", String.format(java.util.Locale.US, "变拍子：从 %s 起按 %d 拍", com.fallenangel.listeningai.listening.SongLog.clock(atSeconds), meter), atSeconds);
            }
        }
 else if ("lost_the_beat".equals(choice)) {
            beat.onSeek();
        }
 else {
            if (!beat.locked()) {
                if (song != null) song.say("我", "网格还没锁上，先不动小节线", atSeconds);
                main.post(() -> {
                    if (card != null) {
                        card.collapse(); resizeCard(false);
                    }
                }
);
                return;
            }
            beat.shiftBarLine(1);
        }
        if (song != null) {
            org.json.JSONObject line = new org.json.JSONObject();
            try {
                line.put("kind", "BAR_ANCHOR");
                line.put("choice", choice);
                line.put("audioTimeMs", Math.round(beat.audioTime() * 1000));
                line.put("bpm", Math.round(beat.bpm() * 10) / 10.0);
                line.put("locked", beat.locked());
                line.put("sessionId", beat.gridSessionId());
                line.put("songId", song.label());
                line.put("meter", lastMeter);
            }
 catch (Exception ignored) {
            }
            song.event(line, atSeconds);
            song.say("你", "lost_the_beat".equals(choice) ? "丢了拍子，重新找" : ("bar_starts".equals(choice) ? "这里是新的一小节" : ("shift_one".equals(choice) ? "跟得上，挪一拍" : "变拍子")), atSeconds);
        }
        Log.i(TAG, "bar anchor from the listener: " + choice);
        main.post(() -> {
            if (card != null) {
                card.collapse(); resizeCard(false);
            }
        }
);
    }
    

/**
     * A flash on the first beat of each bar.
  * Driven by the grid's own position rather than by counting beats in the
  * capture loop: the loop runs every few milliseconds and the grid already
  * knows which position it is on, so asking it cannot drift apart from it. The
  * bar number is what is watched, so a jump in position (a seek, a re-lock)
  * cannot produce a burst of flashes catching up.  */
    /**
     * The bar line, if this tick is one.
     *
     * Returns whether a new bar started here, which the flash uses. The chord box
     * quantises to the beat rather than to this - see {@link #onBeatLine}.
     */
    private boolean flashOnBarLine(BeatDetector beat) {
        // Not while the listener is tapping: the flash is a metronome, and a
        // metronome that argues with the person counting is worse than none.
        if (tapping) return false;
        // A grid from a file is the one that flashes: its bar lines are the ones the
        // music actually has, and the tracker's own are a second opinion nobody asked
        // for once a file is in charge.
        if (gridFromFile && importedGrid != null) {
            // The bar the file names, not position / beatsPerBar: a reading's first bar
            // line is not always its first beat (BeatThis on 打上花火 reports its first
            // bar line 0.64s after its first beat), and dividing by the meter would then
            // flash for the whole song one beat out.
            double where = whereInSong(beat);
            long bar = importedGrid.barAt(where);
            if (bar < 0) return false;
            if (bar == lastFlashedBar) return false;
            lastFlashedBar = bar;
            // How late the flash for this bar line is, and what the capture's own clock
            // made of the same moment: "the flash is off" is either the file's bar line
            // or the playhead, and only the two together say which.
            if (fileFlashes < 8) {
                fileFlashes++;
                Log.i(TAG, String.format(java.util.Locale.US,
                        "flash(file) %d bar=%d line=%.3f where=%.3f late=%.0fms audioClock=%.3f",
                        fileFlashes, bar, importedGrid.barLineSeconds(bar), where,
                        (where - importedGrid.barLineSeconds(bar)) * 1000.0,
                        beat == null ? -1 : Math.max(0, beat.audioTime() - songStartAudioTime)));
            }
            main.post(() -> {
                if (card != null) card.flash();
            }
);
            return true;
        }
        if (beat == null || !beat.locked()) return false;
        int perBar = beat.positionsPerBar();
        if (perBar <= 0) return false;
        int position = beat.positionAt(beat.audioTime());
        if (position == Integer.MIN_VALUE) return false;
        long bar = Math.floorDiv(position, perBar);
        if (bar == lastFlashedBar) return false;
        lastFlashedBar = bar;
        boolean newBar = true;
        // Where it actually lands, for the first few bars after an anchor: the
        // complaint "the flash is not on beat one" is either an offset of a
        // fraction of a beat (the listener's hand, which is expected) or a whole
        // one (the bar is counted from somewhere else, which is a bug), and only
        // the position says which.
        if (flashesSinceAnchor < 4) {
            flashesSinceAnchor++;
            Log.i(TAG, String.format(java.util.Locale.US, "flash %d after anchor: bar=%d position=%d (position %% perBar = %d)", flashesSinceAnchor, bar, position, Math.floorMod(position, perBar)));
        }
        main.post(() -> {
            if (card != null) card.flash();
        }
);
        return newBar;
    }

    /**
     * Whether a new beat started here, which is the moment the chord box may change.
     *
     * Beats rather than bar lines, and that is a correction: chord changes happen on
     * the beat, so quantising to the bar meant a change noticed just after a bar line
     * waited for the *next* one - "反应慢一小节" - while the beat is the unit a chord
     * actually moves in and the wait is at most half a second.
     */
    private boolean onBeatLine(BeatDetector beat) {
        if (gridFromFile && importedGrid != null) {
            int position = importedGrid.positionAt(whereInSong(beat));
            if (position < 0) return false;
            if (position == lastBeatIndex) return false;
            lastBeatIndex = position;
            return true;
        }
        if (beat == null || !beat.locked()) return false;
        int perBar = beat.positionsPerBar();
        int perBeat = Math.max(1, perBar / Math.max(1, beat.beatsPerBar()));
        int position = beat.positionAt(beat.audioTime());
        if (position == Integer.MIN_VALUE) return false;
        long index = Math.floorDiv(position, perBeat);
        if (index == lastBeatIndex) return false;
        lastBeatIndex = index;
        return true;
    }

    /** The last beat a change was allowed on, so each beat counts once. */
    private long lastBeatIndex = Long.MIN_VALUE;
    

/** The last bar a flash was made for, so each bar flashes once. */
    private long lastFlashedBar = Long.MIN_VALUE;
    /** How many flashes off a file's bar lines have been logged since the file took over. */
    private int fileFlashes;
    

/** How many flashes have been logged since the last anchor. */
    private int flashesSinceAnchor = 99;
    

/**
     * Put the listener's anchor back when the grid loses it.
  * The grid drops its lock when the music goes quiet - a pause, the gap between
  * tracks - and comes back with a phase it found by itself. A phase is a
  * circular quantity, so a self-found one can sit a whole beat away from the one
  * the listener marked, which is how a bar line that was right at the start of a
  * song is a beat out later on.
  * Which ending it was matters. Quiet music leaves the beat exactly where it
  * was, so the anchor is still true and is put back. A seek or a new track means
  * the music moved, and a phase from before it says nothing about after it, so
  * the anchor is dropped instead of being re-applied to a different moment.  */
    private void keepTheAnchor(BeatDetector beat) {
        if (beat == null || anchoredBpm <= 0) return;
        if (!beat.locked()) {
            anchorInGrid = false;
            return;
        }
        if (anchorInGrid) return;
        String reason = beat.lastResetReason();
        if (!"quiet".equals(reason)) {
            // The music moved under the anchor, so it is no longer an anchor.
            Log.i(TAG, "anchor dropped: the grid's last reset was " + reason);
            beat.dropHeldLock();
            anchoredBpm = 0;
            anchorDownbeatSeconds = -1;
            return;
        }
        // The phase as it was when the music went quiet, not re-derived from the
        // moment the listener tapped minutes ago: a period the loop has since
        // corrected turns that old moment into a whole beat of error.
        if (!beat.restoreHeldLock()) {
            beat.lockTempoAndDownbeat(anchoredBpm, anchorDownbeatSeconds, true);
        }
        beat.setMeter(lastMeter);
        anchorInGrid = true;
        Log.i(TAG, String.format(java.util.Locale.US, "anchor restored after the music went quiet: %.1f BPM, %d beats a bar" + " (phase kept, not recomputed)", anchoredBpm, lastMeter));
        if (song != null) {
            song.say("我", String.format(java.util.Locale.US, "重新用回你标的锚：%.1f BPM，%d 拍一小节", anchoredBpm, lastMeter), atSeconds);
        }
    }
    

/**
     * Lock the grid to the taps, and write down what was locked.
  *
  * `manual` is passed as true on purpose: a tempo that came from a person
  * should not be nudged away by the same inference that was wrong in the first
  * place. The phase is still tracked - that is the part the music can be
  * trusted to correct.  */
    private void finishTapTempo() {
        if (!tapping) return;
        tapping = false;
        // Everything the grid needs comes out of the listener's two buttons, and
        // each thing by the method they are good at: the tempo from the spaces
        // between presses, the downbeat from the press they called the bar head,
        // and the meter from counting the beats in between. Nothing is derived from
        // the grid, which is the point - the grid's beat is what was in doubt.
        BeatDetector beat = grid;
        double beatBpm = anchor.beatBpm();
        double beatMillis = beatBpm > 0 ? 60_000.0 / beatBpm : 0;
        int meter = anchor.meter();
        double downbeatSeconds = anchor.lastDownbeatMillis() / 1000.0;
        boolean believable = anchor.enough() && beatBpm >= MIN_BEAT_BPM && beatBpm <= MAX_BEAT_BPM && meter > 0;
        // The tracker's own opinion, read before it is overwritten: a tapped tempo
        // that is half or double it is the mistake this has made three times now -
        // measured, 73.5 tapped against a grid reading 140.7, which then dragged the
        // phase for ever because the loop refuses to jump octaves by design. Rather
        // than let it slide, the disagreement is put to the listener, who can settle
        // it with one press.
        double trackerBpm = beat == null ? 0 : beat.bpm();
        double doubled = beatBpm * 2;
        double halved = beatBpm / 2;
        boolean octaveApart = believable && trackerBpm > 0 && (Math.abs(trackerBpm - doubled) < doubled * 0.15 || Math.abs(trackerBpm - halved) < halved * 0.15);
        if (song != null) {
            song.say("我", String.format(java.util.Locale.US, believable ? "对拍完成：%d 下，每下 %.0fms（%.1f BPM），%d 拍一小节，" + "第 1 拍由你标出；鼓型吻合 %.0f%%" : "对拍没有锁上：%d 下，还没有数够一小节，或不像音乐的速度", anchor.pressCount(), beatMillis, beatBpm, meter, beat == null ? 0 : beat.drumScore() * 100), atSeconds);
        }
        Log.i(TAG, String.format(java.util.Locale.US, "anchor finished: %d presses, beat=%.0fms bpm=%.1f meter=%d beatOneAt=%.2fs" + " believable=%s drum=%.2f", anchor.pressCount(), beatMillis, beatBpm, meter, downbeatSeconds, believable, beat == null ? 0 : beat.drumScore()));
        if (octaveApart) {
            askAboutOctave(beatBpm, trackerBpm, downbeatSeconds, meter);
            return;
        }
        lockAnchor(beatBpm, downbeatSeconds, meter, "tapped");
    }
    

/**
     * "Is it what you tapped, or twice that?"
  * Only asked when the grid's own estimate and the listener's are an octave
  * apart, which is the one disagreement that cannot be resolved by any amount
  * of waiting: the tempo loop is built to refuse that jump, so a wrong octave
  * stays wrong and slides. Both numbers are shown because they are the whole
  * question, and the answer is applied immediately rather than logged.  */
    private void askAboutOctave(double tapped, double tracker, double downbeatSeconds, int meter) {
        if (song != null) {
            song.say("我", String.format(java.util.Locale.US, "对拍：你打的是 %.1f，网格自己算的是 %.1f（差一倍），问你哪个对", tapped, tracker), atSeconds);
        }
        main.post(() -> {
            if (card == null) return; card.ask("哪个速度对？", String.format(java.util.Locale.US, "你打的是 %.0f BPM，网格自己算的是 %.0f BPM（差一倍）", tapped, tracker), new String[]{
"按我打的", "快一倍", "慢一半"
            }
, new Runnable[]{
                            () -> lockAnchor(tapped, downbeatSeconds, meter, "tapped"), () -> lockAnchor(tapped * 2, downbeatSeconds, meter, "doubled"), () -> lockAnchor(tapped / 2, downbeatSeconds, meter, "halved")
            }
); resizeCard(true); card.setVisibility(View.VISIBLE);
        }
);
    }
    

/** Lock the grid to a tempo the listener settled on, and say which one it was. */
    

/**
     * Work out which bar the anchor sits in, by counting back to the start of the song.
  * The player's position is the song's own clock - zero is the first sample of
  * the file - so a whole number of bars back from the anchor lands on a bar line
  * near the beginning, and rounding picks the nearest one. That is bar one, and
  * every bar number follows from it: the same on every playthrough, which is
  * what makes two sessions' numbers mean the same place.
  * Recorded as extrapolated rather than as fact: how the music begins - an
  * intro, a pickup, a first bar that starts before the file does - is not
  * something this can know, and a later mark can supersede it by appending a
  * record rather than by rewriting this one.  */
    private void numberTheBars(double beatBpm, int meter) {
        double songSeconds = Playback.positionSeconds;
        double barSeconds = beatBpm > 0 ? meter * 60.0 / beatBpm : 0;
        if (songSeconds < 0 || barSeconds <= 0) {
            barOneSongSeconds = -1;
            anchorBarNumber = 0;
            return;
        }
        double bars = songSeconds / barSeconds;
        // The first bar line at or after the start of the song is bar one, so
        // whatever comes before it - a pickup, an intro - falls in bar zero.
        barOneSongSeconds = Math.ceil(bars) * barSeconds;
        anchorBarNumber = (int) Math.floor((songSeconds - barOneSongSeconds) / barSeconds) + 1;
        anchorSongSeconds = songSeconds;
        if (song != null) {
            song.say("我", String.format(java.util.Locale.US, "编号：按 %.1f BPM、%d 拍一小节往前推，第 1 小节在 %.1f 秒；" + "你标的位置是第 %d 小节（推算，可由后来的标记修正）", beatBpm, meter, barOneSongSeconds, anchorBarNumber), atSeconds);
        }
        Log.i(TAG, String.format(java.util.Locale.US, "bar numbering: barOneAt=%.2fs anchorIsBar=%d barSeconds=%.3f songAt=%.2fs", barOneSongSeconds, anchorBarNumber, barSeconds, songSeconds));
    }
    

/**
     * Which bar of the song the present moment is in, or 0 when that is unknown.
  *
  * Counted outwards from the anchor, not from bar one: the bars between the
  * anchor and now are the ones the grid has actually been tracked through, and
  * counting short distances from the nearer reference cannot drift.  */
    private int songBarNumber() {
        double barSeconds = anchoredBpm > 0 ? lastMeter * 60.0 / anchoredBpm : 0;
        double songAt = Playback.positionSeconds;
        if (barSeconds <= 0 || anchorBarNumber == 0 || songAt < 0 || anchorSongSeconds < 0) {
            return 0;
        }
        return anchorBarNumber + (int) Math.round((songAt - anchorSongSeconds) / barSeconds);
    }
    private void lockAnchor(double beatBpm, double downbeatSeconds, int meter, String how) {
        BeatDetector beat = grid;
        boolean believable = beatBpm >= MIN_BEAT_BPM && beatBpm <= MAX_BEAT_BPM;
        if (believable && beat != null) {
            beat.lockTempoAndDownbeat(beatBpm, downbeatSeconds, true);
            beat.setMeter(meter);
            anchoredBpm = beatBpm;
            anchorDownbeatSeconds = downbeatSeconds;
            anchorInGrid = true;
            lastMeter = meter;
            numberTheBars(beatBpm, meter);
            recordCalibration(beatBpm, downbeatSeconds, meter, how);
            // The mark the listener just made is a bar head, so it is shown: this
            // one flash is the confirmation that what they counted was understood,
            // and it is the only flash they need to check against their own ears.
            lastFlashedBar = Long.MIN_VALUE;
            flashesSinceAnchor = 0;
            main.post(() -> {
                if (card != null) card.flash();
            }
);
        }
        if (song != null) {
            song.say("我", String.format(java.util.Locale.US, believable ? "锚定完成（%s）：%.1f BPM，每下 %.0fms，%d 拍一小节，第 1 拍由你标出" : "锚定没有生效（%s）：%.1f BPM 不像音乐的速度", how, beatBpm, 60_000.0 / Math.max(1, beatBpm), meter), atSeconds);
        }
        // The drums get a say on which beat is beat one: every phase fits the
        // onsets equally well, so the tempo cannot tell, but a bar line one beat
        // out puts the snare on the downbeat and the drums say so. Reported rather
        // than applied - the listener marked it deliberately, and one press of
        // 闪的不是第 1 拍 moves the line if the drums are right.
        int suggested = beat == null ? Integer.MIN_VALUE : beat.bestDownbeatShift();
        if (suggested > 0 && song != null) {
            song.say("我", String.format(java.util.Locale.US, "鼓型看，小节头应该再往后 %d 拍（按「闪的不是第 1 拍」%d 次即可）", suggested, suggested), atSeconds);
        }
        Log.i(TAG, String.format(java.util.Locale.US, "anchor locked: %s bpm=%.1f meter=%d downbeat=%.2fs believable=%s" + " drum=%s suggests shift=%s", how, beatBpm, meter, downbeatSeconds, believable, beat == null ? "-" : String.format(java.util.Locale.US, "%.2f", beat.drumScore()), suggested == Integer.MIN_VALUE ? "none" : String.valueOf(suggested)));
        main.post(() -> {
            if (card == null) return; card.collapse(); resizeCard(false);
        }
);
    }
    

/**
     * Keep the grid this song just locked, for the export at the end of it.
  * Placing it on the song's clock takes one subtraction: the player's position
  * and the capture clock are read at the same instant, so how far behind the
  * song's own timeline the capture clock is is known exactly then, and the
  * press was that many seconds of playback earlier. The player only publishes
  * a position when it feels like it, so this is as close as the phone can get -
  * which is why every press keeps both clocks and nothing here is the last
  * word: the desktop draws the grid, the researcher decides whether it fits.  */
    private void recordCalibration(double beatBpm, double downbeatSeconds, int meter, String how) {
        BeatDetector beat = grid;
        double reported = Playback.positionSeconds;
        double origin = downbeatSeconds;
        if (beat != null && Playback.available && reported >= 0) {
            origin = reported - (beat.audioTime() - downbeatSeconds);
        }
        java.util.List<org.json.JSONObject> taps = new java.util.ArrayList<>(tapLines);
        org.json.JSONObject calibration = com.fallenangel.listeningai.listening.MgmcsExport .calibration("cal_" + String.format(java.util.Locale.US, "%02d", calibrations.size() + 1), origin, beatBpm, meter, "由 " + taps.size() + " 下按压重算（" + how + "）", taps);
        calibrations.add(calibration);
        Log.i(TAG, String.format(java.util.Locale.US, "calibration kept: %s %.1f BPM %d/4 at %.3fs of the song, from %d presses", calibration.optString("calibrationId"), beatBpm, meter, origin, taps.size()));
        // Written now rather than at the end of the song: the whole point of
        // tapping is to go and look at it, and a file that appears four minutes
        // later is a file nobody checks. It grows as the song does; the last
        // write, when the song ends, is the complete one.
        writeTheExport(song, false);
    }
    

    /** The listener changed the song here, and which one they went to. */
    private org.json.JSONObject songChangeLine(String to) {
        org.json.JSONObject line = new org.json.JSONObject();
        try {
            line.put("kind", "SONG_CHANGE");
            line.put("to", to == null ? "" : to);
        } catch (Exception ignored) {
        }
        return line;
    }

    /**
     * The last moment this song's record knows about.

     * Needed because the song clock has already moved on by the time a switch is
     * noticed: the player is on the next track and reports 00:00.0, so a leave
     * written with "now" lands at the beginning of the song - measured, a session
     * with taps up to 3:37 recorded its switch at 00:00.0. The last thing the
     * record already knows is the honest answer to "when did they leave".
     */
    private double lastKnownSongSeconds() {
        double best = 0;
        com.fallenangel.listeningai.listening.SongLog about = song;
        if (about != null) {
            for (org.json.JSONObject line : about.lines()) {
                double at = line.optDouble("songTime", -1);
                if (at > best) best = at;
            }
        }
        return best;
    }

    /**
     * A song's record starts here, and whether this is a song they came back to.

     * The count is what makes "I switched away and came back to redo it" visible
     * in the data instead of being something only the listener remembers: the
     * second pass over a song says so, which is the difference between a repeated
     * measurement and two different songs that happen to share a title.
     */
    private org.json.JSONObject songEnterLine(String from) {
        int plays = playedTitles.contains(Playback.trackTitle)
                ? playCounts.getOrDefault(Playback.trackTitle, 1) + 1 : 1;
        playedTitles.add(Playback.trackTitle);
        playCounts.put(Playback.trackTitle, plays);
        org.json.JSONObject line = new org.json.JSONObject();
        try {
            line.put("kind", "SONG_ENTER");
            line.put("from", from == null ? "" : from);
            line.put("reentry", plays > 1);
            line.put("playsSoFar", plays);
        } catch (Exception ignored) {
        }
        Log.i(TAG, String.format(java.util.Locale.US,
                "song enter: %s (play %d of this title this run, re-entry=%s)",
                Playback.trackTitle, plays, plays > 1));
        return line;
    }

    /** The listener jumped to another place in the same song. */
    private org.json.JSONObject seekLine(double from, double to) {
        org.json.JSONObject line = new org.json.JSONObject();
        try {
            line.put("kind", "SEEK");
            line.put("fromSeconds", Math.round(from * 100) / 100.0);
            line.put("toSeconds", Math.round(to * 100) / 100.0);
            line.put("backwards", to < from);
        } catch (Exception ignored) {
        }
        return line;
    }

/**
     * Write this song's document for the desktop validator.
  * Called once, when the song is over - either because another one started or
  * because listening stopped. A song listened to twice is two directories and
  * two documents, which is the same rule the rest of the record follows: a
  * second listen is a second observation, not a correction of the first.  */
    private void writeTheExport(com.fallenangel.listeningai.listening.SongLog about, boolean announce) {
        if (about == null) return;
        try {
            org.json.JSONObject document = com.fallenangel.listeningai.listening.MgmcsExport.document( about.directory().getName(), about.label(), "", Playback.durationSeconds, calibrations, about.lines());
            java.io.File written = about.writeMgmcs(document);
            String summary = com.fallenangel.listeningai.listening.MgmcsExport .summarise(document);
            Log.i(TAG, "validator export: " + summary + " -> " + written);
            if (announce) about.say("记", "导出给电脑端验证器：" + summary, atSeconds);
        }
 catch (Throwable t) {
            Log.w(TAG, "could not write the validator export", t);
        }
    }
    

/**
     * Drop what the model remembers of the music before this point.
  *
  * Novelty is measured against the model's memory of the recent past, and a
  * track change makes that memory a description of a different song: without
  * this, every new track would look like a dramatic change for as long as the
  * memory is long - which is exactly how a habit of asking turns into a habit
  * of interrupting. The rules engine has always done this for its own
  * features; this is the same idea for the model's.  */
    private void forgetTheOldSong(BeatDetector grid) {
        if (onnx == null) return;
        try {
            onnx.reset(Math.round(grid.audioTime() * SAMPLE_RATE));
            reading = null;
            lastNovelty = 0;
            noticedSomething = false;
            Log.i(TAG, "the model forgot the music before this point");
        }
 catch (Throwable t) {
            Log.w(TAG, "could not reset the audio model", t);
        }
    }
    

/**
     * Where in the song we are.
  *
  * The player's own position when the media session is readable, because that
  * is the song's timeline and it survives a seek; our own clock otherwise,
  * counted from the moment this song started. Everything written down uses
  * this, so "以歌曲为基准" holds even on a phone where the session is not.  */
    private double whereInSong(BeatDetector grid) {
        // Not the raw report: between reports the player's number stands still, and a
        // bar flash that reads a standing number lands late and in jumps.
        double reported = Playback.positionNow();
        if (Playback.available && reported >= 0) return reported;
        if (grid == null) return atSeconds;
        return Math.max(0, grid.audioTime() - songStartAudioTime);
    }
    

/**
     * Start a new song's record.
  *
  * Called at the start and at every track change the player reports. The name
  * comes from the media session; when there is none - which is the case until
  * the system lets this app read it - the song is numbered instead, so the
  * file is still one song's file even when the name is missing.  */
    private void startSong(BeatDetector grid, String title) {
        String name = title == null ? "" : title.trim();
        String artist = Playback.trackArtist == null ? "" : Playback.trackArtist.trim();
        String label = !name.isEmpty() ? (artist.isEmpty() ? name : artist + " — " + name) : "未知曲目 " + (++songNumber);
        com.fallenangel.listeningai.listening.SongLog fresh = new com.fallenangel.listeningai.listening.SongLog( listeningDirectory(), label, System.currentTimeMillis());
        // The last song's questions, counted into that song's own file: the
        // numbers this round is read by, left where they can be read.
        if (song != null && questions != null) song.say("记", questionTally(), atSeconds);
        appendTheTally(song);
        tally.reset();
        // The song just ended, so its own directory is finished and the older ones
        // can be judged against it.
        pruneOldListens();
        // A new song is a new key: the chroma of the last one says nothing about
        // this one, and carrying it over is how every song after the first would
        // be read in the previous song's key.
        java.util.Arrays.fill(songChroma, 0);
        songKey = null;
        chordChanges.reset();
        shownChord = "";
        chordBox.reset();
        heldPauseWindow = null;
        // A new song: whatever was known about the last one says nothing here.
        timeline = null;
        lookedForTimeline = false;
        cacheWasOn = false;
        chordTrack = null;
        lookedForChordTrack = false;
        // And the grids it locked, written now that the song is finished: only
        // this song's presses belong in this song's document.
        writeTheExport(song, true);
        calibrations.clear();
        tapLines.clear();
        song = fresh;
        songStartAudioTime = grid == null ? 0 : grid.audioTime();
        lastReadableReading = "";
        // A new song is a new timeline: where the questions fell in the last one
        // says nothing about this one, so the picker starts again rather than
        // carrying a cooldown over that would silence the first bars.
        questions = new QuestionPicker(questionBank(), MIN_QUESTION_GAP_BARS);
        fresh.say("听", "开始", 0);
        if (!Playback.available) {
            // Said once per song, in the song's own file, because a numbered file
            // with no explanation looks like a bug rather than a missing
            // permission.
            fresh.say("听", "读不到播放器信息：曲目名和切歌都靠它，现在没有", 0);
        }
        Log.i(TAG, "song: " + label + " -> " + fresh.directory());
    }
    // ------------------------------------------------------------ query policy
    

/**
     * The decision layer's settings, from the asset, or the built-in guesses.
  *
  * In a file because every one of these numbers is a guess about this listener
  * that will be tuned by listening, and a guess that can be tuned without a
  * build is a guess that actually gets tuned.  */
    private QueryPolicyConfig readQueryConfig() {
        try (java.io.InputStream in = getAssets().open("query_policy.json")) {
            java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
            byte[] buffer = new byte[4096];
            int read;
            while ((read = in.read(buffer)) > 0) out.write(buffer, 0, read);
            return QueryPolicyConfig.fromJson( new org.json.JSONObject(out.toString("UTF-8")));
        }
 catch (Exception missing) {
            return QueryPolicyConfig.defaults();
        }
    }
    

/**
     * The questions this build may ask, from the asset.
  * A file because the bank is the research instrument: it is meant to be
  * read and replaced by somebody who is not editing Java. A build without one
  * is not a build without questions - it is the app as it was before, where
  * the only thing asked about is a pause the listener made themselves.  */
    private QuestionBank questionBank() {
        if (questionBank == null) {
            questionBank = loadQuestionBank();
        }
        return questionBank;
    }

    /**
     * The words a candidate may use, read once.
     *
     * A missing or broken file is not an error: the app then allows any label, which
     * is how it behaved before the vocabulary existed. A vocabulary that will not
     * parse must not take the questions down with it.
     */
    private com.fallenangel.listeningai.listening.CandidateVocabulary vocabulary() {
        if (vocabulary == null) {
            com.fallenangel.listeningai.listening.CandidateVocabulary loaded;
            try (java.io.InputStream in = getAssets().open("candidate_vocabulary.json")) {
                java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
                byte[] buffer = new byte[4096];
                int read;
                while ((read = in.read(buffer)) > 0) out.write(buffer, 0, read);
                loaded = com.fallenangel.listeningai.listening.CandidateVocabulary.fromJson(
                        new org.json.JSONObject(out.toString("UTF-8")));
                Log.i(TAG, "candidate vocabulary: " + loaded);
            } catch (Exception missing) {
                Log.w(TAG, "no usable candidate vocabulary; any label the model writes is allowed", missing);
                loaded = com.fallenangel.listeningai.listening.CandidateVocabulary.OPEN;
            }
            vocabulary = loaded;
        }
        return vocabulary;
    }

    /**
     * The record, read back: every card this listener has been shown, and what they
     * chose on it.
     *
     * Read once, on the thread that asks the model - not at start-up, because most
     * sessions never ask a question at all, and not on the main thread, because it
     * is a walk over every song played so far. A record that will not parse is
     * skipped: the memory of the other songs is still worth having.
     */
    private com.fallenangel.listeningai.listening.AnswerMemory answerMemory() {
        if (memory != null) return memory;
        com.fallenangel.listeningai.listening.AnswerMemory loaded =
                new com.fallenangel.listeningai.listening.AnswerMemory();
        java.io.File root = listeningDirectory();
        java.io.File[] songs = root == null ? null : root.listFiles();
        int read = 0;
        if (songs != null) {
            for (java.io.File song : songs) {
                java.io.File events = new java.io.File(song, "events.jsonl");
                if (!events.isFile()) continue;
                try {
                    java.util.List<String> lines = java.nio.file.Files.readAllLines(
                            events.toPath(), java.nio.charset.StandardCharsets.UTF_8);
                    for (String line : lines) {
                        if (line.isEmpty()) continue;
                        org.json.JSONObject record = new org.json.JSONObject(line);
                        org.json.JSONArray candidates = record.optJSONArray("candidates");
                        if (candidates == null) continue;
                        java.util.List<String> labels = new java.util.ArrayList<>();
                        for (int i = 0; i < candidates.length(); i++) {
                            org.json.JSONObject item = candidates.optJSONObject(i);
                            if (item != null) labels.add(item.optString("label", ""));
                        }
                        loaded.learn(labels, record.optString("chosenLabel", ""));
                    }
                    read++;
                } catch (Exception unreadable) {
                    Log.i(TAG, "could not read back " + events.getParentFile().getName()
                            + ": " + unreadable);
                }
            }
        }
        Log.i(TAG, String.format(java.util.Locale.US,
                "answer memory: %d songs read, %d labels known, %d worn out",
                read, loaded.size(), loaded.tiredLabels().size()));
        if (!loaded.tiredLabels().isEmpty()) {
            Log.i(TAG, "not offering again: " + loaded.tiredLabels());
        }
        memory = loaded;
        return memory;
    }

    private QuestionBank loadQuestionBank() {
        if (questionBank == null) {
            QuestionBank loaded;
            try (java.io.InputStream in = getAssets().open("question_bank.json")) {
                java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
                byte[] buffer = new byte[4096];
                int read;
                while ((read = in.read(buffer)) > 0) out.write(buffer, 0, read);
                loaded = QuestionBank.fromJson(new org.json.JSONArray(out.toString("UTF-8")));
                Log.i(TAG, "question bank: " + loaded + " -> " + loaded.all());
            }
 catch (Exception missing) {
                Log.w(TAG, "no usable question bank; only a listener's own pause is asked about", missing);
                loaded = QuestionBank.fromJson(new org.json.JSONArray());
            }
            questionBank = loaded;
            questions = new QuestionPicker(loaded, MIN_QUESTION_GAP_BARS);
        }
        return questionBank;
    }
    

/**
     * Which bar the question rules count in.
  * Bars rather than seconds because the rule the listener gave is in bars:
  * the same question should not come round again inside one phrase, and how
  * long a phrase is is a property of the song. Counted from the anchor when
  * there is one - that numbering is what the flashes show - and from the
  * tracker's own tempo before that, so the rules hold from the first question
  * rather than the second. Zero means there is no bar clock at all, and the
  * bank is not asked from a clock that does not exist.  */
    private int questionBar() {
        int anchored = songBarNumber();
        if (anchored > 0) return anchored;
        BeatDetector beat = grid;
        double bpm = anchoredBpm > 0 ? anchoredBpm : (beat == null ? 0 : beat.bpm());
        if (!(bpm >= MIN_BEAT_BPM) || lastMeter <= 0) return 0;
        double barSeconds = lastMeter * 60.0 / bpm;
        return (int) Math.floor(whereInSong(beat) / barSeconds);
    }
    

/** The bank's written answers, as the card's choices. */
    private static List<PauseQuestion.Candidate> bankOptions(QuestionTemplate template) {
        List<PauseQuestion.Candidate> options = new java.util.ArrayList<>();
        for (String option : template.options) {
            options.add(new PauseQuestion.Candidate(option, option, 0, ""));
        }
        return options;
    }
    

/**
     * A bank question as its own line in the song's record.
  * The template id is the point of it: this round is read by comparing
  * answers to the same question, and two differently-worded questions do not
  * have the same answers. What it measures, where in the song it was asked
  * and what was on offer go with it, so the counts can be redone later
  * without the phone.  */
    private org.json.JSONObject bankLine(String state, QuestionTemplate template, int songBar, double songSeconds, String answer) {
        org.json.JSONObject line = new org.json.JSONObject();
        try {
            line.put("type", "QUERY");
            line.put("state", state);
            line.put("templateId", template.id);
            line.put("targetSignal", template.targetSignal);
            line.put("text", template.text);
            line.put("songBar", songBar);
            line.put("songSeconds", Math.round(songSeconds * 100.0) / 100.0);
            if (questions != null) {
                line.put("timesAsked", questions.askedCount(template.id));
                line.put("timesIgnored", questions.unansweredCount(template.id));
            }
            org.json.JSONArray options = new org.json.JSONArray();
            for (String option : template.options) options.put(option);
            line.put("options", options);
            if (answer != null) line.put("answer", answer);
        }
 catch (Exception ignored) {
            // JSONObject is a map; the only way this throws is out of memory.
        }
        return line;
    }
    

/**
     * What this song's questions came to, in one line for that song's own file.
  * The first round is read by four numbers per question - how often it was
  * answered, how often the answer was "说不清", whether one answer took
  * everything, and whether questions on the same signal tell apart. Those are
  * counted here so the phone is enough to read them.  */
    private String questionTally() {
        QuestionBank bank = questionBank();
        if (questions == null || bank.all().isEmpty()) return "这一轮没有从问题库提问";
        StringBuilder out = new StringBuilder("问题统计（问过/没答）：");
        int asked = 0, ignored = 0;
        for (QuestionTemplate template : bank.all()) {
            int timesAsked = questions.askedCount(template.id);
            int timesIgnored = questions.unansweredCount(template.id);
            asked += timesAsked;
            ignored += timesIgnored;
            out.append(template.id).append(' ').append(timesAsked) .append('/').append(timesIgnored).append(" · ");
        }
        out.append("共 ").append(asked).append(" 问，").append(ignored).append(" 个没答");
        return out.toString();
    }

    /**
     * This song's four numbers, appended to the file that spans songs.
     *
     * Beside the per-song folders rather than inside one: "did the app ask too
     * often" is not a question about a song, and a number that only exists inside
     * its own folder is a number nobody ever compares with anything.
     *
     * A failure here is logged and dropped. Losing a row of statistics is not a
     * reason to disturb the listener or to stop the listening.
     */
    private void appendTheTally(com.fallenangel.listeningai.listening.SongLog about) {
        if (about == null) return;
        try {
            File root = about.directory().getParentFile();
            if (root == null) return;
            // Versioned in the name, and not overwritten. The first version of
            // this file was written with seven columns; the row grew three when
            // remarks and the two kinds of annoyance were counted, and the header
            // was only written when the file did not exist yet - so the file that
            // exists has a header describing different columns than its rows. A
            // table whose header lies about it is worse than two tables.
            File file = new File(root, "summary-v2.csv");
            boolean first = !file.exists();
            try (java.io.FileWriter out = new java.io.FileWriter(file, true)) {
                if (first) {
                    out.write(QuestionTally.header());
                    out.write("\n");
                }
                out.write(tally.csvRow(
                        new java.text.SimpleDateFormat("yyyy-MM-dd HH:mm", java.util.Locale.US)
                                .format(new java.util.Date()),
                        about.label()));
                out.write("\n");
            }
            Log.i(TAG, "summary row for " + about.label() + ": " + tally.csvRow("", ""));
        } catch (Throwable t) {
            Log.i(TAG, "could not write the summary row: " + t);
        }
    }

    /**
     * One sentence about the music, said while the music keeps playing.
     *
     * The words come from the model when there is one, because a remark is the app
     * talking and a column of tags read aloud is not talking. What the local
     * reading says is the fallback, not the source: with no model configured the
     * app still says something true rather than going silent, because a companion
     * that only speaks while the network is up is a server with a personality.
     */
    private void saySomething(com.fallenangel.listeningai.clip.StyleReading said,
                              RecentAudio recent) {
        tally.remarked();
        final String local = said == null ? "" : said.sentence();
        final RecentAudio.Window window = recent == null ? null : recent.tail(WINDOW_SECONDS);
        // Claiming the card now, so that anything newer takes it away from this.
        // A remark is the least important thing the app has to say and it must
        // never land on top of a question - that is the whole reason the stage
        // exists, and the reason this is not decided after the model answers.
        final long token = stage.show(CardStage.Content.MESSAGE);

        final String heading = "";

        android.content.SharedPreferences prefs = getSharedPreferences(PREFS, MODE_PRIVATE);
        String endpoint = prefs.getString(PREF_ENDPOINT, "");
        String key = prefs.getString(PREF_KEY, "");
        if (!ExplanationClient.configured(endpoint, key) || window == null) {
            showRemark(token, heading.isEmpty() ? local : heading,
                    heading.isEmpty() ? "" : local, "本地");
            return;
        }
        String model = prefs.getString(PREF_MODEL, ExplanationClient.DEFAULT_MODEL);
        cloud.execute(() -> {
            String said_it = null;
            try {
                said_it = new ExplanationClient(endpoint, key, model, ExplanationClient::post)
                        .remark(window.pcm, 0, window.pcm.length, SAMPLE_RATE);
            } catch (Exception e) {
                Log.i(TAG, "no remark from the model (" + e.getClass().getSimpleName()
                        + "), saying what the local reading says");
            }
            boolean fromModel = said_it != null && !said_it.isEmpty();
            String note = fromModel ? said_it : local;
            // Two things, in the order they can be trusted: what was measured,
            // and what the model made of it.
            showRemark(token, heading.isEmpty() ? note : heading,
                    heading.isEmpty() ? "" : note, fromModel ? "模型" : "本地");
        });
    }

    /**
     * Keep the newest few listens of each song, and delete the ones before them.
     *
     * Run when a song ends, because that is when this listen's own directory is
     * complete - and deleting it, or judging it half-written, would be the one
     * mistake that matters. The rule itself is in {@link SessionKeeper}; what
     * happens here is the walking and the deleting.
     *
     * Every removal is logged by name. This is the only thing in the app that
     * destroys the record, and a deletion nobody can see in the log is
     * indistinguishable from data that went missing.
     */
    private void pruneOldListens() {
        java.io.File root = listeningDirectory();
        java.io.File[] entries = root == null ? null : root.listFiles();
        if (entries == null) return;
        java.util.List<String> names = new java.util.ArrayList<>();
        for (java.io.File entry : entries) if (entry.isDirectory()) names.add(entry.getName());
        String current = song == null ? "" : song.directory().getName();
        java.util.List<String> doomed;
        try {
            doomed = com.fallenangel.listeningai.listening.SessionKeeper.toDelete(names, current);
        } catch (Throwable failed) {
            Log.i(TAG, "could not work out what to prune: " + failed);
            return;
        }
        for (String name : doomed) {
            java.io.File victim = new java.io.File(root, name);
            // Belt and braces: only ever inside the listening directory, and only
            // ever a directory.
            if (!victim.isDirectory() || !root.equals(victim.getParentFile())) continue;
            if (deleteEverythingIn(victim)) {
                Log.i(TAG, "pruned an old listen: " + name);
            } else {
                Log.i(TAG, "could not prune " + name);
            }
        }
    }

    /** Removes a directory and what is in it; true when nothing is left. */
    private boolean deleteEverythingIn(java.io.File directory) {
        java.io.File[] children = directory.listFiles();
        if (children != null) {
            for (java.io.File child : children) {
                if (child.isDirectory()) deleteEverythingIn(child);
                else child.delete();
            }
        }
        return directory.delete();
    }

    /**
     * A beat grid for this song, from a file the listener or the Workbench left behind.
     *
     * Read at the start of a song, from a folder of its own - files are dropped in
     * there, the app does not fetch anything. The player's own duration is not used
     * to match on: on this phone it is wrong by tens of seconds, which would make a
     * duration check reject exactly the files that are right.
     */
    private com.fallenangel.listeningai.listening.WorkbenchGrid workbenchGrid(
            com.fallenangel.listeningai.listening.SongLog current, java.util.List<String> journal) {
        if (current == null) return null;
        // Beside the session folders, not inside them: this is where files the
        // listener drops in live, and the session folder is the app's own - which is
        // also the one that gets pruned when a song has been heard too many times.
        // Measured 2026-10-14: the first version looked inside it, and the file that
        // had just been put beside it was never found.
        java.io.File root = new java.io.File(listeningDirectory().getParentFile(), "analysis");
        java.io.File[] files = root.isDirectory() ? root.listFiles() : null;
        if (files == null || files.length == 0) {
            Log.i(TAG, "no analysis folder to read a grid from (" + root + ")");
            return null;
        }
        String label = current.label();
        for (java.io.File file : files) {
            if (!file.isFile() || !file.getName().endsWith(".json")) continue;
            try {
                String text = new String(java.nio.file.Files.readAllBytes(file.toPath()),
                        java.nio.charset.StandardCharsets.UTF_8);
                org.json.JSONObject fileJson = new org.json.JSONObject(text);
                // Whose song this file is about is decided before anything is read out of
                // it: a file about another song is worth silence, and a file about this
                // one that turns out to hold nothing usable is worth every reason why.
                if (!com.fallenangel.listeningai.listening.WorkbenchGrid.sameSong(
                        com.fallenangel.listeningai.listening.WorkbenchGrid.titleOf(fileJson), label)) {
                    continue;
                }
                Log.i(TAG, file.getName() + " 是这首歌的文件，开始挑读法");
                com.fallenangel.listeningai.listening.WorkbenchGrid grid =
                        com.fallenangel.listeningai.listening.WorkbenchGrid.from(fileJson, journal);
                if (grid == null) {
                    Log.i(TAG, "文件在，但没有一条读法能用：" + file.getName());
                    continue;
                }
                Log.i(TAG, "grid from " + file.getName() + ": reading " + grid.reading
                        + " (" + grid.shape + "), " + grid.beatCount() + " beats, "
                        + grid.beatsPerBar + "/4"
                        + (grid.audioSource.isEmpty() ? ", 素材没写" : ", 素材 " + grid.audioSource)
                        + (grid.caution ? ", 谨慎" : "")
                        + (grid.generatedBy.isEmpty() ? "" : ", by " + grid.generatedBy));
                return grid;
            } catch (Exception unreadable) {
                Log.i(TAG, "could not read " + file.getName() + ": " + unreadable);
            }
        }
        Log.i(TAG, "no analysis file matches " + label + "; tracking the beat instead");
        return null;
    }

    /**
     * This song's chords as the Workbench worked them out, from a file dropped beside
     * the grid files.
     *
     * Read the same way the grids are: files are put there by hand and the app fetches
     * nothing. One file may hold every song (a `songs[]` list, matched on the audio
     * file name it was measured from) or one song (`spans` at the top), because both
     * are ways the other side may hand it over. With a track in hand the app neither
     * measures nor remembers: the answer was already known.
     */
    private com.fallenangel.listeningai.listening.ChordTrack chordTrack(
            com.fallenangel.listeningai.listening.SongLog current) {
        if (current == null) return null;
        java.io.File root = new java.io.File(listeningDirectory().getParentFile(), "analysis");
        java.io.File[] files = root.isDirectory() ? root.listFiles() : null;
        if (files == null || files.length == 0) return null;
        String label = current.label();
        for (java.io.File file : files) {
            if (!file.isFile() || !file.getName().endsWith(".json")) continue;
            try {
                String text = new String(java.nio.file.Files.readAllBytes(file.toPath()),
                        java.nio.charset.StandardCharsets.UTF_8);
                com.fallenangel.listeningai.listening.ChordTrack found =
                        com.fallenangel.listeningai.listening.ChordTrack.from(
                                new org.json.JSONObject(text), label);
                if (found == null) continue;
                Log.i(TAG, "chords from " + file.getName() + ": " + found.size() + " 个半小节格"
                        + (found.keyName.isEmpty() ? "" : "，" + found.keyName)
                        + (found.reading.isEmpty() ? "" : "，小节来自 " + found.reading));
                if (song != null) {
                    song.say("记", "和弦来自 " + file.getName() + "（" + found.size()
                            + " 个半小节格）", 0);
                }
                return found;
            } catch (Exception unreadable) {
                Log.i(TAG, "could not read chords from " + file.getName() + ": " + unreadable);
            }
        }
        Log.i(TAG, "no analysis file has chords for " + label + "; measuring them instead");
        return null;
    }

    /**
     * Look for an earlier listen of this song, and read its chords back.
     *
     * Found by the song's label, which is what the directory names carry. Songs
     * with no name have nothing to find by, and a listen that was cut short is not
     * worth using - {@link ChordTimeline#useful()} decides that, because a fragment
     * used as a cache would show nonsense confidently, which is worse than
     * measuring.
     */
    private ChordTimeline findTimeline(com.fallenangel.listeningai.listening.SongLog current) {
        if (current == null) return null;
        String label = current.label();
        if (label == null || label.isEmpty() || label.startsWith("未知曲目")) return null;
        java.io.File root = listeningDirectory();
        java.io.File[] sessions = root == null ? null : root.listFiles();
        if (sessions == null) return null;
        String mine = current.directory().getName();
        java.util.List<java.io.File> candidates = new java.util.ArrayList<>();
        for (java.io.File session : sessions) {
            if (!session.isDirectory() || session.getName().equals(mine)) continue;
            // By song, not by name: a directory that had to be given a " 2" is the
            // same song, and matching on the raw name missed exactly the newer
            // listens - which is why the cache never found anything.
            if (!com.fallenangel.listeningai.listening.SessionKeeper.songOf(session.getName())
                    .equals(com.fallenangel.listeningai.listening.SongLog.slug(label))) {
                continue;
            }
            candidates.add(session);
        }
        // Newest first, and the first one that is worth using wins - not simply the
        // newest. A listen that was cut short, or one that itself read a cache and
        // wrote almost nothing, must not hide the good record behind it.
        candidates.sort((a, b) -> b.getName().compareTo(a.getName()));
        // Stitched together rather than picked: listening is not one act, and the
        // records left by starting, skipping and coming back cover the song between
        // them. Any single one of them was usually too short to use - which is what
        // "too little to reuse (11 readings)" meant.
        java.util.List<java.util.List<String>> listens = new java.util.ArrayList<>();
        for (java.io.File candidate : candidates) {
            java.io.File chords = new java.io.File(candidate, "chords.jsonl");
            if (!chords.isFile()) continue;
            try {
                listens.add(java.nio.file.Files.readAllLines(
                        chords.toPath(), java.nio.charset.StandardCharsets.UTF_8));
            } catch (Exception unreadable) {
                Log.i(TAG, "could not read " + candidate.getName() + " back: " + unreadable);
            }
        }
        if (listens.isEmpty()) {
            // Never silent: "no earlier listen to read" and "an earlier listen that
            // could not be used" look identical from the box, and one of them is a bug.
            Log.i(TAG, "no earlier listen of " + label + " has chords to reuse ("
                    + candidates.size() + " matching sessions)");
            return null;
        }
        ChordTimeline stitched = ChordTimeline.merge(listens);
        if (!stitched.useful()) {
            Log.i(TAG, "earlier listens cover too little to reuse (" + stitched.size()
                    + " readings across " + listens.size() + " listens)");
            return null;
        }
        Log.i(TAG, "reusing " + stitched.size() + " chord readings stitched from "
                + listens.size() + " earlier listens");
        return stitched;
    }

    /**
     * The chord right now, as the card would print it, or "" when it cannot say.
     *
     * Measured here rather than asked of a model, because this is the half of a
     * remark the listener can check against what they hear: given four clean
     * triads the model got one right, and this gets four of four. The key comes
     * from everything heard so far in the song and is held on to, because C major,
     * G major and A minor share six of seven notes and a fresh estimate every
     * window made songs without modulations look like they had several.
     */
    private Harmony.Reading harmonyNow(RecentAudio recent) {
        RecentAudio.Window window = recent == null ? null : recent.tail(HARMONY_WINDOW_SECONDS);
        if (window == null) return null;
        double[] chroma = Harmony.chroma(window.pcm, 0, window.pcm.length, SAMPLE_RATE);
        for (int i = 0; i < 12; i++) songChroma[i] += chroma[i];
        songKey = Harmony.keyOf(songChroma, songKey, KEY_MARGIN);
        Harmony.Reading chord = Harmony.chordOf(chroma, songKey);
        if (chord == null || chord.confidence < HARMONY_MIN_CONFIDENCE) return null;
        return chord;
    }

    /**
     * One line of raw harmony per second: the measurement, not the decision.
     *
     * Everything the display used to decide is in it - both shares, the gap between
     * them, and what was on screen at the time - because the interesting question
     * later is not "what chord was it" but "when could the app tell, and what did
     * it do while it could not". Nothing here is ever rewritten; the per-bar view
     * is derived from this later, if the beat grid ever earns the right to.
     */
    private void writeChordReading(Harmony.Reading heard, String reading, String box, String from) {
        com.fallenangel.listeningai.listening.SongLog about = song;
        if (about == null) return;
        org.json.JSONObject line = new org.json.JSONObject();
        try {
            line.put("key", songKey == null ? "" : songKey.name());
            if (heard != null) {
                line.put("chord", heard.chord);
                line.put("quality", heard.quality.name());
                line.put("root", heard.root);
                line.put("share", Math.round(heard.confidence * 1000) / 1000.0);
                line.put("runnerUp", Math.round(heard.runnerUp * 1000) / 1000.0);
                line.put("margin", Math.round(heard.margin() * 1000) / 1000.0);
                line.put("reading", reading);
            } else {
                line.put("chord", "");
                line.put("unreadable", true);
            }
            line.put("shown", box);
            // Where it came from, so a later reader can tell a measurement from a
            // replay - and so this file can be reused by the next listen either way.
            line.put("from", from);
        } catch (org.json.JSONException ignored) {
            // A JSONObject is a map; only out-of-memory throws here.
        }
        about.chord(line, atSeconds);
    }

    /**
     * Put the current chord in its own box, if it has changed.
     *
     * Comparing first so the window is touched only when there is something new to
     * say: this runs every pass through the loop, and a view being set to the same
     * text five times a minute is a view being redrawn for nothing.
     */
    private void showChord(String reading, String source, boolean doubted) {
        String text = reading == null ? "" : reading;
        String label = doubted ? source + "·存疑" : source;
        if (text.equals(shownChord) && label.equals(shownChordSource)) return;
        // The one that was up becomes the one before it: the pair is what was heard.
        previousChord = shownChord;
        shownChord = text;
        shownChordSource = label;
        main.post(() -> {
            if (card != null) card.setChord(text, previousChord, label);
        });
    }

    /**
     * Put a remark on the card, unless something more important took it first.
     *
     * Nothing is waiting on it, so it takes itself away: a remark that has to be
     * dismissed is a question with worse manners.
     */
    private void showRemark(long token, String text, String detail, String source) {
        if (text == null || text.isEmpty()) return;
        if (!stage.stillShowing(token)) {
            Log.i(TAG, "a remark arrived after the card had moved on; dropped it");
            return;
        }
        Log.i(TAG, "a remark (" + source + "): " + text);
        if (song != null) song.say("我", "随口一句：" + text, atSeconds);
        main.post(() -> {
            if (card == null || !stage.stillShowing(token)) return;
            card.showRemark(text, detail, "知道了", choice -> {
                card.collapse();
                resizeCard(false);
            });
            resizeCard(true);
            card.setVisibility(View.VISIBLE);
        });
        main.postDelayed(() -> main.post(() -> {
            if (card == null || !stage.stillShowing(token)) return;
            card.collapse();
            resizeCard(false);
        }), REMARK_VISIBLE_MILLIS);
    }

    

/** What the policy needs to know about now, none of it about the music. */
    private QueryContext queryContext(boolean playing) {
        long now = System.currentTimeMillis();
        while (!askedAtMillis.isEmpty() && now - askedAtMillis.peekFirst() > 60_000) {
            askedAtMillis.pollFirst();
        }
        return new QueryContext(lastQuestionMillis, new java.util.ArrayList<>(askedTypes), dismissCount, stage.awaitingAnswer(), playing, askedAtMillis.size());
    }
    

/**
     * Build the moment the policy is asked about.
  * The candidates are the local analysis's own, which is what the app has
  * always explained a pause with - the query layer's job is to decide what to
  * do with them, not to invent new ones. A model-backed candidate list can be
  * appended beside them later without the policy changing.  */
    private MusicMoment buildMoment(BeatDetector grid, RecentAudio recent, String sectionLabel, long sectionBoundarySample, long nowMillis) {
        RecentAudio.Window window = recent.tail(WINDOW_SECONDS);
        if (window == null) return null;
        PauseQuestion analysed = ListeningAnalyzer.analyse(window.pcm, 0, window.pcm.length, window.startSample, gridFor(grid), sectionLabel, sectionBoundarySample);
        java.util.List<EventCandidate> candidates = new java.util.ArrayList<>();
        double top = 0;
        for (PauseQuestion.Candidate candidate : analysed.candidates) {
            if ("none".equals(candidate.id)) continue;
            candidates.add(new EventCandidate(candidate.id, candidate.confidence, "rules", candidate.evidence));
            top = Math.max(top, Math.min(1, candidate.confidence));
        }
        return new MusicMoment("m" + Long.toHexString(nowMillis), nowMillis - Math.round(WINDOW_SECONDS * 1000), nowMillis, analysed.bar, analysed.beat, analysed.subdivision, candidates, java.util.Collections.emptyList(), lastNovelty, top, java.util.Collections.emptyList());
    }
    

/**
     * Ask what the policy decided to ask, and write down that it was asked.
  * The question is one the app started, so the music comes back when it is
  * answered or when the listener ignores it - and being ignored is counted,
  * because a policy that keeps asking into silence is the failure this layer
  * is supposed to prevent.  */
    private void askWithPolicy(BeatDetector grid, MusicMoment moment, QueryDecision decision, String sectionLabel) {
        final String id = FeedbackRecord.newId(System.currentTimeMillis());
        final com.fallenangel.listeningai.listening.SongLog about = song;
        final boolean[] done = {
false
        }
;
        PauseQuestion question = queryRenderer.render(decision, Math.round(grid.audioTime() * SAMPLE_RATE), moment.barIndex, moment.beatIndex, moment.subdivision, sectionLabel);
        askedTypes.addLast(decision.queryType.name());
        while (askedTypes.size() > 8) askedTypes.removeFirst();
        askedAtMillis.addLast(System.currentTimeMillis());
        lastQuestionMillis = System.currentTimeMillis();
        holdingMusic = true;
        stage.show(CardStage.Content.QUESTION);
        if (about != null) {
            about.event(FeedbackRecord.proposed(id, decision, moment, whereInSong(grid), System.currentTimeMillis()), atSeconds);
            about.say("我", "问：" + question.question, atSeconds);
        }
        Log.i(TAG, "query asks: " + QueryDump.oneLine(atSeconds, moment, decision, "-"));
        main.post(() -> {
            if (card == null) return; card.show(question, choice -> {
                if (done[0]) return; done[0] = true; answersGiven++; recordWhereThePressLanded(about, "query_answer", System.currentTimeMillis()); if (about != null) {
                    about.event(FeedbackRecord.answered(id, choice.id, false, System.currentTimeMillis()), atSeconds); about.say("你", "答：" + choice.label, atSeconds);
                }
                dismissCount = 0; card.collapse(); card.clearQuestion(); stage.answered(); resizeCard(false); giveBackMusic(Asked.BY_US);
            }
); resizeCard(true); card.setVisibility(View.VISIBLE);
        }
);
        main.postDelayed(() -> {
            if (done[0] || card == null || !stage.awaitingAnswer() || !holdingMusic) return; done[0] = true; dismissCount++; Log.i(TAG, "query ignored; dismiss count is now " + dismissCount); if (about != null) {
                about.event(FeedbackRecord.answered(id, "", true, System.currentTimeMillis()), atSeconds); about.say("你", "没有回答", atSeconds);
            }
            stage.answered(); giveBackMusic(Asked.BY_US); card.collapse(); resizeCard(false);
        }
, SELF_ASK_PATIENCE_MILLIS);
    }
    

/** The song's beat grid, as the live analysis has it. */
    private ListeningAnalyzer.Grid gridFor(BeatDetector grid) {
        return new ListeningAnalyzer.Grid() {
            @Override public int positionInBar(double seconds) {
                int index = grid.positionAt(seconds);
                return index == Integer.MIN_VALUE ? -1 : Math.floorMod(index, grid.positionsPerBar());
            }
            @Override public int barAt(double seconds) {
                int index = grid.positionAt(seconds);
                return index == Integer.MIN_VALUE ? -1 : Math.floorDiv(index, grid.positionsPerBar());
            }
            @Override public int positionsPerBar() {
                return grid.positionsPerBar();
            }
            @Override public int subdivisionsPerBeat() {
                return BeatDetector.POSITIONS_PER_BEAT;
            }
        }
;
    }
    

/**
     * Bring up the local audio model, or say why there is not one.
  *
  * The model is a bypass: it is fed the same audio as everything else and
  * its observations are counted, but nothing in the capture, trigger or
  * question path waits on it. A model that is missing, that fails to load, or
  * that throws on its first clip leaves the app exactly as it was - which is
  * what makes it safe to put an unfinished model in the build.  */
    private void startModel() {
        ModelSpec spec = ModelHost.spec(this);
        ModelHost.Loaded loaded = ModelHost.open(this, spec);
        if (!loaded.ok()) {
            Log.i(TAG, "no audio model (" + loaded.failure + "); the rules carry on");
            onnx = null;
            return;
        }
        onnx = loaded.engine;
        onnxVersion = spec.path;
        modelSpec = spec;
        Log.i(TAG, String.format(java.util.Locale.US, "audio model ready in %dms: %s", loaded.loadMillis, spec));
    }
    

/** Feed the bypass engine and count what it says. */
    private void feedModel(short[] buffer, int read) {
        if (onnx == null) return;
        try {
            onnx.pushPcm(buffer, 0, read);
            for (MusicObservation observation : onnx.drainObservations()) {
                modelObservations++;
                lastTag = topTag(observation);
                if (observation.type == MusicObservation.Type.NOVELTY) {
                    lastNovelty = observation.confidence;
                    if (modelSpec != null) {
                        reading = StyleReading.of(observation.tags, modelSpec.tagNames, observation.confidence);
                    }
                    // Whether to act on that is a policy, and it lives in the
                    // gate; the asking itself happens in the capture loop, which
                    // still has the last seconds of audio to ask about.
                    if (observation.confidence >= NOTEWORTHY_NOVELTY) {
                        // Every one of these, not every tenth: the state line is
                        // printed every five seconds, which is one observation in
                        // ten, and a trigger tuned from a tenth of its evidence
                        // gets tuned to the wrong number.
                        Log.i(TAG, String.format(java.util.Locale.US, "[model] novelty %.2f at %.2fs (%s)", observation.confidence, observation.audioTimeSeconds, lastTag));
                    }
                    if (selfAsk.observe(observation.confidence, android.os.SystemClock.elapsedRealtime())) {
                        noticedSomething = true;
                        lastNoticedMillis = System.currentTimeMillis();
                    }
                }
                if (modelObservations <= 3) {
                    Log.i(TAG, String.format(java.util.Locale.US, "[model] %s %.2f at %.2fs %s", observation.type, observation.confidence, observation.audioTimeSeconds, lastTag));
                }
            }
        }
 catch (Throwable t) {
            // A model that throws is a model that is off, not a crash: the
            // capture thread has music to keep listening to.
            Log.w(TAG, "the audio model failed and is now off", t);
            try {
                onnx.close();
            }
 catch (Throwable ignored) {
            }
            onnx = null;
        }
    }
    

/**
     * What the model calls what it just heard.
  *
  * The tags are the model's own words and they travel with every
  * observation. Nothing acts on them yet - the novelty is what the trigger
  * will use - but they are the difference between "the model is running" and
  * "the model is hearing music", and that is worth being able to see.  */
    private String topTag(MusicObservation observation) {
        if (observation.tags == null || observation.tags.length == 0 || modelSpec == null) {
            return "-";
        }
        int best = 0;
        for (int i = 1; i < observation.tags.length; i++) {
            if (observation.tags[i] > observation.tags[best]) best = i;
        }
        if (best >= modelSpec.tagNames.size()) return "-";
        return String.format(java.util.Locale.US, "%s %.2f", modelSpec.tagNames.get(best), observation.tags[best]);
    }
    private String modelTopTag() {
        return lastTag;
    }
    private long modelObservations;
    private String lastTag = "-";
    

/** How unlike the recent past the model finds what it is hearing, right now. */
    private double lastNovelty;
    private ModelSpec modelSpec;
    private File listeningDirectory() {
        File dir = getExternalFilesDir("listening");
        if (dir != null && !dir.exists()) dir.mkdirs();
        return dir;
    }
    

/**
     * Ask the system to bind the media watcher again.
  *
  * Notification access being granted is not the same as the service being
  * bound: MIUI grants access on install and then refuses the binding until
  * the app is allowed to autostart. The request is cheap, so it is repeated
  * while the player is unreadable, and the app heals itself the first time
  * the system agrees - which is what happened on the previous project.  */
    private void requestMediaAccess() {
        try {
            android.service.notification.NotificationListenerService.requestRebind( new android.content.ComponentName(this, MediaWatcher.class));
            Log.i(TAG, "asked the system to bind the media watcher again");
        }
 catch (Exception e) {
            Log.w(TAG, "rebind request failed", e);
        }
    }
    private int dp(int value) {
        return Math.round(getResources().getDisplayMetrics().density * value);
    }
    private static final int SAMPLE_RATE = 44100;
}
