package com.fallenangel.listeningai.localai;

import java.util.Objects;

/** Evidence about heard audio, not a chart note or an instruction to control playback. */
public final class MusicObservation {
    public enum Type { ONSET, KICK, SNARE, NOVELTY }

    public final Type type;
    /** Heuristic score for rules-v1; not a calibrated probability. */
    public final double confidence;
    public final AudioFeatures features;
    public final long segmentId;
    public final String modelId;
    public final double audioTimeSeconds;
    /**
     * What the model thought, when the model thinks in vectors.
     *
     * A model that hears music as an embedding has no opinion to express as one
     * of four numbers, and forcing it into them would throw away the only thing
     * it produces. Null when there is nothing to carry - the rules have none.
     */
    public final float[] embedding;
    /** Optional tag probabilities, in the order the model's description names them. */
    public final float[] tags;

    MusicObservation(Type type, double confidence, AudioFeatures features,
                     long segmentId, String modelId) {
        this(type, confidence, features, segmentId, modelId, null, null);
    }

    MusicObservation(Type type, double confidence, AudioFeatures features,
                     long segmentId, String modelId, float[] embedding, float[] tags) {
        this.type = Objects.requireNonNull(type);
        if (!Double.isFinite(confidence) || confidence < 0 || confidence > 1) {
            throw new IllegalArgumentException("confidence must be in [0,1]");
        }
        this.confidence = confidence;
        this.features = Objects.requireNonNull(features);
        this.segmentId = segmentId;
        this.modelId = Objects.requireNonNull(modelId);
        audioTimeSeconds = features.endSample / (double) LocalInferenceEngine.SAMPLE_RATE;
        this.embedding = embedding;
        this.tags = tags;
    }

    /**
     * The same observation, made from outside this package.
     *
     * A model with its own frontend is a peer of the rules, not a subclass of
     * them, and it lives where its frontend lives. What it produces is still
     * this type, so here is the door.
     */
    public static MusicObservation of(Type type, double confidence, AudioFeatures features,
                                      long segmentId, String modelId) {
        return new MusicObservation(type, confidence, features, segmentId, modelId);
    }

    /** The same, carrying what a vector model produced alongside the event. */
    public static MusicObservation of(Type type, double confidence, AudioFeatures features,
                                      long segmentId, String modelId,
                                      float[] embedding, float[] tags) {
        return new MusicObservation(type, confidence, features, segmentId, modelId,
                embedding, tags);
    }
}
