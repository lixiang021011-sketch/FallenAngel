package com.fallenangel.listeningai.clip;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import java.util.ArrayList;
import java.util.List;

/**
 * Getting 44.1 kHz down to 16 kHz without adding anything.
 *
 * The captured audio is whatever the listener is playing, and what is above
 * 8 kHz does not disappear when the samples are thinned out - it reappears
 * below it, wearing the wrong frequency. Measured on real music, decimating
 * without a low pass moved a model's embedding by 0.10, its own noise floor for
 * reporting change.
 */
public class ResamplerTest {
    private static short[] tone(double hz, double seconds, int rate) {
        short[] pcm = new short[(int) (seconds * rate)];
        for (int i = 0; i < pcm.length; i++) {
            pcm[i] = (short) (Math.sin(2 * Math.PI * hz * i / rate) * 12000);
        }
        return pcm;
    }

    private static double rms(float[] values) {
        double sum = 0;
        for (float value : values) sum += value * value;
        return Math.sqrt(sum / values.length);
    }

    /** A tone above the new Nyquist has nowhere to go, and should not land. */
    @Test
    public void aToneAboveTheNewNyquistDoesNotComeBackAsAnotherTone() {
        float[] kept = Resampler.to(tone(1000, 1, 44100), 0, 44100, 44100, 16000);
        float[] folded = Resampler.to(tone(15000, 1, 44100), 0, 44100, 44100, 16000);
        assertTrue("1 kHz should survive", rms(kept) > 0.2);
        assertTrue("15 kHz should be gone, not reborn at 1 kHz: " + rms(folded)
                        + " vs " + rms(kept),
                rms(folded) < 0.05 * rms(kept));
    }

    /** The same audio, block by block, is the same audio. */
    @Test
    public void theStreamingPathAgreesWithTheWholeClipPath() {
        short[] pcm = tone(3000, 0.5, 44100);
        float[] whole = Resampler.to(pcm, 0, pcm.length, 44100, 16000);

        List<Float> streamed = new ArrayList<>();
        Resampler.Stream stream = new Resampler.Stream(44100, 16000);
        int block = 0;
        int[] sizes = {17, 2048, 1, 999, 512};
        for (int at = 0; at < pcm.length; ) {
            int count = Math.min(sizes[block++ % sizes.length], pcm.length - at);
            stream.accept(pcm, at, count, streamed::add);
            at += count;
        }
        // Fewer by the filter's own tail: the last outputs need audio that has
        // not arrived yet, so a stream ends a few dozen samples shorter than its
        // input. Nothing is missing anywhere else.
        assertTrue("the stream ends within its filter's reach: " + streamed.size()
                        + " vs " + whole.length,
                streamed.size() <= whole.length && streamed.size() > whole.length - 64);
        double worst = 0;
        for (int i = 100; i < streamed.size() - 100; i++) {
            worst = Math.max(worst, Math.abs(whole[i] - streamed.get(i)));
        }
        // Not zero: the whole-clip path holds the signal still at its own ends,
        // and the stream starts from silence. Both are the same audio in the
        // middle, which is the other 99% of it.
        assertTrue("block sizes should not change the audio: " + worst, worst < 1e-3);
    }

    /** Nothing to resample means nothing to filter. */
    @Test
    public void theSameRateIsLeftAlone() {
        short[] pcm = tone(440, 0.01, 16000);
        float[] out = Resampler.to(pcm, 0, pcm.length, 16000, 16000);
        assertEquals(pcm.length, out.length);
        for (int i = 0; i < pcm.length; i++) {
            assertEquals(pcm[i] / 32768.0, out[i], 1e-6);
        }
    }
}
