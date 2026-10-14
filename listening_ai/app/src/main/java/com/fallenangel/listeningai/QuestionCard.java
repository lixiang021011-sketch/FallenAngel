package com.fallenangel.listeningai;

import android.content.Context;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewConfiguration;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.fallenangel.listeningai.listening.PauseQuestion;

/**
 * The app's face: a small button, which opens into a question.
 *
 * It never disappears. An earlier version hid the whole window until there was
 * something to ask, which is tidy and unusable: with nothing on screen there is
 * no way to tell a listening app from a stopped one, and no way to reach it when
 * something *was* said. So the resting state is one small button - it says
 * whether music is being heard, opens on a tap to repeat what was last said, and
 * grows into the question when there is one.
 *
 * The button is also the only handle the listener has on the app while music is
 * playing, and the only thing there is to press when nothing is being asked.
 */
public final class QuestionCard extends LinearLayout {
    /** What the listener picked, or null when a message was dismissed. */
    public interface Listener {
        void onAnswer(PauseQuestion.Candidate choice);
    }

    /** What a tap-tempo session reports: one tap, and the end of the attempt. */
    public interface TapListener {
        /** The bar head: the first beat of a bar. */
        void onDownbeat();

        /** Any other beat. */
        void onBeat();

        void onFinish();
    }

    /**
     * Moving the window, driven by the touches on it.
     *
     * The window belongs to the system rather than to the view, so only the
     * service that added it can move it - the card reports the drag and the
     * service does the moving.
     */
    public interface DragListener {
        void onDragStart();

        void onDragBy(float dx, float dy);

        void onDragEnd();
    }

    private final LinearLayout pill;
    private final TextView pillSong;
    private final TextView pillStatus;
    /**
     * The chord, in its own small box.
     *
     * It started inside the remarks, which is the wrong home for it: a remark is
     * an event - something the app noticed and said once - and a chord is a state
     * that is true the whole time it is playing. In a remark it could only ever be
     * stale, or absent, or read as something that had just happened.
     */
    private final LinearLayout chordHolder;
    private final TextView pillChord;
    private final TextView pillChordPrev;
    private final TextView pillChordSource;
    private final Button pillTap;
    private final LinearLayout body;
    private final TextView title;
    private final LinearLayout choices;
    private PauseQuestion question;
    private Runnable onOpen;
    private String lastSaid = "";
    private DragListener dragListener;
    private final int touchSlop;
    private float downRawX, downRawY;
    private boolean dragging;

