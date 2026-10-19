package com.fallenangel.overlaybeat;

import java.util.Arrays;

/**
 * The features everything else is built on: one onset curve for the kick band,
 * one for the snare band, and their mean.
 *
 * Shared on purpose. There are now two ways to make a chart - while listening,
 * and from the whole file afterwards - and the one thing they must agree about
 * is what the audio looks like. A chart generated from a file and a chart
 * generated live are meant to be the same chart, so both measure the same thing
 * with the same window, the same bands and the same definition of an onset.
 *
 * An onset is the positive jump in log energy from one frame to the next. Using
 * the jump rather than the energy itself is what makes a quiet hat count and a
 * loud sustained bass not count.
 */
public final class OnsetCurves {
    public static final int SAMPLE_RATE = 44100;
    public static final int FFT_SIZE = 2048;
    public static final int HOP_SIZE = 220;
    /** Analysis frames per second - about one every five milliseconds. */
    public static final int PER_SECOND = SAMPLE_RATE / HOP_SIZE;

    public static final double KICK_LO = 30, KICK_HI = 160;
    public static final double SNARE_LO = 1800, SNARE_HI = 8000;

    private static final double[] HANN = hannWindow(FFT_SIZE);

    private OnsetCurves() {
    }

    public static double[] hannWindow(int size) {
        double[] window = new double[size];
        for (int i = 0; i < size; i++) {
            window[i] = 0.5 * (1 - Math.cos(2 * Math.PI * i / (size - 1)));
        }
        return window;
    }

    public static double[] hann() {
        return HANN;
    }

    /** The frame time this analysis is anchored on: where the windows end. */
    public static double frameSeconds(int frameIndex) {
        return (FFT_SIZE + frameIndex * (double) HOP_SIZE) / SAMPLE_RATE;
    }

    public static double secondsToFrame(double seconds) {
        return (seconds * SAMPLE_RATE - FFT_SIZE) / (double) HOP_SIZE;
    }

    /**
     * Analyses one window at a time, keeping the previous frame's energy so the
     * onset is a difference rather than a level.
     */
    public static final class FrameAnalyser {
        private final double[] re = new double[FFT_SIZE];
        private final double[] im = new double[FFT_SIZE];
        private double kickLog = -30;
        private double snareLog = -30;

        /** Kick-band jump at the frame just analysed. */
        public double kickOnset;
        /** Snare-band jump at the frame just analysed. */
        public double snareOnset;
        /** Their mean: what the tempo and grid work on. */
        public double strength;
        public double kickEnergy;
        public double snareEnergy;

        /** @param windowed FFT_SIZE samples, already multiplied by the window */
        public void analyse(double[] windowed) {
            System.arraycopy(windowed, 0, re, 0, FFT_SIZE);
            Arrays.fill(im, 0);
            fft(re, im);
            kickEnergy = bandEnergy(re, im, KICK_LO, KICK_HI) / FFT_SIZE;
            snareEnergy = bandEnergy(re, im, SNARE_LO, SNARE_HI) / FFT_SIZE;
            double kickNow = Math.log(kickEnergy + 1e-12);
            double snareNow = Math.log(snareEnergy + 1e-12);
            kickOnset = Math.max(0, kickNow - kickLog);
            snareOnset = Math.max(0, snareNow - snareLog);
            kickLog = kickNow;
            snareLog = snareNow;
            strength = (kickOnset + snareOnset) * 0.5;
        }
    }

    public static double bandEnergy(double[] re, double[] im, double lo, double hi) {
        int loBin = Math.max(1, (int) Math.floor(lo * FFT_SIZE / SAMPLE_RATE));
        int hiBin = Math.min(FFT_SIZE / 2 - 1, (int) Math.ceil(hi * FFT_SIZE / SAMPLE_RATE));
        double sum = 0;
        for (int bin = loBin; bin <= hiBin; bin++) {
            sum += re[bin] * re[bin] + im[bin] * im[bin];
        }
        return sum;
    }

    public static void fft(double[] re, double[] im) {
        int n = re.length;
        for (int i = 1, j = 0; i < n; i++) {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) {
                double tr = re[i];
                re[i] = re[j];
                re[j] = tr;
                double ti = im[i];
                im[i] = im[j];
                im[j] = ti;
            }
        }
        for (int len = 2; len <= n; len <<= 1) {
            double angle = -2 * Math.PI / len;
            double wRe = Math.cos(angle);
            double wIm = Math.sin(angle);
            for (int i = 0; i < n; i += len) {
                double curRe = 1, curIm = 0;
                for (int k = 0; k < len / 2; k++) {
                    int a = i + k;
                    int b = i + k + len / 2;
                    double vRe = re[b] * curRe - im[b] * curIm;
                    double vIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - vRe;
                    im[b] = im[a] - vIm;
                    re[a] += vRe;
                    im[a] += vIm;
                    double nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }
}
