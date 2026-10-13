package com.fallenangel.listeningai.clip;

import org.json.JSONObject;

/**
 * What the audio is turned into before a model sees it.
 *
 * These numbers are not ours to choose: they are whatever the model was trained
 * with, and a model fed a different mel range than it was trained on does not
 * fail - it produces confident nonsense, which is worse. So they are a value
 * that travels with the model rather than a constant in the code, and the
 * defaults here describe only the placeholder.
 *
 * The placeholder happens to want 16 kHz and 64 mel. Nothing about that is
 * special: the first real model is as likely to want 44.1 kHz and 128, and
 * taking STRUM's drum model as an example, it does.
 */
public final class FeatureSpec {
    /**
     * How the mel powers are turned into the numbers the model reads.
     *
     * This is not a detail of taste. A model trained on {@code log10(10000x+1)}
     * sees a different world from one trained on a floored natural log, and
     * feeding it the other one produces confident nonsense rather than an
     * error. So the transform travels with the model, in the same description
     * as the sample rate and the band count.
     */
    public enum Log {
        /** {@code ln(max(x, peak * floorRatio))} - a floor relative to this clip. */
        NATURAL_FLOOR,
        /** {@code log10(logScale * x + logOffset)} - a fixed, absolute curve. */
        LOG10_OFFSET
    }

    public final int sampleRate;
    public final int mels;
    public final int fft;
    public final int frame;
    public final int hop;
    public final double minHz;
    public final double maxHz;
    /** Where the log floor sits, relative to the loudest band in the clip. */
    public final double floorRatio;
    /** Whether the clip's own mean and spread are removed afterwards. */
    public final boolean normalise;
    /**
     * Whether the clip is first scaled to a fixed loudness.
     *
     * The captured audio is whatever the listener set the volume to, and a
     * model handed the same music at four times the level does not say the same
     * thing about it: measured against the first real model, a fourfold gain
     * change moved its embedding by 0.31 cosine, which is the size of the
     * novelty it reports for actual musical change. Removing the clip's own
     * level before the mel makes the volume slider inaudible to the model.
     */
    public final boolean normaliseLevel;
    public final Log log;
    public final double logScale;
    public final double logOffset;
    /**
     * A file holding the exact window and mel filters, when the model needs a
     * frontend this class cannot derive.
     *
     * Deriving them here is fine while the frontend is one we chose. A model
     * trained elsewhere was fed its trainer's mel, and no amount of care in
     * this file reproduces e.g. librosa's Slaney-normalised filters by accident:
     * the first real model measured 80x apart from our derived ones, and not by
     * a single factor - the error is per band. So the filters are exported
     * beside the model and read, rather than re-derived.
     */
    public final String frontend;

    /** The contract the placeholder model was built against. */
    public static final FeatureSpec PLACEHOLDER = new FeatureSpec(
            16000, 64, 512, 400, 160, 0, 8000, 1e-6, true, false,
            Log.NATURAL_FLOOR, 1, 0, null);

    public FeatureSpec(int sampleRate, int mels, int fft, int frame, int hop,
                       double minHz, double maxHz, double floorRatio, boolean normalise) {
        this(sampleRate, mels, fft, frame, hop, minHz, maxHz, floorRatio, normalise,
                false, Log.NATURAL_FLOOR, 1, 0, null);
    }

    public FeatureSpec(int sampleRate, int mels, int fft, int frame, int hop,
                       double minHz, double maxHz, double floorRatio, boolean normalise,
                       boolean normaliseLevel, Log log, double logScale, double logOffset,
                       String frontend) {
        this.sampleRate = sampleRate;
        this.mels = mels;
        this.fft = fft;
        this.frame = frame;
        this.hop = hop;
        this.minHz = minHz;
        this.maxHz = maxHz;
        this.floorRatio = floorRatio;
        this.normalise = normalise;
        this.normaliseLevel = normaliseLevel;
        this.log = log;
        this.logScale = logScale;
        this.logOffset = logOffset;
        this.frontend = frontend;
    }

