package com.fallenangel.listeningai.clip;

/**
 * Getting audio down to the rate a model was trained at, without inventing
 * content.
 *
 * The capture runs at 44.1 kHz and the models here run at 16 kHz, so something
 * has to remove the two thirds of the samples that are in the way. Taking every
 * third one is the obvious thing and the wrong one: everything above 8 kHz folds
 * back down into the band the model is looking at instead of disappearing.
 * Measured on a percussion-heavy track, decimating that way moved the first real
 * model's embedding by 0.10 cosine - which is the size of the novelty it reports
 * for real musical change, so the phone would have been reporting its own
 * resampler.
 *
 * So: a windowed-sinc low pass first, then linear interpolation at the output
 * positions. Not the last word in resampling, but the aliasing is gone, and the
 * cost is a few multiply-adds per input sample.
 */
public final class Resampler {
    /** Odd, so the filter has a centre tap and a whole-sample group delay. */
    private static final int TAPS = 63;
    /** Where the low pass starts to roll off, as a fraction of the target rate. */
    private static final double CUTOFF = 0.48;

    private Resampler() {
    }

    /** Where the resampled audio goes; a lambda in the streaming path. */
    public interface Sink {
        void accept(float sample);
    }

    /**
     * The same resampling, for audio that arrives a block at a time.
     *
     * Filtering each block on its own would treat the signal either side of it
     * as if it were silence, which is a click every block - at 2048 samples that
     * is a broadband tick every 46 ms, and a model asked to notice change will
     * notice it. So the filter carries its last few dozen input samples across
     * blocks, and the output position is a counter rather than an accumulation,
     * so the rate stays exact however long the stream runs.
     */
    public static final class Stream {
        private final double[] taps;
        private final short[] history;
        private final double step;
        /** Half the filter: how far its support reaches either side of a sample. */
        private final int half;
        private long accepted;
        private long outputIndex;

        public Stream(int sourceRate, int targetRate) {
            if (sourceRate <= 0 || targetRate <= 0) {
                throw new IllegalArgumentException("invalid sample rate");
            }
            this.taps = sourceRate > targetRate ? lowPassTaps(sourceRate, targetRate) : null;
            this.half = TAPS / 2;
            // Twice the filter's reach, not once: an output sample is only
            // emitted once its whole support lies inside what we have, so the
            // ones left over at the end of a block are still computable - with
            // real audio on both sides - in the next one.
            this.history = new short[taps == null ? 0 : 2 * half];
            this.step = sourceRate / (double) targetRate;
        }

        public void accept(short[] pcm, int offset, int count, Sink sink) {
            if (pcm == null || offset < 0 || count < 0 || offset > pcm.length - count) {
                throw new IllegalArgumentException("invalid PCM range");
            }
            if (count == 0) return;
            if (taps == null) {
                for (int i = 0; i < count; i++) sink.accept((float) (pcm[offset + i] / 32768.0));
                accepted += count;
                outputIndex = (long) (accepted / step);
                return;
            }
            // Filter the block with the previous block's tail in front of it.
            // `extended[j]` is the source sample at absolute index
            // `accepted - history.length + j`.
            int lead = history.length;
            double[] extended = new double[lead + count];
            for (int i = 0; i < lead; i++) extended[i] = history[i] / 32768.0;
            for (int i = 0; i < count; i++) extended[lead + i] = pcm[offset + i] / 32768.0;
            double[] filtered = lowPass(extended, taps);

            // Output `at` belongs at source position at * step, counted from the
            // start of the stream. The symmetry of the filter is what puts it
            // back in line with the input rather than a half filter behind it.
            int lastUsable = count - 1 + lead - half;
            for (long at = outputIndex; ; at++) {
                double position = at * step;
                long whole = (long) position;
                int index = (int) (whole - accepted + lead);
                if (index < half) break;                  // already emitted
                if (index > lastUsable) break;            // leave it for next time
                double fraction = position - whole;
                double a = filtered[index];
                double b = filtered[index + 1];
                sink.accept((float) (a + (b - a) * fraction));
                outputIndex = at + 1;
            }

            // Keep the last `lead` source samples: the next block starts with
            // these in front of it, and that is the whole bookkeeping.
            if (count >= lead) {
                for (int i = 0; i < lead; i++) history[i] = pcm[offset + count - lead + i];
            } else {
                System.arraycopy(history, count, history, 0, lead - count);
                for (int i = 0; i < count; i++) history[lead - count + i] = pcm[offset + i];
            }
            accepted += count;
        }
    }