    public QuestionCard(Context context) {
        super(context);
        setOrientation(VERTICAL);
        setGravity(Gravity.START);

        // Two layers, because they are two different kinds of thing: what is
        // playing, and what the app makes of it. The name is the only line here
        // the app did not invent, so it gets its own; everything the app measures
        // stays underneath, where it was.
        // The resting state is one row of two things: what is playing, and the
        // one action a listener is likely to want while it plays. The tap button
        // is here rather than behind a panel because calibrating happens *during*
        // the music - a button two taps away is a button nobody uses in time.
        pill = new LinearLayout(context);
        pill.setOrientation(HORIZONTAL);
        pill.setGravity(Gravity.CENTER_VERTICAL);
        pill.setPadding(dp(14), dp(6), dp(8), dp(6));
        pill.setMinimumWidth(dp(150));
        pill.setBackground(background(true));

        LinearLayout pillInfo = new LinearLayout(context);
        pillInfo.setOrientation(VERTICAL);
        pillInfo.setPadding(0, dp(2), dp(10), dp(2));
        pillInfo.setOnClickListener(v -> {
            if (onOpen != null) onOpen.run();
        });
        pill.addView(pillInfo);

        pillSong = new TextView(context);
        pillSong.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        pillSong.setTextColor(Color.WHITE);
        pillSong.setSingleLine(true);
        pillSong.setEllipsize(android.text.TextUtils.TruncateAt.END);
        pillSong.setVisibility(GONE);
        pillInfo.addView(pillSong);

        pillStatus = new TextView(context);
        pillStatus.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        pillStatus.setTextColor(0xFFC8D0E0);
        pillStatus.setText("在听");
        pillStatus.setSingleLine(true);
        pillInfo.addView(pillStatus);

        // Two lines in the one box: what it is now, and what it was before. A
        // listener reading a chord wants the movement, not the state - "Ⅳ after Ⅰ"
        // is the thing that was heard, and a single line makes them remember what
        // the last one was.
        chordHolder = new LinearLayout(context);
        chordHolder.setOrientation(VERTICAL);
        chordHolder.setPadding(dp(7), dp(3), dp(7), dp(3));
        GradientDrawable box = new GradientDrawable();
        box.setColor(0x33FFFFFF);
        box.setCornerRadius(dp(6));
        box.setStroke(dp(1), 0x66FFFFFF);
        chordHolder.setBackground(box);
        chordHolder.setVisibility(GONE);

        pillChord = new TextView(context);
        pillChord.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        pillChord.setTextColor(0xFFE8ECF6);
        pillChord.setSingleLine(true);
        chordHolder.addView(pillChord);

        pillChordPrev = new TextView(context);
        pillChordPrev.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
        pillChordPrev.setTextColor(0xFF8C94A6);
        pillChordPrev.setSingleLine(true);
        pillChordPrev.setVisibility(GONE);
        chordHolder.addView(pillChordPrev);

        // Its own line, and short. Appended to the chord it was clipped away: the
        // line is already as wide as the window, and a single-line view does not
        // wrap what does not fit - it just ends. Measured 2026-10-13: the cache was
        // being used and the word for it was not on screen.
        pillChordSource = new TextView(context);
        pillChordSource.setTextSize(TypedValue.COMPLEX_UNIT_SP, 10);
        pillChordSource.setTextColor(0xFF7EE0A8);
        pillChordSource.setSingleLine(true);
        pillChordSource.setVisibility(GONE);
        chordHolder.addView(pillChordSource);

        LinearLayout.LayoutParams chordParams = new LinearLayout.LayoutParams(
                LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT);
        chordParams.topMargin = dp(4);
        pillInfo.addView(chordHolder, chordParams);

        // The widths are capped on the lines rather than the block: the block
        // wraps its contents, and a title longer than the cap is cut instead of
        // widening the window over the music.
        pillSong.setMaxWidth(dp(260));
        pillStatus.setMaxWidth(dp(260));
        pillTap = new Button(context);
        pillTap.setText("对拍");
        pillTap.setAllCaps(false);
        pillTap.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        pillTap.setTextColor(Color.WHITE);
        pillTap.setBackground(chip());
        pillTap.setPadding(dp(14), dp(6), dp(14), dp(6));
        pillTap.setMinWidth(0);
        pillTap.setMinimumWidth(0);
        pillTap.setOnClickListener(v -> {
            if (onTapTempo != null) onTapTempo.run();
        });
        pill.addView(pillTap);
        addView(pill);
        touchSlop = ViewConfiguration.get(context).getScaledTouchSlop();

        body = new LinearLayout(context);
        body.setOrientation(VERTICAL);
        body.setPadding(dp(14), dp(12), dp(14), dp(12));
        body.setBackground(background(false));
        addView(body);

        title = new TextView(context);
        title.setTextColor(Color.WHITE);
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        title.setPadding(0, 0, 0, dp(8));
        body.addView(title);

        choices = new LinearLayout(context);
        choices.setOrientation(VERTICAL);
        body.addView(choices);

        collapse();
    }

    /** What the button says while there is nothing to ask. */
    public void setPillText(String text) {
        pillStatus.setText(text);
    }

