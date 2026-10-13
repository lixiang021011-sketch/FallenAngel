package com.fallenangel.listeningai.clip;

import com.fallenangel.listeningai.localai.MusicObservation;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

/**
 * Everything about a model that is not the model.
 *
 * Which file, what its input is called and shaped, what its output means, where
 * the line is between "an event" and "no event", and how to turn audio into what
 * it eats. All of it as data, because the point of this layer is that the next
 * model is a file and a description rather than a code change.
 *
 * A model whose description does not match its file does not throw: it returns
 * numbers of the wrong length or in the wrong order, and the results are
 * plausible enough to be believed. So the description is validated on load, and
 * a model that does not match its own description is refused.
 */
public final class ModelSpec {
    /** What one of the model's outputs is for. */
    public enum Role {
        /** Event probabilities, in {@link #classes} order. */
        SCORES,
        /** A music embedding: one vector describing the clip. */
        EMBEDDING,
        /**
         * Tag scores in {@link #tagNames} order, turned into probabilities on
         * the way out.
         *
         * A tagger's last layer is linear, so what it emits are logits. The
         * reference reader for the first real model here puts them through a
         * sigmoid before anyone sees them, and until it does they are negative
         * numbers that look like probabilities.
         */
        TAGS
    }

    /**
     * How the graph wants its input arranged.
     *
     * The graph is fed one clip in the order the frontend produced it - frames
     * of mel bands, oldest first - and this says how that has to be shaped. It
     * is here rather than in the loader because getting it wrong is silent: a
     * square 64 by 64 clip transposes into something the model is happy to eat,
     * and a 187 by 96 one does not.
     */
    public enum Layout {
        /** {@code [1, 1, mels, frames]} - a mel image, bands down. */
        MELS_FRAMES_4D,
        /** {@code [1, frames, mels]} - a sequence of mel frames, time first. */
        FRAMES_MELS_3D
    }

    /** One named output of the graph, and what to do with it. */
    public static final class Output {
        public final String name;
        public final Role role;

        public Output(String name, Role role) {
            this.name = name;
            this.role = role;
        }
    }

    /** Where the file is: an asset path, or an absolute file path. */
    public final String path;
    public final String inputName;
    /** Every output the graph has, and what each one means. */
    public final List<Output> outputs;
    public final int mels;
    public final int frames;
    /** What each output means, in the order the model emits them. */
    public final List<MusicObservation.Type> classes;
    /** Above this probability an event is reported. */
    public final double threshold;
    /** Names of the tag outputs, when the model has any. */
    public final List<String> tagNames;
    /**
     * How many previous embeddings a novelty is measured against.
     *
     * One is the previous window; more is a memory of the last few, so a
     * passage that slowly drifts is not reported as novel every time it moves a
     * little - the comparison is against what the music has been.
     */
    public final int noveltyHistory;
    public final FeatureSpec features;
    public final Layout layout;
    /**
     * How often the model is asked, in milliseconds; zero means every hop.
     *
     * A hop is sixteen milliseconds, which is the right rate for reporting an
     * event and the wrong rate for running a network over three seconds of
     * audio: sixty times a second is how a phone gets hot. The frames still
     * arrive one per hop, because the window has to be the real sequence; only
     * the looking is spaced out.
     */
    public final int strideMillis;

    public ModelSpec(String path, String inputName, List<Output> outputs, int mels, int frames,
                     List<MusicObservation.Type> classes, double threshold,
                     List<String> tagNames, int noveltyHistory, FeatureSpec features) {
        this(path, inputName, outputs, mels, frames, classes, threshold, tagNames,
                noveltyHistory, features, Layout.MELS_FRAMES_4D, 0);
    }

    public ModelSpec(String path, String inputName, List<Output> outputs, int mels, int frames,
                     List<MusicObservation.Type> classes, double threshold,
                     List<String> tagNames, int noveltyHistory, FeatureSpec features,
                     Layout layout, int strideMillis) {
        this.path = path;
        this.inputName = inputName;
        this.outputs = new ArrayList<>(outputs);
        this.mels = mels;
        this.frames = frames;
        this.classes = new ArrayList<>(classes);
        this.threshold = threshold;
        this.tagNames = new ArrayList<>(tagNames);
        this.noveltyHistory = Math.max(1, noveltyHistory);
        this.features = features;
        this.layout = layout == null ? Layout.MELS_FRAMES_4D : layout;
        this.strideMillis = Math.max(0, strideMillis);
    }

    /** The stride in hops of the frontend, which is the unit the stream counts in. */
    public int strideHops() {
        if (strideMillis <= 0) return 1;
        int millisPerHop = (int) Math.round(1000.0 * features.hop / features.sampleRate);
        return Math.max(1, stridesFor(strideMillis, millisPerHop));
    }

    private static int stridesFor(int strideMillis, int millisPerHop) {
        return millisPerHop <= 0 ? 1 : (int) Math.round(strideMillis / (double) millisPerHop);
    }

