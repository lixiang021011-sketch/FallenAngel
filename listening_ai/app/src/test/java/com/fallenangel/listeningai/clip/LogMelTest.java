package com.fallenangel.listeningai.clip;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;
import static org.junit.Assert.fail;

import org.junit.Test;

/**
 * The frontend a model will read, checked on the three things that can silently
 * ruin it: the shape it produces, whether frequency lands where it should, and
 * whether silence stays a number.
 *
 * Plus the one that matters for the layer rather than for the placeholder: that
 * the whole thing follows its spec, because the next model's spec will not be
 * this one.
 */
public class LogMelTest {
    private static final int RATE = 44100;
    private static final LogMel PLACEHOLDER = new LogMel(FeatureSpec.PLACEHOLDER);

    /** The shape a different model might ask for: 44.1 kHz, 128 mel, fft 2048. */
    private static final FeatureSpec SECOND = new FeatureSpec(
            44100, 128, 2048, 2048, 512, 0, 11025, 1e-6, true);

    private static short[] tone(double hz, double seconds, int rate) {
        return tone(hz, seconds, rate, 12000);
    }

    private static short[] tone(double hz, double seconds, int rate, double amplitude) {
        short[] pcm = new short[(int) (seconds * rate)];
        for (int i = 0; i < pcm.length; i++) {
            pcm[i] = (short) (Math.sin(2 * Math.PI * hz * i / rate) * amplitude);
        }
        return pcm;
    }

    private static int peakBand(float[] frame) {
        int best = 0;
        for (int i = 1; i < frame.length; i++) {
            if (frame[i] > frame[best]) best = i;
        }
        return best;
    }

    @Test
    public void theShapeIsWhatTheSpecSays() {
        short[] pcm = tone(440, 2.0, RATE);
        float[][] mel = PLACEHOLDER.of(pcm, 0, pcm.length, RATE);

        FeatureSpec spec = FeatureSpec.PLACEHOLDER;
        int resampled = (int) Math.floor(2.0 * spec.sampleRate);
        assertEquals(spec.framesFor(resampled), mel.length);
        assertEquals(spec.mels, mel[0].length);
    }

    /**
     * The reason the spec exists: another model means another frontend, not
     * another constant to edit.
     */
    @Test
    public void aDifferentSpecIsADifferentFrontend() {
        LogMel second = new LogMel(SECOND);
        short[] pcm = tone(440, 2.0, RATE);
        float[][] mel = second.of(pcm, 0, pcm.length, RATE);
        assertEquals("128 bands, as asked for", 128, mel[0].length);
        assertEquals(SECOND.framesFor(2 * SECOND.sampleRate), mel.length);
    }

    @Test
    public void aHigherTonePeaksInAHigherBand() {
        short[] low = tone(200, 1.0, RATE);
        short[] high = tone(3000, 1.0, RATE);

        float[][] lowMel = PLACEHOLDER.of(low, 0, low.length, RATE);
        float[][] highMel = PLACEHOLDER.of(high, 0, high.length, RATE);

        int lowBand = peakBand(lowMel[lowMel.length / 2]);
        int highBand = peakBand(highMel[highMel.length / 2]);
        assertTrue("200 Hz should sit below 3 kHz: " + lowBand + " vs " + highBand,
                highBand > lowBand + 10);
    }

    @Test
    public void theClipIsNormalisedSoVolumeDoesNotMatter() {
        // Three times the amplitude without clipping: the test is about level,
        // and a clipped signal is a different signal.
        short[] quiet = tone(700, 1.0, RATE, 3000);
        short[] loud = new short[quiet.length];
        for (int i = 0; i < quiet.length; i++) {
            loud[i] = (short) Math.max(-32768, Math.min(32767, quiet[i] * 3));
        }
        float[][] a = PLACEHOLDER.of(quiet, 0, quiet.length, RATE);
        float[][] b = PLACEHOLDER.of(loud, 0, loud.length, RATE);

        double worst = 0;
        for (int f = 0; f < a.length; f++) {
            for (int m = 0; m < a[f].length; m++) {
                worst = Math.max(worst, Math.abs(a[f][m] - b[f][m]));
            }
        }
        assertTrue("the same music at two volumes should look the same: " + worst,
                worst < 0.02);
    }

    @Test
    public void silenceIsAFloorAndNotANan() {
        short[] silent = new short[RATE];
        float[][] mel = PLACEHOLDER.of(silent, 0, silent.length, RATE);
        assertTrue("a second of audio is about a hundred frames", mel.length > 90);
        for (float[] frame : mel) {
            for (float value : frame) {
                assertTrue("finite", Float.isFinite(value));
            }
        }
    }

    @Test
    public void theSameAudioGivesTheSameNumbers() {
        short[] pcm = tone(880, 0.5, RATE);
        float[][] first = PLACEHOLDER.of(pcm, 0, pcm.length, RATE);
        float[][] second = PLACEHOLDER.of(pcm, 0, pcm.length, RATE);
        for (int f = 0; f < first.length; f++) {
            for (int m = 0; m < first[f].length; m++) {
                assertEquals(first[f][m], second[f][m], 0f);
            }
        }
    }

    /** A spec that could not be computed is refused when it is loaded, not per clip. */
    @Test
    public void anImpossibleSpecIsRefused() {
        assertRefused(new FeatureSpec(16000, 64, 500, 400, 160, 0, 8000, 1e-6, true),
                "not a power of two");
        assertRefused(new FeatureSpec(16000, 64, 512, 400, 160, 0, 9000, 1e-6, true),
                "past Nyquist");
        assertRefused(new FeatureSpec(16000, 0, 512, 400, 160, 0, 8000, 1e-6, true),
                "no mel bands");
        assertRefused(new FeatureSpec(16000, 64, 512, 400, 160, 8000, 8000, 1e-6, true),
                "empty range");
    }

    private static void assertRefused(FeatureSpec spec, String why) {
        try {
            new LogMel(spec);
            fail("should have been refused: " + why);
        } catch (IllegalArgumentException expected) {
            assertTrue(expected.getMessage() != null);
        }
    }
}