    /**
     * The same, with one run of it in another colour.
     *
     * Used for the tempo when the grid came from a file rather than from tracking:
     * the number looks identical either way, and the difference is whether it is a
     * guess that drifts or a value somebody worked out in advance. A colour is the
     * smallest way to say that - it costs no line, and the line is where the music
     * also has to fit.
     */
    public void setPillText(String text, int colourFrom, int colourTo, boolean highlighted) {
        String clean = text == null ? "" : text;
        if (!highlighted || colourFrom < 0 || colourTo <= colourFrom || colourTo > clean.length()) {
            pillStatus.setText(clean);
            return;
        }
        android.text.SpannableString span = new android.text.SpannableString(clean);
        span.setSpan(new android.text.style.ForegroundColorSpan(0xFF7EE0A8),
                colourFrom, colourTo, android.text.Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        pillStatus.setText(span);
    }

    /**
     * The song, on its own line.
     *
     * Allowed to be cut off: a title is usually longer than a floating button,
     * and a name that is half visible is worth more than a button that covers
     * the music. Empty hides the line entirely, which is what a phone that
     * cannot read the player looks like.
     */
    public void setPillSong(String song) {
        String name = song == null ? "" : song.trim();
        pillSong.setText(name);
        pillSong.setVisibility(name.isEmpty() ? GONE : VISIBLE);
    }

    /**
     * What the harmony is right now, or "" when the app cannot say.
     *
     * Called as often as the reading changes; the box is hidden rather than shown
     * empty, because an empty box on a floating window reads as a broken one.
     */
    public void setChord(String reading, String before, String source) {
        String now = reading == null ? "" : reading.trim();
        String previous = before == null ? "" : before.trim();
        // Whether this is a known song being replayed or the app working it out now
        // is not a detail a listener should have to open logcat to find: the two look
        // identical on screen and mean very different things about what they are
        // hearing.
        pillChord.setText(now);
        // And where it came from is worth a word whenever it is not a measurement:
        // "缓存" is this device's previous listen, "文件" is the Workbench's own track
        // for the song, and the two are as different as a guess and an answer.
        String where = now.isEmpty() || source == null ? "" : source;
        pillChordSource.setText(where);
        pillChordSource.setVisibility(where.isEmpty() ? GONE : VISIBLE);
        boolean hasPrevious = !previous.isEmpty() && !previous.equals(now);
        pillChordPrev.setText(hasPrevious ? "← " + previous : "");
        pillChordPrev.setVisibility(hasPrevious ? VISIBLE : GONE);
        chordHolder.setVisibility(now.isEmpty() ? GONE : VISIBLE);
    }

    /** What a tap on the resting button does. */
    public void setOnOpen(Runnable action) {
        onOpen = action;
    }

    /** The button on the resting row that starts a tap-tempo attempt. */
    public void setOnTapTempo(Runnable action) {
        onTapTempo = action;
    }

    private Runnable onTapTempo;

    /**
     * A bar just started.
     *
     * One flash on beat one, the way a metronome's first tick is a different
     * sound from the rest. Short and identical every time on purpose: anything
     * longer becomes something to read, and a flash you have to interpret is not
     * a beat any more. Nothing else about the window changes, so the flash cannot
     * be confused with the app having something to say.
     */
    public void flash() {
        if (pill == null) return;
        // Both surfaces, because only one of them is on screen at a time: the pill
        // is hidden while a question is open, and a bar line that stops flashing
        // whenever the app says something is a bar line nobody can follow.
        if (body != null) body.setBackground(background(false, true));
        pill.setBackground(background(true, true));
        pill.postDelayed(() -> {
            if (pill != null) pill.setBackground(background(true, false));
            if (body != null) body.setBackground(background(false, false));
        }, 90);
    }

    public void setDragListener(DragListener listener) {
        dragListener = listener;
    }

    /**
     * A press that moves is a drag; a press that does not is a press.
     *
     * Intercepting only after the finger has travelled means the choices still
     * work - a tap has to reach the button it landed on - while a drag can start
     * anywhere on the window, including on top of one.
     */
    @Override
    public boolean onInterceptTouchEvent(MotionEvent event) {
        switch (event.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                downRawX = event.getRawX();
                downRawY = event.getRawY();
                dragging = false;
                // A press that starts on a button belongs to that button: a
                // window is dragged by its chrome, not by its controls. Without
                // this, a finger that slides a few pixels while tapping counts as
                // a drag, the press is swallowed, and fast tapping registers as
                // one tap in five - which is what "点了好几下只算一下" was.
                downOnButton = onButton(event.getX(), event.getY());
                break;
            case MotionEvent.ACTION_MOVE:
                if (!downOnButton
                        && (Math.abs(event.getRawX() - downRawX) > touchSlop
                        || Math.abs(event.getRawY() - downRawY) > touchSlop)) {
                    dragging = true;
                    beginDrag();
                    return true;
                }
                break;
            default:
                break;
        }
        return false;
    }

    /** Whether one of the card's buttons is under this point, in its own coordinates. */
    private boolean onButton(float x, float y) {
        if (pillTap != null && hits(pillTap, pill, x, y)) return true;
        return hits(choices, body, x, y);
    }

    /**
     * Whether a point falls inside a view, given an ancestor to measure against.

     * A hit rectangle is expressed in the parent's coordinates, so the offsets of
     * every level between the two have to come off it before comparing with a
     * point that the card received.
     */
    private static boolean hits(View target, View ancestor, float x, float y) {
        if (target == null || ancestor == null) return false;
        if (target.getVisibility() != VISIBLE || ancestor.getVisibility() != VISIBLE) return false;
        android.graphics.Rect rect = new android.graphics.Rect();
        target.getHitRect(rect);
        View parent = (View) target.getParent();
        while (parent != null) {
            rect.offset(-parent.getLeft(), -parent.getTop());
            if (parent == ancestor) break;
            parent = (View) parent.getParent();
        }
        return parent == ancestor && rect.contains((int) x, (int) y);
    }

    private boolean downOnButton;

    @Override
    public boolean onTouchEvent(MotionEvent event) {
        if (!dragging) return super.onTouchEvent(event);
        switch (event.getActionMasked()) {
            case MotionEvent.ACTION_MOVE:
                if (dragListener != null) {
                    dragListener.onDragBy(event.getRawX() - downRawX,
                            event.getRawY() - downRawY);
                }
                return true;
            case MotionEvent.ACTION_UP:
            case MotionEvent.ACTION_CANCEL:
                dragging = false;
                if (dragListener != null) dragListener.onDragEnd();
                return true;
            default:
                return true;
        }
    }

    private void beginDrag() {
        if (dragListener != null) dragListener.onDragStart();
    }

    /** The card, folded down to its button. */
    public void collapse() {
        pill.setVisibility(VISIBLE);
        body.setVisibility(GONE);
    }

    public boolean collapsed() {
        return body.getVisibility() != VISIBLE;
    }

    /** The question on screen, or null when what is showing is a message. */
    public PauseQuestion question() {
        return question;
    }

    /** Fill the body for a question and open it. */
    public void show(PauseQuestion asked, Listener listener) {
        show(asked, "", listener);
    }

    /**
     * The same, with a line above the choices.
     *
     * The line is for what the app knows about this moment that is not a choice
     * to make - today, that the listener and the model stopped on it together.
     */
    public void show(PauseQuestion asked, String note, Listener listener) {
        question = asked;
        lastSaid = asked.question;
        title.setText(asked.question);
        choices.removeAllViews();
        addNote(note);
        for (PauseQuestion.Candidate candidate : asked.candidates) {
            choices.addView(choice(label(candidate), () -> {
                if (listener != null) listener.onAnswer(candidate);
            }), new LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT));
        }
        expand();
    }

