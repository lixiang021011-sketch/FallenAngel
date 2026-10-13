package com.fallenangel.listeningai.clip;

import org.json.JSONArray;
import org.json.JSONObject;

/**
 * The window and the mel filters: the two things that decide what a model
 * actually reads.
 *
 * They can be derived from a {@link FeatureSpec}, which is what the placeholder
 * uses, or read from a file that gives the exact numbers a model was trained
 * with. The second is not a nicety. Deriving a librosa mel here produced filters
 * whose total weight was eighty times the real ones and whose per-band error
 * varied by a factor of three - the same music would have reached the model as a
 * different shape, and a model that has never seen that shape answers
 * confidently anyway.
 *
 * Which frame of the grid a rolling buffer ends on is decided here too, because
 * it depends on how the trainer placed its frames: a centred frontend puts the
 * last sample at the centre of the last frame, so the newest frame whose window
 * is entirely real audio sits one step further back.
 */
public final class Frontend {
    public final int mels;
    public final int fft;
    public final int frame;
    public final int hop;
    /** Whether the trainer padded the signal so frames are centred on samples. */
    public final boolean center;
    public final double[] window;
    public final double[][] filters;

    public Frontend(int mels, int fft, int frame, int hop, boolean center,
                    double[] window, double[][] filters) {
        this.mels = mels;
        this.fft = fft;
        this.frame = frame;
        this.hop = hop;
        this.center = center;
        this.window = window;
        this.filters = filters;
    }

    /** The filters this project would have chosen: htk mel, plain triangles. */
    public static Frontend derived(FeatureSpec spec) {
        spec.validate();
        double[] window = new double[spec.frame];
        for (int i = 0; i < window.length; i++) {
            window[i] = 0.5 * (1 - Math.cos(2 * Math.PI * i / (spec.frame - 1)));
        }
        double lowMel = hzToMel(spec.minHz);
        double highMel = hzToMel(spec.maxHz);
        double[] points = new double[spec.mels + 2];
        for (int i = 0; i < points.length; i++) {
            points[i] = melToHz(lowMel + (highMel - lowMel) * i / (spec.mels + 1));
        }
        double[][] filters = new double[spec.mels][spec.fft / 2 + 1];
        for (int m = 0; m < spec.mels; m++) {
            double left = points[m];
            double centre = points[m + 1];
            double right = points[m + 2];
            for (int k = 0; k < filters[m].length; k++) {
                double hz = k * (double) spec.sampleRate / spec.fft;
                if (hz <= left || hz >= right) continue;
                filters[m][k] = hz <= centre
                        ? (hz - left) / Math.max(1e-9, centre - left)
                        : (right - hz) / Math.max(1e-9, right - centre);
            }
        }
        return new Frontend(spec.mels, spec.fft, spec.frame, spec.hop, false, window, filters);
    }

    /** The window and filters a model was exported with. */
    public static Frontend fromJson(JSONObject json) {
        if (json == null) throw new IllegalArgumentException("no frontend description");
        JSONArray windowJson = json.optJSONArray("window");
        if (windowJson == null) throw new IllegalArgumentException("no window in the frontend");
        double[] window = new double[windowJson.length()];
        for (int i = 0; i < window.length; i++) window[i] = windowJson.optDouble(i);

        JSONArray filtersJson = json.optJSONArray("filters");
        if (filtersJson == null) throw new IllegalArgumentException("no filters in the frontend");
        double[][] filters = new double[filtersJson.length()][];
        for (int m = 0; m < filters.length; m++) {
            JSONArray row = filtersJson.optJSONArray(m);
            if (row == null) throw new IllegalArgumentException("filter row " + m + " is missing");
            filters[m] = new double[row.length()];
            for (int k = 0; k < row.length(); k++) {
                double value = row.optDouble(k);
                if (!(value >= 0)) {
                    throw new IllegalArgumentException("a mel filter is negative or absent");
                }
                filters[m][k] = value;
            }
        }
        return new Frontend(
                filters.length,
                json.optInt("fft", 0),
                window.length,
                json.optInt("hop", 0),
                json.optBoolean("center", false),
                window, filters);
    }

    /**
     * Whether this frontend is the one the spec describes.
     *
     * A window or filter set that does not match its own description is worse
     * than a missing model: it runs, and answers.
     */
    public void check(FeatureSpec spec) {
        spec.validate();
        if (mels != spec.mels) {
            throw new IllegalArgumentException("the frontend has " + mels
                    + " mel bands but the spec says " + spec.mels);
        }
        if (fft != spec.fft) {
            throw new IllegalArgumentException("the frontend has an fft of " + fft
                    + " but the spec says " + spec.fft);
        }
        if (frame != spec.frame) {
            throw new IllegalArgumentException("the frontend window is " + frame
                    + " samples but the spec says " + spec.frame);
        }
        if (hop != spec.hop) {
            throw new IllegalArgumentException("the frontend advances " + hop
                    + " samples but the spec says " + spec.hop);
        }
        if (window.length != frame) throw new IllegalArgumentException("the window is the wrong length");
        if (filters.length != mels) throw new IllegalArgumentException("the filter count is wrong");
        for (double[] row : filters) {
            if (row.length != fft / 2 + 1) {
                throw new IllegalArgumentException("a mel filter covers "
                        + row.length + " bins, not " + (fft / 2 + 1));
            }
        }
        if (center && fft / 2 > hop) {
            // A stream cannot show a frame more than half its window into the
            // future. If a trainer wants that, the audio has to be held back by
            // the difference, and until something needs it, refusing is better
            // than quietly taking a frame built from mirrored audio.
            throw new IllegalArgumentException(
                    "a centred frontend needs its hop to be at least half its window");
        }
    }

    /** How much audio at the model's rate a full window of frames needs. */
    public int ringSamples(int frames) {
        return center ? frames * hop : frame + frames * hop;
    }

    /**
     * Where the newest window that is entirely real audio starts, or -1.
     *
     * Centred frames sit on samples, so the last of them reaches past the end of
     * what has been captured; the newest complete one is a step earlier. Both
     * cases end up as "the newest window that fits", which is what the whole
     * clip path computes for its last frame as well.
     */
    public int newestWindowStart(int available) {
        if (center) {
            if (available < fft / 2 + frame) return -1;
            int index = (available - fft / 2) / hop;
            return index * hop - fft / 2;
        }
        if (available < frame) return -1;
        return ((available - frame) / hop) * hop;
    }

    /** How many frames a clip of this many samples produces. */
    public int frameCount(int samples) {
        if (center) return 1 + samples / hop;
        return samples < frame ? 0 : (samples - frame) / hop + 1;
    }

    /** Where frame {@code index} starts reading, in samples of the original. */
    public int frameStart(int index) {
        return center ? index * hop - fft / 2 : index * hop;
    }

    /** The first window that fits inside a buffer without reading past its start. */
    public int firstCompleteWindowStart() {
        if (!center) return 0;
        int index = (fft / 2 + hop - 1) / hop;   // ceiling
        return index * hop - fft / 2;
    }

    private static double hzToMel(double hz) {
        return 2595 * Math.log10(1 + hz / 700.0);
    }

    private static double melToHz(double mel) {
        return 700 * (Math.pow(10, mel / 2595.0) - 1);
    }
}
