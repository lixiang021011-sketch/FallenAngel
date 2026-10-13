package com.fallenangel.listeningai.clip;

/**
 * The on-demand frontend: a few seconds of audio as log-mel, for a model.
 *
 * Deliberately separate from the running analysis. The always-on features are
 * five numbers per five milliseconds - enough to notice that something happened
 * and to ask about it, and nothing like what an audio model eats. So when a
 * question is worth asking, the raw audio is run through this instead.
 *
 * Every number in here comes from a {@link FeatureSpec}: the sample rate, the
 * mel count, the window, the hop, the band range, the floor and whether the clip
 * is normalised. None of them is a constant in this class, because they belong
 * to whichever model is being fed, and a model given the wrong ones answers
 * confidently and wrongly rather than failing.
 */
public final class LogMel {
    private final FeatureSpec spec;
    private final Frontend frontend;

    /** The frontend this project would derive, which is what the placeholder uses. */
    public LogMel(FeatureSpec spec) {
        this(spec, derivedFor(spec));
    }

    /**
     * Deriving is only right when the spec does not name a frontend.
     *
     * A model whose trainer's filters are sitting in a file next to it, fed our
     * own approximation instead, is the exact failure this whole layer is meant
     * to prevent: it does not error, it answers.
     */
    private static Frontend derivedFor(FeatureSpec spec) {
        if (spec.frontend != null) {
            throw new IllegalArgumentException("this frontend has to be loaded from "
                    + spec.frontend + " rather than derived");
        }
        return Frontend.derived(spec);
    }

    /** A frontend that came with the model, checked against its description. */
    public LogMel(FeatureSpec spec, Frontend frontend) {
        spec.validate();
        this.spec = spec;
        this.frontend = java.util.Objects.requireNonNull(frontend);
        frontend.check(spec);
    }

    public FeatureSpec spec() {
        return spec;
    }

    public Frontend frontend() {
        return frontend;
    }

    /** How much audio at the model's rate a window of this many frames needs. */
    public int ringSamples(int frames) {
        return frontend.ringSamples(frames);
    }

    /**
     * @param pcm        mono signed PCM at {@code sourceRate}
     * @param offset     first sample of the clip
     * @param count      how many samples the clip is
     * @return one row per frame, one column per mel band, oldest first
     */
    public float[][] of(short[] pcm, int offset, int count, int sourceRate) {
        float[] mono = Resampler.to(pcm, offset, count, sourceRate, spec.sampleRate);
        return of(mono);
    }

    /** The same, for audio that is already at the model's rate. */
    public float[][] of(float[] mono) {
        int frames = frontend.frameCount(mono.length);
        float[][] out = new float[Math.max(0, frames)][spec.mels];
        if (frames <= 0) return out;
        double gain = gain(mono);
        for (int frame = 0; frame < frames; frame++) {
            out[frame] = frame(mono, frontend.frameStart(frame), gain);
        }
        if (spec.normalise) normalise(out);
        return out;
    }

    /**
     * The newest frame whose window is entirely inside the audio.
     *
     * This is what a rolling frontend computes: one frame per hop, from the
     * newest window it has. Returning it on its own rather than recomputing the
     * whole clip keeps the cost of a hop to one FFT - which matters, because a
     * hop is sixteen milliseconds.
     *
     * @return one row of mel bands, or null if there is not yet a whole window
     */
    public float[] newestFrame(float[] mono) {
        int start = frontend.newestWindowStart(mono.length);
        if (start < 0) return null;
        return frame(mono, start, gain(mono));
    }

    /**
     * Several of the newest frames at once, oldest first, for a buffer that has
     * just been filled for the first time.
     *
     * @return at most {@code wanted} rows, or none if the buffer is too short
     */
    public float[][] newestRows(float[] mono, int wanted) {
        int newest = frontend.newestWindowStart(mono.length);
        if (newest < 0 || wanted <= 0) return new float[0][];
        int oldest = Math.max(frontend.firstCompleteWindowStart(),
                newest - (wanted - 1) * spec.hop);
        double gain = gain(mono);
        java.util.List<float[]> rows = new java.util.ArrayList<>();
        for (int at = oldest; at <= newest; at += spec.hop) {
            rows.add(frame(mono, at, gain));
        }
        return rows.toArray(new float[0][]);
    }

