package com.fallenangel.listeningai.localai;

import com.fallenangel.listeningai.OnsetCurves;
import java.util.Arrays;
import java.util.function.Consumer;

/** Reuses the chart frontend: 2048 Hann window, 220 sample hop, 30-160 / 1800-8000 Hz bands. */
public final class PcmFeatureExtractor {
    private final double[] frame = new double[OnsetCurves.FFT_SIZE];
    private final double[] windowed = new double[OnsetCurves.FFT_SIZE];
    private final double[] hann = OnsetCurves.hannWindow(OnsetCurves.FFT_SIZE);
    private OnsetCurves.FrameAnalyser analyser = new OnsetCurves.FrameAnalyser();
    private int fill;
    private long nextSample;

    public void reset(long firstSample) {
        if (firstSample < 0) throw new IllegalArgumentException("negative sample time");
        fill = 0;
        nextSample = firstSample;
        Arrays.fill(frame, 0);
        analyser = new OnsetCurves.FrameAnalyser();
    }

    public void push(short[] pcm, int offset, int count, Consumer<AudioFeatures> consumer) {
        checkRange(pcm, offset, count);
        for (int i = offset; i < offset + count; i++) {
            frame[fill++] = pcm[i] / 32768.0;
            nextSample++;
            if (fill < frame.length) continue;
            double squares = 0;
            for (int j = 0; j < frame.length; j++) {
                squares += frame[j] * frame[j];
                windowed[j] = frame[j] * hann[j];
            }
            analyser.analyse(windowed);
            AudioFeatures features = new AudioFeatures(nextSample, Math.sqrt(squares / frame.length),
                    analyser.kickEnergy, analyser.snareEnergy, analyser.kickOnset, analyser.snareOnset);
            System.arraycopy(frame, OnsetCurves.HOP_SIZE, frame, 0,
                    frame.length - OnsetCurves.HOP_SIZE);
            fill = frame.length - OnsetCurves.HOP_SIZE;
            consumer.accept(features);
        }
    }

    public static void checkRange(short[] pcm, int offset, int count) {
        if (pcm == null || offset < 0 || count < 0 || offset > pcm.length - count) {
            throw new IllegalArgumentException("invalid PCM range");
        }
    }
}
