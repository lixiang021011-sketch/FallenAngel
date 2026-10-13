package com.fallenangel.listeningai.clip;

import com.fallenangel.listeningai.localai.AudioFeatures;
import com.fallenangel.listeningai.localai.LocalInferenceEngine;
import com.fallenangel.listeningai.localai.MusicObservation;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * A model of audio, rather than a model of five numbers.
 *
 * The always-on analysis measures five things every five milliseconds, and that
 * is the right size for noticing that something happened. It is the wrong size
 * for recognising what: a kick and a bass note are the same five numbers. So
 * this engine is a second one, with its own frontend - the audio is resampled,
 * turned into log-mel, and only then does a model see it.
 *
 * It shares nothing with the rules except the shape of what comes out: an
 * observation is an observation, whoever produced it. That is what makes the
 * model replaceable, and what makes a model that fails to load harmless - this
 * engine simply produces nothing, and everything around it carries on.
 */
public final class OnnxEventEngine implements LocalInferenceEngine {
    /** What one run of a model produced; any of it may be absent. */
    public static final class ModelOutput {
        public final float[] scores;
        public final float[] embedding;
        public final float[] tags;

        public ModelOutput(float[] scores, float[] embedding, float[] tags) {
            this.scores = scores;
            this.embedding = embedding;
            this.tags = tags;
        }
    }

    /** The contract the .onnx file has to satisfy: log-mel clip in, four logits out. */
    public interface Model extends AutoCloseable {
        /**
         * @param logmel one clip, one channel, {@code mels} by {@code frames},
         *               row-major, highest band last
         * @param spec   which named outputs to read and what they mean
         */
        ModelOutput run(float[] logmel, ModelSpec spec);

        @Override default void close() {
        }
    }

    private final ModelSpec spec;
    private final LogMel frontend;
    private final Model model;
    /** The last {@code frames} log-mel frames, oldest first, frame-major. */
    private final float[] window;
    /** What the model is fed: the window, or a normalised copy of it. */
    private final float[] modelInput;
    /** Audio at the model's rate, newest last, exactly as long as a window needs. */
    private final float[] ring;
    /** A hop of audio at the model's rate, waiting to be moved into the ring. */
    private final float[] hop;
    private Resampler.Stream stream;
    private int pendingFill;
    private int windowFill;
    private boolean warmed;
    private long specSamples;
    private final int strideHops;
    private int hopsSinceRun;
    private long samples;          // capture clock, in source samples
    private long nextFrameAt;      // in the model's own rate, for observation times
    private double previousRms;
    private boolean closed;
    /**
     * Where the model's work happens.
     *
     * Running it on the caller's thread would make the capture wait for it, and
     * the capture has an audio buffer behind it: a run longer than that buffer
     * is audio nobody will hear again. So the window is copied and handed to
     * this, and a stride that arrives while the previous one is still being
     * worked on is dropped - the next one is half a second away, and the music
     * keeps arriving either way. Tests pass something that runs inline.
     */
    private final java.util.concurrent.Executor dispatch;
    private final java.util.concurrent.atomic.AtomicBoolean busy =
            new java.util.concurrent.atomic.AtomicBoolean();
    /** Reads and writes happen on the dispatch thread, one run at a time. */
    private final java.util.concurrent.ConcurrentLinkedQueue<MusicObservation> pending =
            new java.util.concurrent.ConcurrentLinkedQueue<>();
    private volatile boolean failed;

    public OnnxEventEngine(Model model, ModelSpec spec) {
        this(model, spec, new LogMel(spec.features));
    }

    public OnnxEventEngine(Model model, ModelSpec spec, LogMel frontend) {
        this(model, spec, frontend, Runnable::run);
    }

    public OnnxEventEngine(Model model, ModelSpec spec, LogMel frontend,
                           java.util.concurrent.Executor dispatch) {
        spec.validate();
        this.model = model;
        this.spec = spec;
        this.frontend = frontend;
        this.dispatch = dispatch;
        this.window = new float[spec.mels * spec.frames];
        this.modelInput = spec.features.normalise ? new float[window.length] : window;
        this.ring = new float[frontend.ringSamples(spec.frames)];
        this.hop = new float[spec.features.hop];
        this.stream = new Resampler.Stream(SAMPLE_RATE, spec.features.sampleRate);
        this.strideHops = spec.strideHops();
    }

    public ModelSpec spec() {
        return spec;
    }