    /** Everything that has to be true for this to be computable at all. */
    public void validate() {
        if (sampleRate <= 0) throw new IllegalArgumentException("sample rate must be positive");
        if (mels <= 0) throw new IllegalArgumentException("mel bins must be positive");
        if (Integer.bitCount(fft) != 1) throw new IllegalArgumentException("fft must be a power of two");
        if (frame <= 0 || frame > fft) throw new IllegalArgumentException("frame must fit the fft");
        if (hop <= 0 || hop > frame) throw new IllegalArgumentException("hop must be within the frame");
        if (minHz < 0 || maxHz <= minHz) throw new IllegalArgumentException("mel range is empty");
        if (maxHz > sampleRate / 2.0) {
            throw new IllegalArgumentException("mel range runs past the sample rate's Nyquist");
        }
        if (!(floorRatio > 0) || floorRatio > 1) {
            throw new IllegalArgumentException("floor ratio must be in (0,1]");
        }
        if (log == null) throw new IllegalArgumentException("no log transform");
        if (log == Log.LOG10_OFFSET) {
            if (!(logScale > 0)) throw new IllegalArgumentException("log scale must be positive");
            if (!(logOffset > 0)) throw new IllegalArgumentException("log offset must be positive");
        }
        if (frontend != null && frontend.isEmpty()) {
            throw new IllegalArgumentException("a frontend path is either absent or a path");
        }
    }

    /** How many frames a clip of this many samples produces. */
    public int framesFor(int samples) {
        return samples < frame ? 0 : (samples - frame) / hop + 1;
    }

    /**
     * The linear power a logged band value came from, for the numbers that
     * travel with an observation.
     *
     * These are read back out of what the model was shown, so they have to be
     * in the units the rest of the app uses, and those cannot depend on which
     * log the current model was trained with. The clamp is there because an
     * observation is not worth an infinity.
     */
    public double power(double logged) {
        double bounded = Math.max(-50, Math.min(50, logged));
        if (log == Log.LOG10_OFFSET) {
            return (Math.pow(10, bounded) - logOffset) / logScale;
        }
        return Math.exp(bounded);
    }

    public JSONObject toJson() {
        JSONObject out = new JSONObject();
        try {
            out.put("sampleRate", sampleRate);
            out.put("mels", mels);
            out.put("fft", fft);
            out.put("frame", frame);
            out.put("hop", hop);
            out.put("minHz", minHz);
            out.put("maxHz", maxHz);
            out.put("floorRatio", floorRatio);
            out.put("normalise", normalise);
            out.put("normaliseLevel", normaliseLevel);
            out.put("logKind", log.name());
            out.put("logScale", logScale);
            out.put("logOffset", logOffset);
            if (frontend != null) out.put("frontend", frontend);
        } catch (Exception ignored) {
        }
        return out;
    }

    /** Read one back, filling anything absent from the placeholder's contract. */
    public static FeatureSpec fromJson(JSONObject json) {
        if (json == null) return PLACEHOLDER;
        Log log = PLACEHOLDER.log;
        String kind = json.optString("logKind", "");
        if (!kind.isEmpty()) {
            try {
                log = Log.valueOf(kind);
            } catch (Exception unknown) {
                throw new IllegalArgumentException("unknown log transform: " + kind);
            }
        }
        String frontend = json.optString("frontend", "");
        FeatureSpec spec = new FeatureSpec(
                json.optInt("sampleRate", PLACEHOLDER.sampleRate),
                json.optInt("mels", PLACEHOLDER.mels),
                json.optInt("fft", PLACEHOLDER.fft),
                json.optInt("frame", PLACEHOLDER.frame),
                json.optInt("hop", PLACEHOLDER.hop),
                json.optDouble("minHz", PLACEHOLDER.minHz),
                json.optDouble("maxHz", PLACEHOLDER.maxHz),
                json.optDouble("floorRatio", PLACEHOLDER.floorRatio),
                json.optBoolean("normalise", PLACEHOLDER.normalise),
                json.optBoolean("normaliseLevel", PLACEHOLDER.normaliseLevel),
                log,
                json.optDouble("logScale", PLACEHOLDER.logScale),
                json.optDouble("logOffset", PLACEHOLDER.logOffset),
                frontend.isEmpty() ? null : frontend);
        spec.validate();
        return spec;
    }

    @Override
    public String toString() {
        return sampleRate + "Hz/" + mels + "mel/fft" + fft + "/frame" + frame + "/hop" + hop
                + "/" + log + (normaliseLevel ? "/level" : "")
                + (frontend == null ? "" : "/" + frontend);
    }
}
