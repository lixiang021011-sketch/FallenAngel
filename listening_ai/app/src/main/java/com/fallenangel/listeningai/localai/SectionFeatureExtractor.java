package com.fallenangel.listeningai.localai;

import com.fallenangel.listeningai.OnsetCurves;
import java.util.Arrays;
import java.util.function.Consumer;

/** Separate slow frontend: 4096 FFT / 2205 hop, pooled to exact 0.5s capture bins.
 * Descriptor = 24 log-frequency bands + 12 pitch classes + relative dB level.
 * FFT chroma is an approximation, not a transcription or an instrument recognizer.
 */
public final class SectionFeatureExtractor {
    public static final int FFT = 4096, HOP = 2205, BIN_SAMPLES = 22050;
    static final int BANDS = 24, DIMENSIONS = 37;
    private final double[] frame = new double[FFT], re = new double[FFT], im = new double[FFT];
    private final double[] hann = OnsetCurves.hannWindow(FFT);
    private final int[] bandOf = new int[FFT / 2], pitchOf = new int[FFT / 2];
    private final double[] sum = new double[DIMENSIONS];
    private int fill, frames;
    private long nextSample, binStart, binEnd;
    private double rmsSum;

    /** Exposed so a later stage can read a descriptor rather than recompute it. */
    public static final class Feature {
        public final long start, end;
        public final double rms;
        public final double[] vector;
        Feature(long start, long end, double rms, double[] vector) {
            this.start = start;
            this.end = end;
            this.rms = rms;
            this.vector = vector.clone();
        }
    }

    public SectionFeatureExtractor() {
        Arrays.fill(bandOf, -1);
        Arrays.fill(pitchOf, -1);
        for (int k = 1; k < FFT / 2; k++) {
            double hz = k * 44100.0 / FFT;
            if (hz >= 55 && hz < 11025) {
                bandOf[k] = Math.min(BANDS - 1, (int) (Math.log(hz / 55) / Math.log(11025.0 / 55) * BANDS));
            }
            if (hz >= 130 && hz <= 4000) {
                int midi = (int) Math.round(69 + 12 * Math.log(hz / 440) / Math.log(2));
                pitchOf[k] = Math.floorMod(midi, 12);
            }
        }
        reset(0);
    }

    public void reset(long firstSample) {
        if (firstSample < 0) throw new IllegalArgumentException("negative sample time");
        nextSample = binStart = firstSample;
        binEnd = firstSample + BIN_SAMPLES;
        fill = frames = 0;
        rmsSum = 0;
        Arrays.fill(frame, 0);
        Arrays.fill(sum, 0);
    }

    public void push(short[] pcm, int offset, int count, Consumer<Feature> sink) {
        PcmFeatureExtractor.checkRange(pcm, offset, count);
        for (int i = offset; i < offset + count; i++) {
            frame[fill++] = pcm[i] / 32768.0;
            nextSample++;
            if (fill == FFT) {
                analyse();
                System.arraycopy(frame, HOP, frame, 0, FFT - HOP);
                fill = FFT - HOP;
            }
            if (nextSample == binEnd) {
                double[] pooled = sum.clone();
                if (frames > 0) for (int d = 0; d < pooled.length; d++) pooled[d] /= frames;
                sink.accept(new Feature(binStart, binEnd, frames == 0 ? 0 : rmsSum / frames, pooled));
                Arrays.fill(sum, 0);
                frames = 0;
                rmsSum = 0;
                binStart = binEnd;
                binEnd += BIN_SAMPLES;
            }
        }
    }

    private void analyse() {
        double squares = 0;
        for (int i = 0; i < FFT; i++) {
            re[i] = frame[i] * hann[i];
            squares += frame[i] * frame[i];
        }
        Arrays.fill(im, 0);
        OnsetCurves.fft(re, im);
        double[] vector = new double[DIMENSIONS];
        for (int k = 1; k < FFT / 2; k++) {
            double power = re[k] * re[k] + im[k] * im[k];
            if (bandOf[k] >= 0) vector[bandOf[k]] += power;
            if (pitchOf[k] >= 0) vector[BANDS + pitchOf[k]] += power;
        }
        // Unit norms per feature family largely remove playback gain from structure decisions.
        normalize(vector, 0, BANDS, 0.55);
        normalize(vector, BANDS, BANDS + 12, 0.35);
        double rms = Math.sqrt(squares / FFT);
        vector[36] = Math.sqrt(0.10) * Math.max(0, Math.min(1, (20 * Math.log10(rms + 1e-9) + 60) / 60));
        for (int d = 0; d < vector.length; d++) sum[d] += vector[d];
        rmsSum += rms;
        frames++;
    }

    private static void normalize(double[] vector, int from, int to, double weight) {
        double powerSum = 0;
        for (int i = from; i < to; i++) powerSum += vector[i];
        for (int i = from; i < to; i++) vector[i] = Math.sqrt(weight * vector[i] / (powerSum + 1e-15));
    }
}
