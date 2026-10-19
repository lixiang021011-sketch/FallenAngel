package com.fallenangel.overlaybeat;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Path;
import android.util.AttributeSet;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewConfiguration;
import android.view.HapticFeedbackConstants;

import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;
import java.util.Locale;

/**
 * The floating panel: one lane, notes falling to a judgment line.
 *
 * Sized to be a narrow strip rather than a screen, so it can sit over a music
 * player without hiding it, and styled like the attention annotator - same dark
 * rounded panel, same hairline border, same accent colour - so the two tools
 * feel like one family.
 *
 * The panel owns every touch event, exactly like the annotator's pad: a press
 * that travels is a drag of the window, a press that stays put is a hit. The
 * lane is the whole target, because this is meant to be playable without
 * looking.
 *
 * Notes are placed by BeatDetector; LEAD_SECONDS is how much warning the player
 * gets, which is also the entire reason the note source is a locked beat grid
 * rather than detected onsets.
 */
public class GameView extends View {
    private static final double PERFECT_SECONDS = 0.07;
    private static final double GOOD_SECONDS = 0.14;

    private static final float PANEL_WIDTH_DP = 104;
    private static final float PANEL_HEIGHT_DP = 384;

    private static final int COLOR_PANEL = 0xE60B0D12;
    private static final int COLOR_BORDER = 0xFF2A3143;
    private static final int COLOR_NOTE = 0xFF6EE7B7;
    private static final int COLOR_CONFIRMED = 0xFFFFD43B;
    private static final int COLOR_TEXT = 0xFFEEF1F7;
    private static final int COLOR_DIM = 0xFF99A2B8;
    private static final int COLOR_MISS = 0xFFFF6B6B;

    public interface DragListener {
        void onDragStart();

        void onDragBy(float dx, float dy);

        void onDragEnd();
    }

    private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Path wavePath = new Path();
    private final List<ChartNote> live = new ArrayList<>();
    private final List<Judged> history = new ArrayList<>();
    private final List<Hit> hits = new ArrayList<>();
    private final float[] wave = new float[120];
    private final int slop;

    private DragListener dragListener;
    private float downX;
    private float downY;
    private long downAt;
    private boolean dragging;

