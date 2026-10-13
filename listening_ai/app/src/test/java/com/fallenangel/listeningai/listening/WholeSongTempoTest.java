package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import java.util.Random;

/**
 * Fitting the tempo to the whole song's onsets.

 * Two things worth pinning: that the fit finds the tempo the clicks are actually
 * at, and that it admits the ambiguity instead of hiding it - a comb fitted to a
 * kick-and-snare pattern fits half the tempo just as well, and the answer has to
 * carry that candidate rather than pretend there is only one.
 */
public class WholeSongTempoTest {
    private static final double FRAME = 0.01;

    private static OnsetCurve curveOf(float[] values) {
        OnsetCurve curve = new OnsetCurve(FRAME);
        for (float value : values) curve.push(value);
        return curve;
    }

    /**
     * A click track: a short rise on every beat.

     * `strongEvery` of 0 means every beat is equal; 2 means every other beat is
     * loud, which is what a kick and a snare on beats one and three look like.
     */
    private static OnsetCurve clicks(double seconds, double start, double beat,
                                     int strongEvery, double weakLevel) {
        int frames = (int) (seconds / FRAME);
        float[] values = new float[frames];
        Random random = new Random(7);
        int index = 0;
        for (double t = start; t < seconds; t += beat, index++) {
            int at = (int) ((t + (random.nextDouble() - 0.5) * 0.01) / FRAME);
            double level = (strongEvery > 0 && index % strongEvery != 0) ? weakLevel : 1.0;
            for (int k = 0; k < 3 && at + k < frames; k++) {
                if (at + k >= 0) {
                    values[at + k] = Math.max(values[at + k], (float) (level / (k + 1)));
                }
            }
        }
        return curveOf(values);
    }

    private static boolean has(WholeSongTempo.Result result, double bpm, double tolerance) {
        for (WholeSongTempo.Candidate candidate : result.candidates) {
            if (Math.abs(candidate.bpm - bpm) <= tolerance) return true;
        }
        return false;
    }

    @Test
    public void theTempoOfEvenClicksIsFound() {
        OnsetCurve curve = clicks(40, 2.0, 0.5, 0, 1);
        WholeSongTempo.Result got = WholeSongTempo.fit(curve, 2.0, 40, 240);
        assertTrue(got.usable);
        assertNotNull(got.best());
        assertEquals(120.0, got.best().bpm, 1.0);
    }

    @Test
    public void aPhaseHalfABeatOutScoresBadly() {
        OnsetCurve curve = clicks(40, 2.0, 0.5, 0, 1);
        double onBeat = WholeSongTempo.fit(curve, 2.0, 40, 240).best().score;
        double offBeat = WholeSongTempo.fit(curve, 2.25, 40, 240).best().score;
        assertTrue("on-beat " + onBeat + " should beat off-beat " + offBeat, onBeat > offBeat * 2);
    }

    @Test
    public void bothHalvesOfTheAmbiguityAreOffered() {
        // Kick and snare: loud on beats one and three, light on the others.
        OnsetCurve curve = clicks(60, 2.0, 0.5, 2, 0.15);
        WholeSongTempo.Result got = WholeSongTempo.fit(curve, 2.0, 40, 240);
        assertTrue("candidates: " + got, has(got, 60, 3));
        assertTrue("candidates: " + got, has(got, 120, 3));
    }

    /**
     * The tie the preference exists for.

     * Evenly spaced clicks fit 60 and 120 equally well on the onsets alone; only
     * the preference decides, so the two raw scores should be close and the
     * ranking should still come out at the tempo songs are written in.
     */
    @Test
    public void theOctaveTieIsBrokenOpenly() {
        OnsetCurve curve = clicks(40, 2.0, 0.5, 0, 1);
        WholeSongTempo.Result got = WholeSongTempo.fit(curve, 2.0, 40, 240);
        double half = 0;
        for (WholeSongTempo.Candidate candidate : got.candidates) {
            if (Math.abs(candidate.bpm - 60) < 3) half = candidate.raw;
        }
        assertTrue("half-tempo candidate missing: " + got, half > 0);
        double best = got.best().raw;
        assertTrue("raw scores should be close: " + best + " vs " + half,
                Math.abs(best - half) < 0.5 * Math.max(best, half));
    }

    @Test
    public void aSongWithNoOnsetsIsRefusedRatherThanGuessed() {
        float[] silence = new float[4000];
        assertFalse(WholeSongTempo.fit(curveOf(silence), 0, 40, 240).usable);
        assertFalse(WholeSongTempo.fit(new OnsetCurve(FRAME), 0, 40, 240).usable);
    }

    @Test
    public void aTempoOutsideTheSearchRangeIsNeverReturned() {
        OnsetCurve curve = clicks(40, 2.0, 0.25, 0, 1);   // clicks at 240 a minute
        WholeSongTempo.Result got = WholeSongTempo.fit(curve, 2.0, 80, 160);
        assertTrue(got.usable);
        for (WholeSongTempo.Candidate candidate : got.candidates) {
            assertTrue(candidate.bpm + "", candidate.bpm >= 80 && candidate.bpm <= 160);
        }
    }
}