    /**
     * Load the runtime and open a model file.
     *
     * The only place the native library is touched, so that everything else in
     * this class can be tested without it.
     */
    public static Model open(byte[] modelBytes) throws Exception {
        ai.onnxruntime.OrtEnvironment environment = ai.onnxruntime.OrtEnvironment.getEnvironment();
        ai.onnxruntime.OrtSession session = environment.createSession(modelBytes,
                new ai.onnxruntime.OrtSession.SessionOptions());
        return new Model() {
            @Override public ModelOutput run(float[] logmel, ModelSpec spec) {
                long[] shape = spec.layout == ModelSpec.Layout.FRAMES_MELS_3D
                        ? new long[] {1, spec.frames, spec.mels}
                        : new long[] {1, 1, spec.mels, spec.frames};
                try (ai.onnxruntime.OnnxTensor tensor = ai.onnxruntime.OnnxTensor.createTensor(
                        environment, java.nio.FloatBuffer.wrap(arrange(logmel, spec)), shape)) {
                    try (ai.onnxruntime.OrtSession.Result result =
                                 session.run(java.util.Map.of(spec.inputName, tensor))) {
                        float[] scores = null, embedding = null, tags = null;
                        for (ModelSpec.Output output : spec.outputs) {
                            java.util.Optional<ai.onnxruntime.OnnxValue> found =
                                    result.get(output.name);
                            Object value = found.isPresent() ? found.get().getValue() : null;
                            float[] flat = flatten(value);
                            if (flat == null) continue;
                            switch (output.role) {
                                case SCORES: scores = flat; break;
                                case EMBEDDING: embedding = flat; break;
                                case TAGS: tags = flat; break;
                            }
                        }
                        return new ModelOutput(scores, embedding, tags);
                    }
                } catch (Exception e) {
                    throw new IllegalStateException("inference failed", e);
                }
            }

            /**
             * The clip in the order the graph asked for.
             *
             * The frontend produces frames of bands, and that is what a
             * sequence model wants. A mel image wants the transpose, and it is
             * the kind of transpose that does not fail: a square 64 by 64 clip
             * is its own transpose, so this went unnoticed until a model with
             * 187 frames and 96 bands arrived.
             */
            private float[] arrange(float[] logmel, ModelSpec spec) {
                if (spec.layout == ModelSpec.Layout.FRAMES_MELS_3D) return logmel;
                float[] transposed = new float[logmel.length];
                for (int frame = 0; frame < spec.frames; frame++) {
                    for (int mel = 0; mel < spec.mels; mel++) {
                        transposed[mel * spec.frames + frame] = logmel[frame * spec.mels + mel];
                    }
                }
                return transposed;
            }

            @Override public void close() {
                try {
                    session.close();
                } catch (Exception ignored) {
                }
            }
        };
    }

    /** Whatever shape the graph returned, as one flat vector. */
    private static float[] flatten(Object value) {
        if (value instanceof float[][]) {
            float[][] outer = (float[][]) value;
            return outer.length == 0 ? null : outer[0];
        }
        if (value instanceof float[]) return (float[]) value;
        return null;
    }

    @Override
    public void pushPcm(short[] pcm, int offset, int count) {
        requireOpen();
        if (failed) throw new IllegalStateException("the model failed while inference was running");
        if (count <= 0) return;
        // Resample as it arrives: the model's frontend is 16 kHz, and holding
        // the original as well would be holding audio twice for nothing.
        stream.accept(pcm, offset, count, this::append);
        samples += count;
    }

    private void append(float value) {
        hop[pendingFill++] = value;
        if (pendingFill == hop.length) {
            pendingFill = 0;
            advance();
        }
    }