    private int waveFill;
    private double now;
    private int score;
    private int combo;
    private int bestCombo;
    private int perfectCount;
    private int goodCount;
    private int missCount;
    /** Whether the song was still being taught at the last frame. */
    private boolean wasTeaching;
    /**
     * The cached song's own drums, when this song has been heard before.
     *
     * Null means the chart is being predicted and the lane draws bricks. A
     * waveform means the whole song was analysed before it started playing.
     */
    private float[] waveform;
    private final android.graphics.Paint waveformPaint =
            new android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG);

    /** Play the lane as the song's drums. Null puts the bricks back. */
    public void setWaveform(float[] envelope) {
        this.waveform = envelope;
    }
    private float flash;
    private int flashColor = COLOR_NOTE;
    private double comboPoppedAt = -10;
    private String titleShown = "";
    private double titleShownAt;
    private String lastTrackKey = "";
    private long seenCalibrationGeneration;

    /** A hit landing on the judgment line: what feedback is drawn from. */
    private static final class Hit {
        final double time;
        final int color;
        final boolean perfect;

        Hit(double time, int color, boolean perfect) {
            this.time = time;
            this.color = color;
            this.perfect = perfect;
        }
    }

    private static final class Judged {
        final double time;
        final String label;
        final int color;

        Judged(double time, String label, int color) {
            this.time = time;
            this.label = label;
            this.color = color;
        }
    }

    public GameView(Context context) {
        super(context);
        slop = ViewConfiguration.get(context).getScaledTouchSlop();
        init();
    }

    public GameView(Context context, AttributeSet attrs) {
        super(context, attrs);
        slop = ViewConfiguration.get(context).getScaledTouchSlop();
        init();
    }

    private void init() {
        paint.setStrokeCap(Paint.Cap.ROUND);
        paint.setTextAlign(Paint.Align.CENTER);
        setElevation(dp(6));
    }

    public void setDragListener(DragListener listener) {
        this.dragListener = listener;
    }

    /** Shared with the app screen so both see the same calibration. */
    public int visualDelayMs() {
        return CaptureService.visualDelayMs;
    }

    public void setVisualDelayMs(int delayMs) {
        CaptureService.visualDelayMs = Math.max(-300, Math.min(300, delayMs));
    }

    public int score() {
        return score;
    }

    public int combo() {
        return combo;
    }

    @Override
    protected void onMeasure(int widthSpec, int heightSpec) {
        setMeasuredDimension(Math.round(dp(PANEL_WIDTH_DP)), Math.round(dp(PANEL_HEIGHT_DP)));
    }

    // -------------------------------------------------------------- game logic

    public void update(BeatDetector detector, double audioTime) {
        now = audioTime;
        if (detector != null) {
            live.addAll(CaptureService.chartBuilder.drain());
            live.addAll(CaptureService.chartPlayer.drain());
            wave[waveFill % wave.length] = (float) detector.latestStrength();
            waveFill++;
        }
        double cutoff = now - GOOD_SECONDS;
        Iterator<ChartNote> iterator = live.iterator();
        while (iterator.hasNext()) {
            ChartNote note = iterator.next();
            if (note.time < cutoff) {
                iterator.remove();
                missCount++;
                combo = 0;
                history.add(new Judged(note.time, "MISS", COLOR_MISS));
            }
        }
        if (flash > 0) flash = Math.max(0, flash - 0.07f);
    }

    /**
     * A tap on the panel: judge the closest note inside the window, and learn
     * how the player's timing sits relative to the notes.
     *
     * The running median of (tap time - note time) is the device's total delay:
     * if the player is consistently 80 ms late, the notes are being shown 80 ms
     * too early for them, and the whole lane should move. Fitting it from play
     * beats asking for a number nobody can feel.
     */
    public void tap() {
        double delay = CaptureService.visualDelayMs / 1000.0;
        ChartNote best = null;
        double bestDelta = Double.MAX_VALUE;
        ChartNote nearestByRaw = null;
        double nearestRaw = Double.MAX_VALUE;
        for (ChartNote note : live) {
            double delta = Math.abs(note.time + delay - now);
            if (delta < bestDelta) {
                bestDelta = delta;
                best = note;
            }
            double raw = Math.abs(note.time - now);
            if (raw < nearestRaw) {
                nearestRaw = raw;
                nearestByRaw = note;
            }
        }

        // Calibration is measured against the note's own time, with a window that
        // does not move with the offset. Measuring inside the offset-shifted
        // window was a trap: once the value was wrong every tap fell outside it,
        // the learner got no more evidence, and the wrong value became
        // permanent - which is exactly how every song stopped working.
        if (nearestByRaw != null && nearestRaw < 0.4) {
            tapError[tapErrorIndex % tapError.length] = now - nearestByRaw.time;
            tapErrorIndex++;
            tapErrorCount = Math.min(tapErrorCount + 1, tapError.length);
            recalibrate();
        }

        if (best == null || bestDelta > GOOD_SECONDS) {
            flash = 1f;
            flashColor = COLOR_MISS;
            return;
        }
        live.remove(best);
        if (bestDelta <= PERFECT_SECONDS) {
            score += 100 + combo * 2;
            perfectCount++;
            flashColor = COLOR_NOTE;
            history.add(new Judged(best.time, "PERFECT", COLOR_NOTE));
            hits.add(new Hit(now, COLOR_NOTE, true));
            performHapticFeedback(HapticFeedbackConstants.CONFIRM);
        } else {
            score += 50 + combo;
            goodCount++;
            flashColor = COLOR_CONFIRMED;
            history.add(new Judged(best.time, "GOOD", COLOR_CONFIRMED));
            hits.add(new Hit(now, COLOR_CONFIRMED, false));
            performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY);
        }
        combo++;
        comboPoppedAt = now;
        bestCombo = Math.max(bestCombo, combo);
        flash = 1f;
    }

    private static final int CALIBRATION_SAMPLES = 24;
    private final double[] tapError = new double[CALIBRATION_SAMPLES];
    private int tapErrorIndex;
    private int tapErrorCount;

    /** Move the lane to where the player's hands actually are. */
    private void recalibrate() {
        if (CaptureService.offsetFrozen || tapErrorCount < 12) return;
        double[] sorted = new double[tapErrorCount];
        System.arraycopy(tapError, 0, sorted, 0, tapErrorCount);
        java.util.Arrays.sort(sorted);
        double median = sorted[sorted.length / 2];
        int target = (int) Math.round(median * 1000);
        target = Math.max(-200, Math.min(200, target));
        // One decision from a run of taps, and then the value is settled: it is
        // whatever those twelve taps said, and it is never revised again by
        // whatever the player happens to do for the rest of the song.
        CaptureService.visualDelayMs = target;
        CaptureService.offsetFrozen = true;
        SongMemory.saveDelayMs(getContext(), CaptureService.visualDelayMs);
    }

    /** Start the fit clean when the track changes; the old samples may be junk. */
    public void forgetCalibration() {
        tapErrorCount = 0;
        tapErrorIndex = 0;
    }

    // ---------------------------------------------------------------- gestures

    @Override
    public boolean onTouchEvent(MotionEvent event) {
        switch (event.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                downX = event.getRawX();
                downY = event.getRawY();
                downAt = event.getEventTime();
                dragging = false;
                return true;

            case MotionEvent.ACTION_MOVE:
                if (!dragging
                        && (Math.abs(event.getRawX() - downX) > slop
                        || Math.abs(event.getRawY() - downY) > slop)) {
                    dragging = true;
                    if (dragListener != null) dragListener.onDragStart();
                }
                if (dragging && dragListener != null) {
                    dragListener.onDragBy(event.getRawX() - downX, event.getRawY() - downY);
                }
                return true;

            case MotionEvent.ACTION_UP:
                if (dragging) {
                    if (dragListener != null) dragListener.onDragEnd();
                } else {
                    BeatDetector detector = CaptureService.detector;
                    if (detector != null && detector.tapTempoActive()) {
                        // Calibrating: every tap is a beat, not a hit.
                        if (detector.onTapTempo(detector.audioTime())) {
                            // A tapped tempo is knowledge; keep it for this song.
                            SongMemory.saveBpm(getContext(), Playback.trackKey, detector.bpm());
                            // The grid just moved, so any latency fit taken
                            // against the old grid is worthless: take a new one.
                            forgetCalibration();
                            CaptureService.offsetFrozen = false;
                        }
                    } else if (inTempoZone(event.getX(), event.getY()) && detector != null) {
                        // Tap the readout to tap the beat; hold it to halve the
                        // tempo, which is the fix for an octave error.
                        if (event.getEventTime() - downAt > 500) detector.toggleHalfTempo();
                        else detector.beginTapTempo();
                    } else {
                        tap();
                    }
                }
                dragging = false;
                return true;

            case MotionEvent.ACTION_CANCEL:
                if (dragging && dragListener != null) dragListener.onDragEnd();
                dragging = false;
                return true;

            default:
                return true;
        }
    }

    private boolean inTempoZone(float x, float y) {
        return y < dp(30) && x > getWidth() - dp(68);
    }

    // ----------------------------------------------------------------- drawing

    @Override
    protected void onDraw(Canvas canvas) {
        pull();
        int width = getWidth();
        int height = getHeight();
        float laneX = width * 0.5f;
        float laneTop = dp(52);
        float judgeY = height - dp(52);
        float laneWidth = width - dp(24);

        paint.setStyle(Paint.Style.FILL);
        paint.setColor(COLOR_PANEL);
        canvas.drawRoundRect(0, 0, width, height, dp(18), dp(18), paint);

        drawLane(canvas, laneX, laneTop, judgeY, laneWidth);
        if (waveform == null) {
            drawNotes(canvas, laneX, laneTop, judgeY, laneWidth);
        } else {
            drawWaveform(canvas, laneX, laneTop, judgeY, laneWidth);
        }
        drawHits(canvas, laneX, judgeY);
        drawWave(canvas, width, height, judgeY);
        drawTitle(canvas, width);
        drawHeader(canvas, width);
        drawFooter(canvas, width, height, judgeY);
        drawJudgment(canvas, laneX, judgeY);

        paint.setStyle(Paint.Style.STROKE);
        paint.setStrokeWidth(dp(1));
        paint.setColor(COLOR_BORDER);
        canvas.drawRoundRect(dp(0.5f), dp(0.5f), width - dp(0.5f), height - dp(0.5f),
                dp(18), dp(18), paint);

        if (flash > 0) {
            paint.setStrokeWidth(dp(3));
            paint.setColor(flashColor);
            paint.setAlpha((int) (flash * 170));
            canvas.drawRoundRect(dp(1.5f), dp(1.5f), width - dp(1.5f), height - dp(1.5f),
                    dp(18), dp(18), paint);
            paint.setAlpha(255);
        }
        postInvalidateOnAnimation();
    }

    /** The view drives itself: it is the only thing that needs a steady frame. */
    private void pull() {
        BeatDetector detector = CaptureService.detector;
        if (detector == null) return;
        if (seenCalibrationGeneration != CaptureService.calibrationGeneration) {
            seenCalibrationGeneration = CaptureService.calibrationGeneration;
            forgetCalibration();
            CaptureService.offsetFrozen = false;
        }
        if (!Playback.trackKey.equals(lastTrackKey)) {
            lastTrackKey = Playback.trackKey;
            // The offset is a property of the device, not of the song, so it is
            // not re-learned for every track. It is invalidated here because a
            // track change re-acquires the grid - and a fit measured against the
            // old grid is a fit measured against the wrong notes.
            forgetCalibration();
            CaptureService.offsetFrozen = false;
        }
        if (!detector.locked() && !live.isEmpty()) {
            // The music stopped: drop the notes that were already falling.
            live.clear();
        }
        update(detector, detector.audioTime());
    }

    private void drawHeader(Canvas canvas, int width) {
        paint.setStyle(Paint.Style.FILL);
        paint.setTextAlign(Paint.Align.LEFT);
        paint.setColor(0xFFFF6B6B);
        canvas.drawCircle(dp(12), dp(34), dp(3.5f), paint);

        BeatDetector detector = CaptureService.detector;
        paint.setTextAlign(Paint.Align.RIGHT);
        paint.setColor(COLOR_DIM);
        paint.setTextSize(dp(10));
        String tempo = detector == null || !detector.locked()
                ? "--"
                : String.format(Locale.US, detector.halfTempo() ? "%.0f \u00bd" : "%.0f",
                        detector.displayBpm());
        canvas.drawText(tempo, width - dp(10), dp(38), paint);
        paint.setStrokeWidth(dp(1));
        paint.setColor(0x33FFFFFF);
        canvas.drawLine(width - dp(58), dp(44), width - dp(10), dp(44), paint);

        if (detector != null && detector.tapTempoActive()) {
            paint.setTextAlign(Paint.Align.CENTER);
            paint.setColor(0xFF6EE7B7);
            paint.setTextSize(dp(11));
            canvas.drawText("tap the beat " + detector.tapTempoCount() + "/4",
                    width * 0.5f, dp(60), paint);
        }
    }

    /**
     * The track the player picked, read from the media session.
     *
     * The panel is a narrow strip, so anything longer than a few words scrolls
     * once it has been readable for a moment - a title that is silently cut off
     * is worse than no title at all.
     */
    private void drawTitle(Canvas canvas, int width) {
        String title = Playback.trackTitle;
        if (title == null || title.isEmpty()) title = Playback.trackArtist;
        if (title == null || title.isEmpty()) return;
        if (!title.equals(titleShown)) {
            titleShown = title;
            titleShownAt = now;
        }

        paint.setStyle(Paint.Style.FILL);
        paint.setTextAlign(Paint.Align.LEFT);
        paint.setTextSize(dp(9));
        paint.setColor(COLOR_DIM);

        float left = dp(8);
        float available = width - dp(16);
        float textWidth = paint.measureText(title);
        canvas.save();
        canvas.clipRect(0, 0, width, dp(24));
        if (textWidth <= available) {
            canvas.drawText(title, left, dp(14), paint);
        } else {
            float speed = dp(16);
            double gap = dp(30);
            double cycleDistance = textWidth + gap;
            double cycleSeconds = cycleDistance / speed + 1.2;
            double elapsed = (now - titleShownAt) % cycleSeconds;
            float offset = elapsed < 1.2 ? 0 : (float) ((elapsed - 1.2) * speed);
            canvas.drawText(title, left - offset, dp(14), paint);
            canvas.drawText(title, left - offset + (float) cycleDistance, dp(14), paint);
        }
        canvas.restore();
    }

    private void drawLane(Canvas canvas, float laneX, float laneTop, float judgeY, float laneWidth) {
        paint.setStyle(Paint.Style.STROKE);
        paint.setStrokeWidth(dp(1));
        paint.setColor(0x22FFFFFF);
        canvas.drawLine(laneX - laneWidth / 2, laneTop, laneX - laneWidth / 2, judgeY, paint);
        canvas.drawLine(laneX + laneWidth / 2, laneTop, laneX + laneWidth / 2, judgeY, paint);

        paint.setStrokeWidth(dp(3));
        paint.setColor(0xFF39415A);
        canvas.drawLine(laneX - laneWidth / 2, judgeY, laneX + laneWidth / 2, judgeY, paint);
    }

    /**
     * The song's own drums, falling.
     *
     * One row of pixels is one moment of the song: the row on the judgement line
     * is what is playing now, and the rows above it are the next second and a
     * bit. Each row is as wide as the drums are loud there, so what comes down
     * the lane is the shape of the music rather than a row of bricks standing in
     * for it. The notes are still marked on top, thin, because the waveform is
     * what the eye should be reading.
     */
    private void drawWaveform(Canvas canvas, float laneX, float laneTop, float judgeY,
                              float laneWidth) {
        double songTime = CaptureService.chartPlayer.songTimeNow();
        float span = judgeY - laneTop;
        float half = laneWidth * 0.46f;
        if (songTime < 0 || span <= 0) return;
        float perRow = (float) (BeatDetector.TRAVEL_SECONDS / span);

        for (int row = 0; row < (int) span; row++) {
            float y = judgeY - row;
            double ahead = (double) row / span * BeatDetector.TRAVEL_SECONDS;
            int from = (int) ((songTime + ahead) * OfflineChart.ENVELOPE_RATE);
            int to = (int) ((songTime + ahead + perRow) * OfflineChart.ENVELOPE_RATE) + 1;
            float peak = 0;
            for (int i = Math.max(0, from);
                 i < Math.min(waveform.length, Math.max(to, from + 1)); i++) {
                peak = Math.max(peak, waveform[i]);
            }
            if (peak <= 0.02f) continue;
            waveformPaint.setColor(COLOR_NOTE);
            waveformPaint.setAlpha((int) (40 + 180 * Math.min(1, peak)));
            waveformPaint.setStrokeWidth(1.2f);
            canvas.drawLine(laneX - peak * half, y, laneX + peak * half, y, waveformPaint);
        }

        // The notes the chart asks for, as thin marks over the waveform.
        for (ChartNote note : live) {
            float y = judgeY - (float) ((note.time - now) / BeatDetector.TRAVEL_SECONDS) * span;
            if (y < laneTop - 2 || y > judgeY) continue;
            waveformPaint.setColor(note.accent ? COLOR_CONFIRMED : COLOR_NOTE);
            waveformPaint.setAlpha(255);
            waveformPaint.setStrokeWidth(dp(2));
            canvas.drawLine(laneX - half, y, laneX + half, y, waveformPaint);
        }
    }

    private void drawNotes(Canvas canvas, float laneX, float laneTop, float judgeY, float laneWidth) {
        paint.setStyle(Paint.Style.FILL);
        float span = judgeY - laneTop;
        float half = laneWidth * 0.44f;
        for (ChartNote note : live) {
            double remaining = note.time + CaptureService.visualDelayMs / 1000.0 - now;
            if (remaining > BeatDetector.TRAVEL_SECONDS) continue;
            float y = (float) (judgeY - remaining / BeatDetector.TRAVEL_SECONDS * span);
            paint.setColor(note.accent ? COLOR_CONFIRMED : COLOR_NOTE);
            paint.setAlpha(remaining < -GOOD_SECONDS * 0.5 ? 120 : 255);
            canvas.drawRoundRect(laneX - half, y - dp(5), laneX + half, y + dp(5),
                    dp(5), dp(5), paint);
            paint.setAlpha(255);
        }
    }

    /**
     * What a hit looks like: a ring expanding off the judgment line, bright for
     * a perfect and dimmer for a good. Both last about a quarter of a second, so
     * they register in peripheral vision without covering the lane.
     */
    private void drawHits(Canvas canvas, float laneX, float judgeY) {
        if (hits.isEmpty()) return;
        paint.setStyle(Paint.Style.STROKE);
        Iterator<Hit> iterator = hits.iterator();
        while (iterator.hasNext()) {
            Hit hit = iterator.next();
            double age = now - hit.time;
            if (age > 0.28 || age < 0) {
                iterator.remove();
                continue;
            }
            float progress = (float) (age / 0.28);
            float radius = dp(8) + progress * dp(26);
            paint.setColor(hit.color);
            paint.setStrokeWidth(dp(hit.perfect ? 3f : 2f));
            paint.setAlpha((int) (210 * (1 - progress)));
            canvas.drawCircle(laneX, judgeY, radius, paint);
            if (hit.perfect) {
                paint.setStrokeWidth(dp(1));
                paint.setAlpha((int) (120 * (1 - progress)));
                canvas.drawCircle(laneX, judgeY, radius * 0.55f, paint);
            }
        }
        paint.setAlpha(255);
    }

    private void drawWave(Canvas canvas, int width, int height, float judgeY) {
        float top = judgeY + dp(8);
        float bottom = height - dp(22);
        int count = Math.min(waveFill, wave.length);
        if (count < 2) return;
        wavePath.reset();
        for (int i = 0; i < count; i++) {
            int index = ((waveFill - count + i) % wave.length + wave.length) % wave.length;
            float value = Math.min(1f, wave[index] * 6f);
            float x = dp(10) + (width - dp(20)) * (float) i / (count - 1);
            float y = bottom - value * (bottom - top);
            if (i == 0) wavePath.moveTo(x, y);
            else wavePath.lineTo(x, y);
        }
        paint.setStyle(Paint.Style.STROKE);
        paint.setStrokeWidth(dp(1.5f));
        paint.setColor(0x776EE7B7);
        canvas.drawPath(wavePath, paint);
    }

    private void drawFooter(Canvas canvas, int width, int height, float judgeY) {
        paint.setStyle(Paint.Style.FILL);
        paint.setTextAlign(Paint.Align.LEFT);
        paint.setColor(COLOR_TEXT);
        paint.setTextSize(dp(15));
        canvas.drawText(String.valueOf(score), dp(10), height - dp(8), paint);

        paint.setTextAlign(Paint.Align.RIGHT);
        paint.setColor(combo > 1 ? COLOR_NOTE : COLOR_DIM);
        boolean popped = now - comboPoppedAt < 0.16;
        paint.setTextSize(dp(popped ? 15 : 11));
        canvas.drawText("x" + combo, width - dp(10), height - dp(9), paint);

        paint.setTextAlign(Paint.Align.CENTER);
        paint.setColor(COLOR_DIM);
        paint.setTextSize(dp(9));
        // "--" while the fit is still gathering taps, the value once it has
        // settled: without that, "learning" and "settled" look identical, which
        // is how a frozen wrong value went unnoticed.
        String offset = CaptureService.offsetFrozen
                ? String.format(Locale.US, "\u0394%+dms", CaptureService.visualDelayMs)
                : "\u0394--";
        canvas.drawText(offset,
                width * 0.5f, height - dp(9), paint);
    }

    private void drawJudgment(Canvas canvas, float laneX, float judgeY) {
        paint.setStyle(Paint.Style.FILL);
        paint.setTextAlign(Paint.Align.CENTER);
        paint.setTextSize(dp(11));
        int slot = 0;
        Iterator<Judged> iterator = history.iterator();
        while (iterator.hasNext()) {
            Judged judged = iterator.next();
            double age = now - judged.time;
            if (age > 0.45) {
                iterator.remove();
                continue;
            }
            paint.setColor(judged.color);
            paint.setAlpha((int) Math.max(0, 255 * (1 - age / 0.45)));
            canvas.drawText(judged.label, laneX, judgeY - dp(14) - slot * dp(13), paint);
            slot++;
        }
        paint.setAlpha(255);
    }

    private float dp(float value) {
        return value * getResources().getDisplayMetrics().density;
    }
}
