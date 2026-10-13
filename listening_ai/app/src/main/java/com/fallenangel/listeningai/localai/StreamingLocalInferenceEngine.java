package com.fallenangel.listeningai.localai;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

public final class StreamingLocalInferenceEngine implements LocalInferenceEngine {
    public static final int DEFAULT_CAPACITY = 512;
    private final PcmFeatureExtractor extractor = new PcmFeatureExtractor();
    private final AudioFeatureModel model;
    private final String modelId;
    private final int capacity;
    private final ArrayDeque<MusicObservation> pending = new ArrayDeque<>();
    private long segmentId, dropped;
    private boolean closed;

    public StreamingLocalInferenceEngine() { this(new RuleAudioModel(), DEFAULT_CAPACITY); }

    public StreamingLocalInferenceEngine(AudioFeatureModel model, int capacity) {
        if (capacity < 1) throw new IllegalArgumentException("capacity must be positive");
        this.model = Objects.requireNonNull(model);
        modelId = Objects.requireNonNull(model.id());
        this.capacity = capacity;
        model.reset();
    }

    @Override public void pushPcm(short[] pcm, int offset, int count) {
        requireOpen();
        extractor.push(pcm, offset, count, f -> {
            for (AudioFeatureModel.Prediction prediction : model.infer(f)) {
                if (pending.size() == capacity) {
                    pending.removeFirst();
                    dropped++;
                }
                pending.addLast(new MusicObservation(prediction.type, prediction.confidence,
                        f, segmentId, modelId));
            }
        });
    }

    @Override public void reset(long firstSample) {
        requireOpen();
        extractor.reset(firstSample);
        model.reset();
        pending.clear();
        segmentId++;
    }

    @Override public List<MusicObservation> drainObservations() {
        requireOpen();
        List<MusicObservation> out = new ArrayList<>(pending);
        pending.clear();
        return out;
    }

    @Override public long droppedObservations() { return dropped; }

    @Override public void close() {
        if (closed) return;
        closed = true;
        pending.clear();
        model.close();
    }

    private void requireOpen() {
        if (closed) throw new IllegalStateException("engine is closed");
    }
}
