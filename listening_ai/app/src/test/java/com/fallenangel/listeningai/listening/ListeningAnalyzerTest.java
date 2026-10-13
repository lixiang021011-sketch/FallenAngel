package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import java.util.Random;

/**
 * The explanations the analysis offers, checked against audio whose answer is
 * known. These are the one part of the system that has to be right about the
 * music before a person is asked about it - a question built on a measurement
 * that is not there teaches the listener to stop answering.
 */
public class ListeningAnalyzerTest {
    private static final int RATE = 44100;
    private static final double WINDOW = 8;

    /** A drum-ish hit: a short low thump or a short high noise. */
    private static void hit(short[] pcm, double at, boolean low, Random random) {
        int start = (int) (at * RATE);
        int length = (int) (0.08 * RATE);
        for (int i = 0; i < length && start + i < pcm.length; i++) {
            double t = i / (double) RATE;
            double value = low
                    ? Math.sin(2 * Math.PI * 70 * t) * Math.exp(-t * 35) * 0.8
                    : (random.nextDouble() * 2 - 1) * Math.exp(-t * 40) * 0.5;
            double mixed = pcm[start + i] / 32768.0 + value;
            pcm[start + i] = (short) Math.max(-32768, Math.min(32767, Math.round(mixed * 32768)));
        }
    }

    private static PauseQuestion analyse(short[] pcm) {
        return ListeningAnalyzer.analyse(pcm, 0, pcm.length, 0,
                ListeningAnalyzer.NO_GRID, "", -1);
    }

    private static boolean offers(PauseQuestion question, String id) {
        for (PauseQuestion.Candidate candidate : question.candidates) {
            if (candidate.id.equals(id)) return true;
        }
        return false;
    }

    @Test
    public void aBusierSecondHalfIsOfferedAsDensity() {
        Random random = new Random(11);
        short[] pcm = new short[(int) (WINDOW * RATE)];
        for (int i = 0; i < 8; i++) hit(pcm, i * 0.5, i % 2 == 0, random);        // two a second
        for (int i = 0; i < 32; i++) hit(pcm, 4 + i * 0.125, i % 2 == 0, random);  // eight a second

        PauseQuestion question = analyse(pcm);
        assertTrue("a clear increase in activity should be offered",
                offers(question, "density_up"));
        assertFalse("and it should not be offered as a decrease", offers(question, "density_down"));
        for (PauseQuestion.Candidate candidate : question.candidates) {
            if (candidate.id.equals("density_up")) {
                assertTrue("the evidence should say what was measured: " + candidate.evidence,
                        candidate.evidence.contains("/s"));
                assertTrue(candidate.confidence > 0.3);
            }
        }
    }

    @Test
    public void aCalmerSecondHalfIsOfferedAsTheOpposite() {
        Random random = new Random(12);
        short[] pcm = new short[(int) (WINDOW * RATE)];
        for (int i = 0; i < 32; i++) hit(pcm, i * 0.125, i % 2 == 0, random);
        for (int i = 0; i < 8; i++) hit(pcm, 4 + i * 0.5, i % 2 == 0, random);

        PauseQuestion question = analyse(pcm);
        assertTrue(offers(question, "density_down"));
        assertFalse(offers(question, "density_up"));
    }

    @Test
    public void silenceIsNotAQuestion() {
        short[] pcm = new short[(int) (WINDOW * RATE)];
        PauseQuestion question = analyse(pcm);
        assertEquals("with nothing measurable there is nothing to choose between",
                1, question.candidates.size());
        assertEquals(PauseQuestion.NONE_LABEL, question.candidates.get(0).label);
    }

    @Test
    public void everyQuestionOffersNoneOfTheAbove() {
        Random random = new Random(13);
        short[] pcm = new short[(int) (WINDOW * RATE)];
        for (int i = 0; i < 8; i++) hit(pcm, i * 0.5, true, random);
        for (int i = 0; i < 32; i++) hit(pcm, 4 + i * 0.125, false, random);

        PauseQuestion question = analyse(pcm);
        PauseQuestion.Candidate last = question.candidates.get(question.candidates.size() - 1);
        assertEquals(PauseQuestion.NONE_LABEL, last.label);
        assertEquals(0.0, last.confidence, 1e-9);
        assertTrue("never more than four proposals plus the way out",
                question.candidates.size() <= 5);
    }

    /**
     * The same hits, played louder, is a lift and not a change of density - the
     * difference a chorus makes, and the one people notice most reliably.
     */
    @Test
    public void gettingLouderIsOfferedAsALift() {
        Random random = new Random(14);
        short[] pcm = new short[(int) (WINDOW * RATE)];
        for (int i = 0; i < 16; i++) hit(pcm, i * 0.25, i % 2 == 0, random);
        for (int i = 0; i < 16; i++) loudHit(pcm, 4 + i * 0.25, i % 2 == 0, random);

        PauseQuestion question = analyse(pcm);
        assertTrue("a passage that lifts should be offered as a lift",
                offers(question, "level_up"));
        assertFalse("the same hits louder is not more hits",
                offers(question, "density_up"));
    }

    /** A new part of the song starting inside the window is a fact worth offering. */
    @Test
    public void aSectionBoundaryInTheWindowIsOffered() {
        Random random = new Random(15);
        short[] pcm = new short[(int) (WINDOW * RATE)];
        for (int i = 0; i < 32; i++) hit(pcm, i * 0.25, true, random);

        PauseQuestion question = ListeningAnalyzer.analyse(pcm, 0, pcm.length, 0,
                ListeningAnalyzer.NO_GRID, "B", (long) (6.5 * RATE));
        assertTrue(offers(question, "section"));
        for (PauseQuestion.Candidate candidate : question.candidates) {
            if (candidate.id.equals("section")) {
                assertTrue("the evidence should say when and where: " + candidate.evidence,
                        candidate.evidence.contains("B"));
            }
        }
    }

    /** Louder than {@link #hit}, same shape. */
    private static void loudHit(short[] pcm, double at, boolean low, Random random) {
        short[] one = new short[(int) (0.08 * RATE)];
        hit(one, 0, low, random);
        int start = (int) (at * RATE);
        for (int i = 0; i < one.length && start + i < pcm.length; i++) {
            int mixed = pcm[start + i] + one[i] * 2;
            pcm[start + i] = (short) Math.max(-32768, Math.min(32767, mixed));
        }
    }
}