    public void validate() {
        if (path == null || path.isEmpty()) throw new IllegalArgumentException("no model path");
        if (inputName == null || inputName.isEmpty()) throw new IllegalArgumentException("no input name");
        if (outputs.isEmpty()) throw new IllegalArgumentException("no outputs described");
        if (mels <= 0 || frames <= 0) throw new IllegalArgumentException("empty input shape");
        boolean scores = false, embedding = false;
        for (Output output : outputs) {
            if (output.name == null || output.name.isEmpty()) {
                throw new IllegalArgumentException("an output has no name");
            }
            if (output.role == Role.SCORES) scores = true;
            if (output.role == Role.EMBEDDING) embedding = true;
            if (output.role == Role.TAGS && tagNames.isEmpty()) {
                throw new IllegalArgumentException("a tag output with no tag names");
            }
        }
        if (scores && classes.isEmpty()) {
            throw new IllegalArgumentException("a score output with no classes");
        }
        if (!scores && !embedding) {
            throw new IllegalArgumentException("a model has to produce something to report");
        }
        if (!(threshold >= 0) || threshold > 1) throw new IllegalArgumentException("threshold outside [0,1]");
        features.validate();
        if (features.mels != mels) {
            throw new IllegalArgumentException("the frontend makes " + features.mels
                    + " mel bands but the model wants " + mels);
        }
    }

    /** The description of the placeholder, which is what ships today. */
    public static ModelSpec placeholder() {
        return new ModelSpec("models/placeholder-events.onnx", "logmel",
                java.util.Collections.singletonList(new Output("logits", Role.SCORES)),
                64, 64,
                Arrays.asList(MusicObservation.Type.ONSET, MusicObservation.Type.KICK,
                        MusicObservation.Type.SNARE, MusicObservation.Type.NOVELTY),
                0.5, java.util.Collections.emptyList(), 1, FeatureSpec.PLACEHOLDER,
                Layout.MELS_FRAMES_4D, 0);
    }

    public JSONObject toJson() {
        JSONObject out = new JSONObject();
        try {
            out.put("path", path);
            out.put("inputName", inputName);
            JSONArray outputsJson = new JSONArray();
            for (Output output : outputs) {
                outputsJson.put(new JSONObject().put("name", output.name)
                        .put("role", output.role.name()));
            }
            out.put("outputs", outputsJson);
            out.put("mels", mels);
            out.put("frames", frames);
            JSONArray names = new JSONArray();
            for (MusicObservation.Type type : classes) names.put(type.name());
            out.put("classes", names);
            out.put("threshold", threshold);
            out.put("tagNames", new JSONArray(tagNames));
            out.put("noveltyHistory", noveltyHistory);
            out.put("features", features.toJson());
            out.put("layout", layout.name());
            out.put("strideMillis", strideMillis);
        } catch (Exception ignored) {
        }
        return out;
    }

    public static ModelSpec fromJson(JSONObject json) {
        if (json == null) return placeholder();
        List<MusicObservation.Type> classes = new ArrayList<>();
        JSONArray names = json.optJSONArray("classes");
        if (names != null) {
            for (int i = 0; i < names.length(); i++) {
                try {
                    classes.add(MusicObservation.Type.valueOf(names.getString(i)));
                } catch (Exception unknown) {
                    throw new IllegalArgumentException("unknown class: " + names.optString(i));
                }
            }
        }
        List<Output> outputs = new ArrayList<>();
        JSONArray described = json.optJSONArray("outputs");
        if (described != null) {
            for (int i = 0; i < described.length(); i++) {
                JSONObject item = described.optJSONObject(i);
                if (item == null) continue;
                try {
                    outputs.add(new Output(item.optString("name"),
                            Role.valueOf(item.optString("role", "SCORES"))));
                } catch (Exception unknown) {
                    throw new IllegalArgumentException("unknown output role: "
                            + item.optString("role"));
                }
            }
        } else if (!json.optString("outputName", "").isEmpty()) {
            // Manifests written before a model could have more than one output.
            outputs.add(new Output(json.optString("outputName"), Role.SCORES));
        }
        List<String> tagNames = new ArrayList<>();
        JSONArray tags = json.optJSONArray("tagNames");
        if (tags != null) {
            for (int i = 0; i < tags.length(); i++) tagNames.add(tags.optString(i));
        }
        ModelSpec spec = new ModelSpec(
                json.optString("path", ""),
                json.optString("inputName", "logmel"),
                outputs,
                json.optInt("mels", 64),
                json.optInt("frames", 64),
                classes,
                json.optDouble("threshold", 0.5),
                tagNames,
                json.optInt("noveltyHistory", 1),
                FeatureSpec.fromJson(json.optJSONObject("features")),
                layoutOf(json.optString("layout", Layout.MELS_FRAMES_4D.name())),
                json.optInt("strideMillis", 0));
        spec.validate();
        return spec;
    }

    private static Layout layoutOf(String name) {
        try {
            return Layout.valueOf(name);
        } catch (Exception unknown) {
            throw new IllegalArgumentException("unknown input layout: " + name);
        }
    }

    @Override
    public String toString() {
        return path + " [" + mels + "x" + frames + "] " + classes
                + " at " + threshold + " over " + features;
    }
}
