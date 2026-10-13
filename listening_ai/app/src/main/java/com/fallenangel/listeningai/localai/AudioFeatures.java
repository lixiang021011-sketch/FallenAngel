package com.fallenangel.listeningai.localai;

/** Immutable v1 features. Frame time is its exclusive end on the capture sample clock. */
public final class AudioFeatures {
    public static final String SCHEMA = "overlay-bands-v1";
    public final long endSample;
    public final double rms, kickEnergy, snareEnergy, kickOnset, snareOnset;

    public AudioFeatures(long endSample, double rms, double kickEnergy,
                         double snareEnergy, double kickOnset, double snareOnset) {
        if (endSample < 0) throw new IllegalArgumentException("negative sample time");
        for (double value : new double[]{rms, kickEnergy, snareEnergy, kickOnset, snareOnset}) {
            if (!Double.isFinite(value) || value < 0) throw new IllegalArgumentException("invalid feature");
        }
        this.endSample = endSample;
        this.rms = rms;
        this.kickEnergy = kickEnergy;
        this.snareEnergy = snareEnergy;
        this.kickOnset = kickOnset;
        this.snareOnset = snareOnset;
    }

    /** Adapter tensor order, before any model-specific normalization. Caller owns the copy. */
    public float[] toTensor() {
        return new float[]{(float) rms, (float) kickEnergy, (float) snareEnergy,
                (float) kickOnset, (float) snareOnset};
    }
}