    /**
     * The app saying something about the music, without asking anything.
     *
     * Apart from {@link #showMessage} on purpose: a message is the app explaining
     * itself - why there is no question this time, what it is doing - and a remark
     * is the app talking about the music. Only the remark and the question carry
     * the "too much" button, because those are the two things a listener could
     * want less of, and they are the two the tally counts apart.
     */
    public void showRemark(String text, String detail, String dismiss, Listener listener) {
        question = null;
        titleText(text);
        choices.removeAllViews();
        addNote(detail);
        choices.addView(choice(dismiss, () -> {
            if (listener != null) listener.onAnswer(null);
        }), new LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT));
        expand();
    }

    /**
     * Say something without asking anything.
     *
     * Used for the reasons there is no question this time. The app says why it
     * is quiet; it does not just go quiet.
     */
    public void showMessage(String text, String detail, String button, Listener listener) {
        question = null;
        title.setText(text);
        lastSaid = detail == null || detail.isEmpty() ? text : text + " " + detail;
        choices.removeAllViews();
        addNote(detail);
        if (button == null || button.isEmpty()) {
            expand();
            return;
        }
        choices.addView(choice(button, () -> {
            if (listener != null) listener.onAnswer(null);
        }), new LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT));
        expand();
    }

    /** A line with nothing to press, for the moment between a pause and an answer. */
    public void showBusy(String text) {
        showMessage(text, "", "", null);
    }

    /**
     * What the model is hearing right now, and two buttons to judge it.
     *
     * This is the one interaction that does not wait for anything: the listener
     * can look at the app whenever they like and say whether it is right. A
     * wrong tag is as useful as a right one - more, even, since it is the
     * disagreement that says where a tagger and a listener part ways.
     */
    public void showStyle(String title, String detail, Runnable onTapTempo,
                          Runnable onShiftBar, Runnable onDismiss) {
        question = null;
        titleText(title);
        choices.removeAllViews();
        addNote(detail);
        add(choice("对拍子", () -> {
            if (onTapTempo != null) onTapTempo.run();
        }));
        add(choice("闪的不是第 1 拍", () -> {
            if (onShiftBar != null) onShiftBar.run();
        }));
        add(choice("知道了", () -> {
            if (onDismiss != null) onDismiss.run();
        }));
        expand();
    }

    /**
     * A question with its own answers.

     * For the things the app cannot decide and a person can - today, whether the
     * beat it is counting is still the beat they are following. The answers are
     * not judgements of the past, they are instructions about now.
     */
    public void ask(String title, String detail, String[] labels, Runnable[] actions) {
        question = null;
        titleText(title);
        choices.removeAllViews();
        addNote(detail);
        for (int i = 0; i < labels.length && i < actions.length; i++) {
            Runnable action = actions[i];
            add(choice(labels[i], action));
        }
        expand();
    }

    /**
     * Tap mode: one button that is the instrument, and a count of what it has.
     *
     * The button has to be big and it has to be the same button every time -
     * tapping in time with music is a rhythm action, and a target that moves or
     * that has to be found again between beats cannot be tapped in time.
     */
    public void showTapTempo(String title, String detail, TapListener listener) {
        question = null;
        titleText(title);
        choices.removeAllViews();
        addNote(detail);
        add(choice("小节头（每小节第 1 拍）", () -> {
            if (listener != null) listener.onDownbeat();
        }));
        add(choice("其余正拍", () -> {
            if (listener != null) listener.onBeat();
        }));
        tapButton = choice("0 下", () -> {
        });
        tapButton.setEnabled(false);
        tapButton.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        add(tapButton);
        add(choice("完成", () -> {
            if (listener != null) listener.onFinish();
        }));
        expand();
    }

    /** What the tap button says while the listener is tapping. */
    public void setTapProgress(int presses, double beatMillis, double beatBpm, int meter) {
        if (tapButton == null) return;
        StringBuilder text = new StringBuilder("已按 ").append(presses).append(" 下");
        if (beatMillis > 0) {
            text.append(String.format(java.util.Locale.US, " · 约 %.0f", beatBpm));
        }
        if (meter > 0) text.append(String.format(java.util.Locale.US, " · %d 拍一小节", meter));
        tapButton.setText(text.toString());
    }

    private Button tapButton;

    /**
     * One button, in this app's clothes rather than the system's.
     *
     * The card is dark and every button on it used to be a system button:
     * light grey with dark text, which is the loudest thing on the window and
     * the only part of it that does not look like the app. One method so that
     * the four places buttons are made cannot drift apart again.
     */
    private Button choice(String label, Runnable action) {
        Button button = new Button(getContext());
        button.setText(label);
        button.setAllCaps(false);
        button.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        button.setTextColor(Color.WHITE);
        button.setBackground(chip());
        button.setPadding(dp(12), dp(6), dp(12), dp(6));
        // A null action is a caller that means to fill the listener in itself -
        // styling a button it is about to wire, rather than one that does nothing.
        if (action != null) button.setOnClickListener(v -> action.run());
        return button;
    }

    private void add(View view) {
        choices.addView(view, new LayoutParams(
                LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT));
    }

    private android.graphics.drawable.Drawable chip() {
        GradientDrawable shape = new GradientDrawable();
        shape.setColor(0x33FFFFFF);
        shape.setCornerRadius(dp(14));
        shape.setStroke(dp(1), 0x66FFFFFF);
        // Pressed state: without it a button that does not obviously move looks
        // like it did not hear the tap.
        return new android.graphics.drawable.RippleDrawable(
                android.content.res.ColorStateList.valueOf(0x55FFFFFF), shape, null);
    }

    /** A grey line of explanation above the buttons. */
    private void addNote(String detail) {
        if (detail == null || detail.isEmpty()) return;
        choices.addView(noteView(detail));
    }

    private TextView noteView(String detail) {
        TextView note = new TextView(getContext());
        note.setText(detail);
        note.setTextColor(0xFF99A2B8);
        note.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        note.setPadding(0, 0, 0, dp(8));
        return note;
    }

    /**
     * Nothing is being asked any more.
     *
     * Answering only hid the question, which left the card insisting it still had
     * one - and the button, which asks the card whether anything is pending,
     * reopening the question that had just been answered.
     */
    public void clearQuestion() {
        question = null;
        choices.removeAllViews();
    }

    /** Set the title and remember it as the thing a later tap repeats. */
    private void titleText(String text) {
        title.setText(text);
        lastSaid = text;
    }

    /** Open, showing whatever was said last if nothing is pending. */
    public void open() {
        if (body.getVisibility() == VISIBLE) return;
        if (question == null && choices.getChildCount() == 0) {
            showMessage("还没有要问的。", lastSaid, "知道了", choice -> collapse());
            return;
        }
        expand();
    }

    private void expand() {
        pill.setVisibility(GONE);
        body.setVisibility(VISIBLE);
    }

    private String label(PauseQuestion.Candidate candidate) {
        if (candidate.evidence.isEmpty()) return candidate.label;
        return candidate.label + "   (" + candidate.evidence + ")";
    }

    private GradientDrawable background(boolean small) {
        return background(small, false);
    }

    /** The same shape, optionally lit up for a bar line. */
    private GradientDrawable background(boolean small, boolean lit) {
        GradientDrawable shape = new GradientDrawable();
        shape.setColor(Color.parseColor(lit ? "#CC3A4A66"
                : (small ? "#CC10141C" : "#EE10141C")));
        shape.setCornerRadius(dp(small ? 18 : 12));
        shape.setStroke(dp(1), Color.parseColor(lit ? "#AAFFFFFF" : "#55FFFFFF"));
        return shape;
    }

    private int dp(int value) {
        return Math.round(getResources().getDisplayMetrics().density * value);
    }
}
