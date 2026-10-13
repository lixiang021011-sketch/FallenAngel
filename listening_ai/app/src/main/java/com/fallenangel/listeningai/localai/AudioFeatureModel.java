package com.fallenangel.listeningai.localai;

import java.util.List;
import java.util.Objects;

/** Adapter boundary for rules or a future ONNX/LiteRT model trained on AudioFeatures.SCHEMA.
 * Stateful models own their context window, normalization and native resources here.
 * Models needing a different frontend implement LocalInferenceEngine instead.
 */
public interface AudioFeatureModel extends AutoCloseable {
    String id();
    List<Prediction> infer(AudioFeatures features);
    void reset();
    @Override default void close() { }

    final class Prediction {
        public final MusicObservation.Type type;
        public final double confidence;

        public Prediction(MusicObservation.Type type, double confidence) {
            this.type = Objects.requireNonNull(type);
            if (!Double.isFinite(confidence) || confidence < 0 || confidence > 1) {
                throw new IllegalArgumentException("invalid model score");
            }
            this.confidence = confidence;
        }
    }
}
