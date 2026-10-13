package com.fallenangel.listeningai.listening;

/**
 * What the chord box shows, which is not the same thing as what was just measured.
 *
 * Two rules, and neither of them is a cooldown.
 *
 * The first is about noise: a reading arrives every second and the top two chords
 * are often a hundredth
 * apart, so a box that followed the measurements would flick A → Am → C → Am
 * several times a second and be useless to read. So the reading has to be the same
 * this time as it was the last few times: a value that does not last never reaches
 * the screen.
 *
     * The second is about when a change is shown. A chord that swaps in the middle of
 * a beat reads as a jump; the same chord landing on a beat reads as the music having
 * moved. So the box changes only on a tick the caller marks as a beat - the caller's
 * choice, and it used to be the bar line, which was a whole bar of delay for a change
 * that happened on the line and was noticed just after it - and the caller marks
 * every tick when the grid is not up, because a box that stops working whenever the
 * beat is unknown is worse than one that is not quantised.
 *
 * There used to be a second rule - a reading had to beat the next one by a set
 * amount before it could replace what was up - and it was wrong by an order of
 * magnitude. On this app's own recordings the gap between the top two chords is
 * 0.005 to 0.008, and the rule asked for 0.03, so the box froze on the first chord
 * it ever showed and sat there while the readings underneath moved. Measured, on 说
 * 说Crystal — 月: reading "#F 调 ⅵ 级", shown "#F 调 Ⅰ 级", margin 0.006, for thirty
 * seconds. The margin is still worth knowing and is still written to the record; it
 * is just not a gate.
 *
 * This is the display's own cadence. Speaking is a different thing with a
 * different rhythm, and the two were allowed to share one number for exactly one
 * commit - a twenty-second gap is right for a sentence and wrong for a chord.
 *
 * Kept away from Android so the rule can be tried against a sequence of readings
 * instead of by listening to a song and watching a small box.
 */
public final class ChordDisplay {
    private final int needed;
    /**
     * Below this, two chords are level and the reading is a coin toss.
     *
     * On this app's own recordings the top two sit 0.002 to 0.008 apart most of the
     * time, so a reading that is not a near-tie is the exception - which is why the
     * old absolute gate froze the box, and why the fix is not another absolute
     * gate. A tie is not banned: it just has to persist much longer to be believed,
     * because a run of three coin tosses landing the same way is not evidence.
     */
    private static final double TIE_MARGIN = 0.010;
    private String pending = null;
    private int seen = 0;
    private String shown = "";

    /** @param needed how many readings in a row have to agree before it shows */
    public ChordDisplay(int needed) {
        this.needed = Math.max(1, needed);
    }

    /** What a listener would want: three seconds of agreement. */
    public static ChordDisplay standard() {
        return new ChordDisplay(3);
    }

    /**
     * @param reading the chord as the card prints it, or "" when nothing is readable
     * @return what to show now
     */
    public String consider(String reading) {
        return consider(reading, true);
    }

    /**
     * @param switchPoint whether this is a moment the box is allowed to change on -
     *                    a bar line, or any tick at all when there is no grid
     */
    public String consider(String reading, boolean switchPoint) {
        return consider(reading, 1.0, switchPoint);
    }

    /**
     * @param margin how far ahead of the next chord this reading is; a near-tie has
     *               to hold for longer before it is allowed to replace what is up
     */
    public String consider(String reading, double margin, boolean switchPoint) {
        String text = reading == null ? "" : reading;
        if (text.isEmpty()) {
            // Losing the reading is not a reason to keep showing a stale one for
            // long: two misses in a row and the box goes quiet.
            pending = null;
            seen = 0;
            if (++misses >= 2) shown = "";
            return shown;
        }
        misses = 0;
        if (text.equals(shown)) {
            pending = null;
            seen = 0;
            return shown;
        }
        if (text.equals(pending)) {
            seen++;
        } else {
            pending = text;
            seen = 1;
        }
        int required = margin < TIE_MARGIN ? needed * 2 : needed;
        if (seen >= required && switchPoint) {
            shown = text;
            pending = null;
            seen = 0;
        }
        return shown;
    }

    private int misses = 0;

    /** What is on screen right now. */
    public String shown() {
        return shown;
    }

    /** A new song starts with an empty box. */
    public void reset() {
        pending = null;
        seen = 0;
        shown = "";
        misses = 0;
    }
}