    /**
     * One window as mel bands, in the model's own scale.
     *
     * The loudness normalisation is a factor on the audio, which reaches the
     * mel as its square because the spectrum is a power spectrum.
     */
    private float[] frame(float[] mono, int start, double gain) {
        double[] re = new double[spec.fft];
        double[] im = new double[spec.fft];
        double[] powers = new double[spec.fft / 2 + 1];
        for (int i = 0; i < spec.fft; i++) {
            re[i] = i < spec.frame ? sample(mono, start + i) * frontend.window[i] * gain : 0;
            im[i] = 0;
        }
        fastFourier(re, im);
        double peak = 0;
        for (int k = 0; k < powers.length; k++) {
            powers[k] = re[k] * re[k] + im[k] * im[k];
            peak = Math.max(peak, powers[k]);
        }
        float[] out = new float[spec.mels];
        for (int m = 0; m < spec.mels; m++) {
            double sum = 0;
            double[] filter = frontend.filters[m];
            for (int k = 0; k < powers.length; k++) sum += filter[k] * powers[k];
            out[m] = (float) log(sum, peak);
        }
        return out;
    }

    /**
     * Silence inside the log.
     *
     * A relative floor keeps the numbers finite without deciding anything about
     * the music: the loudest band in the frame is the reference, and a band
     * floorRatio below it is treated as being at the floor. The absolute curve
     * a real model used instead has no floor to place, because it has no zero.
     */
    private double log(double power, double peak) {
        if (spec.log == FeatureSpec.Log.LOG10_OFFSET) {
            return Math.log10(spec.logScale * power + spec.logOffset);
        }
        double floor = Math.max(peak * spec.floorRatio, Double.MIN_NORMAL);
        return Math.log(Math.max(power, floor));
    }

    /** The gain that takes this audio to the level the frontend expects. */
    private double gain(float[] mono) {
        if (!spec.normaliseLevel) return 1;
        double sum = 0;
        for (float value : mono) sum += value * value;
        double rms = mono.length == 0 ? 0 : Math.sqrt(sum / mono.length);
        // Below this there is nothing but the noise floor of the capture, and
        // amplifying it would turn silence into music.
        if (rms < 1e-4) return 1;
        return LEVEL / rms;
    }

    /** What a clip is scaled to when the frontend normalises loudness. */
    private static final double LEVEL = 0.1;

    /** Read a sample, counting the empty lead-in of a padded frame as silence. */
    private double sample(float[] mono, int at) {
        // Frames that reach past either end are what centring means, and the
        // trainer's padding was zeros, not a mirror of the audio: measured
        // against librosa, mirroring the first frame changed it by a factor of
        // three and zeroes matched it to 3e-7.
        return at < 0 || at >= mono.length ? 0 : mono[at];
    }

    /**
     * Put a clip on the same footing as other clips.
     *
     * A model should recognise a drum fill whether it arrived loud or quiet, so
     * the clip's own mean and spread are removed. The clip here is whatever the
     * caller is treating as one - the model's whole window when this is used
     * from a stream.
     */
    public static void normalise(float[][] frames) {
        if (frames.length == 0 || frames[0].length == 0) return;
        float[] flat = new float[frames.length * frames[0].length];
        for (int f = 0; f < frames.length; f++) {
            System.arraycopy(frames[f], 0, flat, f * frames[0].length, frames[0].length);
        }
        normalise(flat);
        for (int f = 0; f < frames.length; f++) {
            System.arraycopy(flat, f * frames[0].length, frames[f], 0, frames[0].length);
        }
    }

    /** The same, over one flat clip of numbers. */
    public static void normalise(float[] values) {
        double mean = 0;
        for (float value : values) mean += value;
        if (values.length == 0) return;
        mean /= values.length;
        double variance = 0;
        for (float value : values) {
            double d = value - mean;
            variance += d * d;
        }
        double spread = Math.sqrt(variance / values.length);
        if (spread < 1e-6) spread = 1;
        for (int i = 0; i < values.length; i++) {
            values[i] = (float) ((values[i] - mean) / spread);
        }
    }

    private static void fastFourier(double[] re, double[] im) {
        int n = re.length;
        for (int i = 1, j = 0; i < n; i++) {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) {
                double tr = re[i];
                re[i] = re[j];
                re[j] = tr;
                double ti = im[i];
                im[i] = im[j];
                im[j] = ti;
            }
        }
        for (int len = 2; len <= n; len <<= 1) {
            double angle = -2 * Math.PI / len;
            double wRe = Math.cos(angle);
            double wIm = Math.sin(angle);
            for (int i = 0; i < n; i += len) {
                double curRe = 1, curIm = 0;
                for (int k = 0; k < len / 2; k++) {
                    int a = i + k;
                    int b = i + k + len / 2;
                    double vRe = re[b] * curRe - im[b] * curIm;
                    double vIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - vRe;
                    im[b] = im[a] - vIm;
                    re[a] += vRe;
                    im[a] += vIm;
                    double nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }
}