    /**
     * One hop of audio has arrived: move it into the window and look.
     *
     * A hop is sixteen milliseconds, and everything here happens once per hop
     * rather than once per sample, which is what makes a three second window
     * affordable: one short FFT and one array shift, not a thousand of either.
     */
    private void advance() {
        System.arraycopy(ring, hop.length, ring, 0, ring.length - hop.length);
        System.arraycopy(hop, 0, ring, ring.length - hop.length, hop.length);
        specSamples += hop.length;
        nextFrameAt += spec.features.hop;
        if (!warmed) {
            // The first full ring already contains a whole window of music, so
            // it is turned into one in a single step and the model can be asked
            // as soon as there is three seconds to ask about. Waiting for the
            // frames to arrive one hop at a time would delay the first answer
            // by the length of the window all over again.
            // Not until the ring holds a window of real audio: filling the
            // window from a buffer that is still mostly silence would have the
            // model classify three seconds of nothing.
            if (specSamples < ring.length) return;
            float[][] rows = frontend.newestRows(ring, spec.frames);
            windowFill = rows.length;
            for (int frame = 0; frame < rows.length; frame++) {
                System.arraycopy(rows[frame], 0, window,
                        (spec.frames - rows.length + frame) * spec.mels, spec.mels);
            }
            warmed = true;
        } else {
            float[] row = frontend.newestFrame(ring);
            if (row == null) return;
            System.arraycopy(window, spec.mels, window, 0, window.length - spec.mels);
            System.arraycopy(row, 0, window, window.length - spec.mels, spec.mels);
            // The window is one row fuller than it was. The first fill cannot
            // always provide every row - a centred frontend leaves the newest
            // frame short of complete, so the buffer holds one fewer than the
            // model wants - and forgetting to count them here means the window
            // never finishes filling and the model is never asked.
            windowFill = Math.min(spec.frames, windowFill + 1);
        }
        if (windowFill < spec.frames) return;
        if (++hopsSinceRun < strideHops) return;
        hopsSinceRun = 0;
        step();
    }

    /** One new frame: shift the window and, when it is full, ask the model. */
    private void step() {
        if (failed) return;
        // Before anything is copied: the array a run in flight is reading must
        // not be written over by the stride that decides to skip itself.
        if (busy.get()) return;
        float[] input = prepared();
        double rms = rms();
        long atSample = sampleOfThisFrame();
        long forSegment = segment;
        if (!busy.compareAndSet(false, true)) return;
        long started = System.nanoTime();
        dispatch.execute(() -> {
            try {
                if (forSegment != segment) return;   // the song moved on
                ModelOutput output = model.run(input, spec);
                lastRunNanos = System.nanoTime() - started;
                emit(output, input, rms, atSample, forSegment);
            } catch (Throwable t) {
                // A model that throws is off, not a crash: this is the one place
                // that finds out, and the caller hears about it on its next push.
                failed = true;
            } finally {
                busy.set(false);
            }
        });
    }

    /** What one run of the model means, once it has come back. */
    private void emit(ModelOutput output, float[] input, double rms, long atSample,
                      long forSegment) {
        if (output == null) output = new ModelOutput(null, null, null);
        AudioFeatures features = features(rms, input, atSample);

        if (output.scores != null) {
            double[] probabilities = softmax(output.scores);
            for (int i = 0; i < spec.classes.size() && i < probabilities.length; i++) {
                if (probabilities[i] < spec.threshold) continue;
                pending.add(MusicObservation.of(spec.classes.get(i), probabilities[i],
                        features, forSegment, "onnx", output.embedding, output.tags));
            }
        }

        // A tag output is a multi-label classifier's last layer, so it is a
        // column of logits and each one becomes its own probability. Left as
        // they arrive they are negative for most of the labels, which reads as
        // "not this" only if you know to expect it.
        float[] tags = logistic(output.tags);

        // A model that thinks in vectors has nothing to say in four classes, so
        // what it says instead is "this does not sound like the last few
        // seconds". The embedding travels with the observation: the score is
        // ours, the evidence is the model's.
        if (output.embedding != null) {
            double novelty = noveltyOf(output.embedding);
            pending.add(MusicObservation.of(MusicObservation.Type.NOVELTY, novelty,
                    features, forSegment, "onnx", output.embedding, tags));
            remember(output.embedding);
        }
        previousRms = rms;
    }

    /** One probability per logit, or nothing when there are no tags. */
    static float[] logistic(float[] logits) {
        if (logits == null) return null;
        float[] out = new float[logits.length];
        for (int i = 0; i < logits.length; i++) {
            out[i] = (float) (1 / (1 + Math.exp(-logits[i])));
        }
        return out;
    }

    /**
     * How long the last inference took.
     *
     * The capture thread runs this, so this is the number that says whether the
     * phone can keep up: a run longer than the audio the capture buffer holds is
     * audio the listener never hears again.
     */
    private volatile long lastRunNanos;

    public long lastRunMillis() {
        return lastRunNanos / 1_000_000;
    }

    /**
     * The window as the model should see it.
     *
     * The whole window is normalised as one clip rather than frame by frame:
     * a frame's own mean and spread would flatten the dynamics inside the
     * window, which is the opposite of what the normalisation is for.
     */
    private float[] prepared() {
        // Always a copy: the window keeps moving while the model is reading it.
        System.arraycopy(window, 0, modelInput, 0, window.length);
        if (spec.features.normalise) LogMel.normalise(modelInput);
        return modelInput;
    }

