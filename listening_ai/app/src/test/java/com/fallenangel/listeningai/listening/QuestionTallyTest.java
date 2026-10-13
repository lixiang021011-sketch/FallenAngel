package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

/**
 * The numbers a listener's own tolerance is read from.
 *
 * Four of them, and they are the four the design argues about: how often the app
 * asked, how often it was answered, how often it was left alone until the music
 * was given back, and how often the listener said outright that it was too much.
 * Whether being interrupted is welcome is a question about people, not about
 * code; this is the part of it that can be counted.
 */
public class QuestionTallyTest {
    @Test
    public void emptyTallySaysNobodyAnsweredRatherThanZeroSeconds() {
        QuestionTally tally = new QuestionTally();
        assertEquals(0, tally.askedCount());
        assertEquals(0, tally.answeredCount());
        // -1, not 0: "no answer" and "answered instantly" are different, and a
        // column that rounds both to nothing cannot tell them apart later.
        assertEquals(-1.0, tally.medianAnswerSeconds(), 1e-9);
        assertTrue(tally.csvRow("2026-10-13 04:05", "no one sang").endsWith(",-1.0"));
    }

    @Test
    public void countsAskAnswerIgnoreAndAnnoyanceSeparately() {
        QuestionTally tally = new QuestionTally();
        tally.asked();
        tally.asked();
        tally.asked();
        tally.answered(3.5);
        tally.ignored();
        tally.annoyed(true);

        assertEquals(3, tally.askedCount());
        assertEquals(1, tally.answeredCount());
        assertEquals(1, tally.ignoredCount());
        assertEquals(1, tally.annoyedAskingCount());
        assertEquals(0, tally.annoyedSayingCount());
    }

    @Test
    public void beingTalkedAtIsCountedApartFromBeingAsked() {
        QuestionTally tally = new QuestionTally();
        tally.asked();
        tally.remarked();
        tally.remarked();
        tally.annoyed(true);
        tally.annoyed(false);
        tally.annoyed(false);

        // The question this file exists to answer: which of the two they mind.
        // One column for both would fold the answer away.
        assertEquals(1, tally.askedCount());
        assertEquals(2, tally.remarkedCount());
        assertEquals(1, tally.annoyedAskingCount());
        assertEquals(2, tally.annoyedSayingCount());
    }

    @Test
    public void medianIgnoresTheOneSlowAnswer() {
        QuestionTally tally = new QuestionTally();
        for (double seconds : new double[] {2.0, 4.0, 30.0}) {
            tally.asked();
            tally.answered(seconds);
        }
        // The mean would be 12 seconds, which describes no answer anybody gave.
        assertEquals(4.0, tally.medianAnswerSeconds(), 1e-9);
    }

    @Test
    public void evenCountsTakeTheMiddleTwo() {
        QuestionTally tally = new QuestionTally();
        for (double seconds : new double[] {1.0, 2.0, 3.0, 10.0}) {
            tally.answered(seconds);
        }
        assertEquals(2.5, tally.medianAnswerSeconds(), 1e-9);
    }

    @Test
    public void aSongWithACommaStaysOneColumn() {
        QuestionTally tally = new QuestionTally();
        tally.asked();
        tally.remarked();
        tally.annoyed(true);
        String row = tally.csvRow("2026-10-13 04:05", "Yorushika, 雲と幽霊");
        assertTrue(row, row.contains("\"Yorushika, 雲と幽霊\""));
        assertEquals(9, row.split(",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)", -1).length);
    }

    @Test
    public void resettingStartsTheSongAgain() {
        QuestionTally tally = new QuestionTally();
        tally.asked();
        tally.answered(2.0);
        tally.remarked();
        tally.annoyed(false);
        tally.reset();
        assertEquals(0, tally.askedCount());
        assertEquals(0, tally.answeredCount());
        assertEquals(0, tally.remarkedCount());
        assertEquals(0, tally.annoyedAskingCount());
        assertEquals(0, tally.annoyedSayingCount());
        assertEquals(-1.0, tally.medianAnswerSeconds(), 1e-9);
    }

    @Test
    public void theHeaderNamesTheColumnsInTheSameOrderTheRowFillsThem() {
        String header = QuestionTally.header();
        assertEquals(
                "when,song,asked,answered,ignored,remarks,annoyedAsking,annoyedSaying,answerMedianSeconds",
                header);
        QuestionTally tally = new QuestionTally();
        assertEquals(header.split(",").length,
                tally.csvRow("2026-10-13 04:05", "x").split(",").length);
    }
}
