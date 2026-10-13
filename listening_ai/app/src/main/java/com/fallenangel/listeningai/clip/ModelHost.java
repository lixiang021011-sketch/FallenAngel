package com.fallenangel.listeningai.clip;

import android.content.Context;
import android.content.res.AssetManager;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;

/**
 * Finding, describing and loading a model, and coping when it is not there.
 *
 * Two rules shape this class. A model is described by data, so the next one is
 * a file and a manifest rather than a code change - and a model that cannot be
 * loaded is not an error to report up, it is simply a model that is not
 * running. Everything that captures audio, notices change and asks questions
 * carries on without it, because none of it ever depended on this.
 */
public final class ModelHost {
    /** Where a manifest would be, if one is shipped with the app. */
    private static final String MANIFEST = "models/models.json";

    public static final class Loaded {
        public final OnnxEventEngine engine;
        public final ModelSpec spec;
        public final long loadMillis;
        /** Null when the model is running; why not, when it is not. */
        public final String failure;

        Loaded(OnnxEventEngine engine, ModelSpec spec, long loadMillis, String failure) {
            this.engine = engine;
            this.spec = spec;
            this.loadMillis = loadMillis;
            this.failure = failure;
        }

        public boolean ok() {
            return engine != null;
        }
    }

    private ModelHost() {
    }

    /**
     * The description to use: the shipped manifest if there is one, otherwise
     * the placeholder. Nothing here reads the model file itself - a description
     * that cannot be read is a failure like any other.
     */
    public static ModelSpec spec(Context context) {
        try {
            AssetManager assets = context.getAssets();
            try (InputStream in = assets.open(MANIFEST)) {
                String text = new String(readAll(in), StandardCharsets.UTF_8);
                return ModelSpec.fromJson(new JSONObject(text));
            }
        } catch (Exception missing) {
            return ModelSpec.placeholder();
        }
    }

    /** Open the model the spec describes. Never throws: a failure is a result. */
    public static Loaded open(Context context, ModelSpec spec) {
        long started = System.nanoTime();
        try {
            spec.validate();
            LogMel frontend = frontend(context, spec);
            byte[] bytes = read(context, spec.path);
            // Inference gets a thread of its own; the capture thread has an audio
            // buffer behind it and cannot afford to wait for a network.
            OnnxEventEngine engine = new OnnxEventEngine(
                    OnnxEventEngine.open(bytes), spec, frontend,
                    java.util.concurrent.Executors.newSingleThreadExecutor(
                            task -> new Thread(task, "audio-model")));
            return new Loaded(engine, spec, (System.nanoTime() - started) / 1_000_000, null);
        } catch (Throwable t) {
            return new Loaded(null, spec, (System.nanoTime() - started) / 1_000_000,
                    t.getClass().getSimpleName() + ": " + t.getMessage());
        }
    }

    /**
     * The frontend the model was trained with.
     *
     * A spec that names one and cannot have it read is a failure, not a reason
     * to derive our own: the model would run, on the wrong shape of input, and
     * there is no version of that which is better than not running.
     */
    private static LogMel frontend(Context context, ModelSpec spec) throws Exception {
        if (spec.features.frontend == null) return new LogMel(spec.features);
        String text = new String(read(context, spec.features.frontend),
                StandardCharsets.UTF_8);
        return new LogMel(spec.features, Frontend.fromJson(new JSONObject(text)));
    }

    /** A model is either an asset or, for testing, a file path. */
    private static byte[] read(Context context, String path) throws Exception {
        if (path.startsWith("/")) {
            try (InputStream in = new FileInputStream(new File(path))) {
                return readAll(in);
            }
        }
        try (InputStream in = context.getAssets().open(path)) {
            return readAll(in);
        }
    }

    private static byte[] readAll(InputStream in) throws Exception {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = in.read(buffer)) > 0) out.write(buffer, 0, read);
        return out.toByteArray();
    }
}