    /**
     * @param pcm        mono signed PCM at {@code sourceRate}
     * @param offset     first sample to read
     * @param count      how many samples
     * @param sourceRate the rate the audio arrived at
     * @param targetRate the rate the model wants
     * @return the clip at {@code targetRate}, scaled to [-1, 1]
     */
    public static float[] to(short[] pcm, int offset, int count, int sourceRate, int targetRate) {
        if (pcm == null || offset < 0 || count < 0 || offset > pcm.length - count) {
            throw new IllegalArgumentException("invalid PCM range");
        }
        if (sourceRate <= 0 || targetRate <= 0) {
            throw new IllegalArgumentException("invalid sample rate");
        }
        float[] source = new float[count];
        for (int i = 0; i < count; i++) source[i] = (float) (pcm[offset + i] / 32768.0);
        if (sourceRate == targetRate) return source;
        if (count == 0) return source;

        // Upsampling needs no low pass: nothing above the target rate exists in
        // the input if the target rate is the higher one.
        double[] filtered = null;
        if (sourceRate > targetRate) {
            double[] wide = new double[count];
            for (int i = 0; i < count; i++) wide[i] = source[i];
            filtered = lowPass(wide, lowPassTaps(sourceRate, targetRate));
        }
        int outCount = (int) Math.floor(count * (double) targetRate / sourceRate);
        float[] out = new float[Math.max(0, outCount)];
        double step = sourceRate / (double) targetRate;
        // No delay term: the filter is applied symmetrically, so the sample it
        // returns for an index is already that index's, and adding its half
        // length again would put the whole clip three quarters of a millisecond
        // out of step with the same audio arriving in blocks.
        for (int i = 0; i < out.length; i++) {
            double at = i * step;
            int index = (int) at;
            double fraction = at - index;
            if (filtered == null) {
                int a = Math.min(index, count - 1);
                int b = Math.min(index + 1, count - 1);
                double low = source[a];
                out[i] = (float) (low + (source[b] - low) * fraction);
            } else {
                double a = filtered[Math.min(Math.max(index, 0), count - 1)];
                double b = filtered[Math.min(Math.max(index + 1, 0), count - 1)];
                out[i] = (float) (a + (b - a) * fraction);
            }
        }
        return out;
    }

    /** A sinc low pass at {@link #CUTOFF} of the target rate, Blackman-windowed. */
    static double[] lowPassTaps(int sourceRate, int targetRate) {
        double cutoff = CUTOFF * targetRate / sourceRate;
        double[] taps = new double[TAPS];
        double sum = 0;
        for (int i = 0; i < TAPS; i++) {
            double x = i - (TAPS - 1) / 2.0;
            double sinc = x == 0 ? 2 * cutoff : Math.sin(2 * Math.PI * cutoff * x) / (Math.PI * x);
            double window = 0.42 - 0.5 * Math.cos(2 * Math.PI * i / (TAPS - 1))
                    + 0.08 * Math.cos(4 * Math.PI * i / (TAPS - 1));
            taps[i] = sinc * window;
            sum += taps[i];
        }
        if (sum != 0) {
            for (int i = 0; i < TAPS; i++) taps[i] /= sum;
        }
        return taps;
    }

    /**
     * Convolve with the taps, holding the signal's ends still.
     *
     * Clamping at the edges rather than mirroring or zeroing keeps the first and
     * last few milliseconds at roughly the level of the audio next to them, so a
     * clip never starts with a step that the model would read as an onset.
     */
    private static double[] lowPass(double[] source, double[] taps) {
        int half = (TAPS - 1) / 2;
        double[] out = new double[source.length];
        for (int i = 0; i < source.length; i++) {
            double sum = 0;
            for (int k = 0; k < TAPS; k++) {
                int at = Math.min(Math.max(i + k - half, 0), source.length - 1);
                sum += taps[k] * source[at];
            }
            out[i] = sum;
        }
        return out;
    }
}
