package com.fallenangel.listeningai.listening;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * What this song's questions cost, in numbers that outlive the song's folder.
 *
 * The design has been arguing from taste about how much a listener will put up
 * with: "asking while they are listening is an interruption", "one pause question
 * is cheap". Both are guesses about people, and the app is already the instrument
 * that could replace them with counts - it knows how many questions it asked, how
 * many were answered, how many were left until the music was given back, and it
 * can be told when one was unwelcome.
 *
 * The four counts are kept per song because the song is the unit a listener
 * thinks in ("this one asked too often"), and written to one file that spans
 * songs, because a single song cannot say whether the app asks too often - only
 * a week of them can.
 *
 * Not calibrated, and not meant to be: 0.6 of a question per song is not
 * "acceptable", it is a number to compare against next week's number.
 */
public final class QuestionTally {
    private int asked, answered, ignored, remarks;
    private final List<Double> answerSeconds = new ArrayList<>();

    /** One question reached the listener. */
    public void asked() {
        asked++;
    }

    /** One was answered, and how long the listener looked at it first. */
    public void answered(double seconds) {
        answered++;
        if (seconds >= 0 && !Double.isNaN(seconds)) answerSeconds.add(seconds);
    }

    /** One was left alone until the app gave the music back. */
    public void ignored() {
        ignored++;
    }

    /**
     * The app said something without asking anything - the other half of the
     * interaction, and the half nobody has ever measured.
     */
    public void remarked() {
        remarks++;
    }

    public int askedCount() {
        return asked;
    }

    public int answeredCount() {
        return answered;
    }

    public int ignoredCount() {
        return ignored;
    }

    public int remarkedCount() {
        return remarks;
    }

    /**
     * The middle answer time, in seconds; -1 when there is nothing to take the
     * middle of.
     *
     * The median rather than the mean on purpose: one question answered after
     * forty seconds while the listener went to the kitchen would drag a mean to a
     * number no answer was ever given at, and this number is going to be read as
     * "how long does it take them to decide".
     */
    public double medianAnswerSeconds() {
        if (answerSeconds.isEmpty()) return -1;
        List<Double> sorted = new ArrayList<>(answerSeconds);
        Collections.sort(sorted);
        int n = sorted.size();
        if (n % 2 == 1) return sorted.get(n / 2);
        return (sorted.get(n / 2 - 1) + sorted.get(n / 2)) / 2.0;
    }

    /** A new song starts from zero: these numbers are about one piece of music. */
    public void reset() {
        asked = 0;
        answered = 0;
        ignored = 0;
        remarks = 0;
        answerSeconds.clear();
    }

    /** One line of csv, for the file that spans songs. */
    public String csvRow(String when, String songName) {
        return String.join(",",
                csv(when),
                csv(songName),
                Integer.toString(asked),
                Integer.toString(answered),
                Integer.toString(ignored),
                Integer.toString(remarks),
                String.format(java.util.Locale.US, "%.1f", medianAnswerSeconds()));
    }

    /** The columns, in the order {@link #csvRow} fills them. */
    public static String header() {
        return "when,song,asked,answered,ignored,remarks,answerMedianSeconds";
    }

    /**
     * Song titles contain commas, quotes and brackets, and a row that quietly
     * becomes eight columns is worse than no file - it is a file that looks
     * readable.
     */
    private static String csv(String text) {
        String clean = text == null ? "" : text;
        if (clean.indexOf(',') < 0 && clean.indexOf('"') < 0
                && clean.indexOf('\n') < 0 && clean.indexOf('\r') < 0) {
            return clean;
        }
        return '"' + clean.replace("\"", "\"\"") + '"';
    }
}
