package com.fallenangel.listeningai.localai;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import static com.fallenangel.listeningai.localai.MusicObservation.Type.*;

/** Small causal placeholder. Drum labels describe frequency-band evidence, not source separation. */
public final class RuleAudioModel implements AudioFeatureModel {
    private long lastOnset = -LocalInferenceEngine.SAMPLE_RATE;
    private long lastNovelty = -LocalInferenceEngine.SAMPLE_RATE;
    private int frames;
    private double meanRms, meanHighShare, meanOnset, previousRms;

    @Override public String id() { return "rules-v1"; }

    @Override public void reset() {
        lastOnset = lastNovelty = -LocalInferenceEngine.SAMPLE_RATE;
        frames = 0;
        meanRms = meanHighShare = meanOnset = previousRms = 0;
    }

    @Override public List<Prediction> infer(AudioFeatures f) {
        double highShare = f.snareEnergy / (f.kickEnergy + f.snareEnergy + 1e-12);
        double strength = Math.max(f.kickOnset, f.snareOnset);
        if (frames++ == 0) {
            // The frontend's first log difference has no previous audio reference.
            meanRms = f.rms;
            previousRms = f.rms;
            meanHighShare = highShare;
            meanOnset = 0;
            return Collections.emptyList();
        }
        List<Prediction> out = new ArrayList<>(3);
        double threshold = Math.max(0.7, meanOnset * 2.5);
        // A hard note ending can create spectral flux too; require rising broadband energy.
        if (f.rms >= 0.004 && f.rms > previousRms * 1.005 && strength > threshold
                && f.endSample - lastOnset >= LocalInferenceEngine.SAMPLE_RATE * 0.08) {
            double score = Math.min(0.99, 0.5 + 0.49 * (1 - threshold / strength));
            out.add(new Prediction(ONSET, score));
            // Avoid calling tiny spectral leakage a drum hit.
            if (f.kickEnergy > f.snareEnergy && f.kickOnset > threshold) {
                out.add(new Prediction(KICK, score));
            } else if (f.snareOnset > threshold && f.snareEnergy > f.kickEnergy) {
                out.add(new Prediction(SNARE, score));
            }
            lastOnset = f.endSample;
        }
        double levelChange = Math.abs(Math.log((f.rms + 0.004) / (meanRms + 0.004)));
        double timbreChange = Math.abs(highShare - meanHighShare);
        // Approx. one second of context; novelty means a level/timbre change, not a song section.
        if (frames > 200 && f.rms >= 0.004 && (levelChange > 0.9 || timbreChange > 0.55)
                && f.endSample - lastNovelty >= LocalInferenceEngine.SAMPLE_RATE) {
            out.add(new Prediction(NOVELTY, Math.min(0.99,
                    0.5 + 0.25 * Math.max(levelChange, timbreChange))));
            lastNovelty = f.endSample;
        }
        meanRms += 0.01 * (f.rms - meanRms);
        meanHighShare += 0.01 * (highShare - meanHighShare);
        meanOnset += 0.02 * (strength - meanOnset);
        previousRms = f.rms;
        return out;
    }
}