    /** The last few embeddings, so novelty is measured against recent music. */
    private final java.util.ArrayDeque<float[]> history = new java.util.ArrayDeque<>();

    /**
     * How unlike the recent past this clip is, 0 to 1.
     *
     * Cosine distance to the most similar of the remembered windows, so a
     * passage that slowly drifts is not called novel at every step: only a clip
     * that resembles none of what came before is.
     */
    private double noveltyOf(float[] embedding) {
        if (history.isEmpty()) return 0;
        double mostSimilar = -1;
        for (float[] past : history) mostSimilar = Math.max(mostSimilar, cosine(embedding, past));
        return Math.max(0, Math.min(1, 1 - mostSimilar));
    }

    private void remember(float[] embedding) {
        history.addLast(embedding.clone());
        while (history.size() > spec.noveltyHistory) history.removeFirst();
    }

    /** Cosine similarity, 1 when the two point the same way. */
    static double cosine(float[] a, float[] b) {
        if (a == null || b == null) return 0;
        double dot = 0, na = 0, nb = 0;
        int length = Math.min(a.length, b.length);
        for (int i = 0; i < length; i++) {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        if (na <= 0 || nb <= 0) return 0;
        return dot / Math.sqrt(na * nb);
    }

    private double rms() {
        double sum = 0;
        for (float value : ring) sum += value * value;
        return Math.sqrt(sum / ring.length);
    }

    /**
     * The five numbers the observation carries.
     *
     * Not a second measurement - these are read back out of what the model was
     * shown, because the observation type is shared and has to be filled with
     * something. The bands come from the window's own low and high quarters, so
     * they agree with what the model saw rather than competing with it.
     */
    private AudioFeatures features(double rms, float[] seen, long atSample) {
        double low = bandMean(seen, 0, spec.mels / 4);
        double high = bandMean(seen, spec.mels - spec.mels / 4, spec.mels);
        double onset = Math.max(0, rms - previousRms);
        return new AudioFeatures(atSample, rms,
                spec.features.power(low), spec.features.power(high), onset, onset);
    }

    private long sampleOfThisFrame() {
        return (long) (nextFrameAt * SAMPLE_RATE / (double) spec.features.sampleRate);
    }

    private double bandMean(float[] seen, int from, int to) {
        double sum = 0;
        for (int frame = 0; frame < spec.frames; frame++) {
            for (int mel = from; mel < to; mel++) sum += seen[frame * spec.mels + mel];
        }
        return sum / (spec.frames * (to - from));
    }

    static double[] softmax(float[] logits) {
        double[] out = new double[logits.length];
        double max = Double.NEGATIVE_INFINITY;
        for (float logit : logits) max = Math.max(max, logit);
        double total = 0;
        for (int i = 0; i < logits.length; i++) {
            out[i] = Math.exp(logits[i] - max);
            total += out[i];
        }
        for (int i = 0; i < out.length; i++) out[i] = total <= 0 ? 0 : out[i] / total;
        return out;
    }

    private long segment;

    @Override
    public void reset(long firstSample) {
        requireOpen();
        java.util.Arrays.fill(window, 0);
        java.util.Arrays.fill(ring, 0);
        pending.clear();
        history.clear();
        previousRms = 0;
        pendingFill = 0;
        windowFill = 0;
        warmed = false;
        specSamples = 0;
        hopsSinceRun = 0;
        nextFrameAt = 0;
        samples = firstSample;
        stream = new Resampler.Stream(SAMPLE_RATE, spec.features.sampleRate);
        segment++;
    }

    @Override
    public List<MusicObservation> drainObservations() {
        requireOpen();
        // Polled rather than copied-then-cleared: the model runs on its own
        // thread now, and an observation that arrives between the copy and the
        // clear would be thrown away.
        MusicObservation first = pending.poll();
        if (first == null) return Collections.emptyList();
        List<MusicObservation> out = new ArrayList<>();
        out.add(first);
        for (MusicObservation next = pending.poll(); next != null; next = pending.poll()) {
            out.add(next);
        }
        return out;
    }

    @Override
    public long droppedObservations() {
        return 0;
    }

    @Override
    public void close() {
        if (closed) return;
        closed = true;
        pending.clear();
        if (dispatch instanceof java.util.concurrent.ExecutorService) {
            ((java.util.concurrent.ExecutorService) dispatch).shutdownNow();
        }
        model.close();
    }

    private void requireOpen() {
        if (closed) throw new IllegalStateException("engine is closed");
    }
}
