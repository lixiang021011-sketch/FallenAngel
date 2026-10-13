package com.fallenangel.listeningai.listening;

import com.fallenangel.listeningai.OnsetCurves;

/**
 * What chord is this, and where does it sit in the key.
 *
 * Asked of a language model first, and the answer was no: given four clean
 * triads - sines, no drums, no vocals - it named three of them "F major" and got
 * one right. A chord is not a thing to ask about, it is a thing to measure. So
 * this measures it, with the machinery the app already has (one FFT per frame,
 * from {@link OnsetCurves}) and nothing else: chroma, a key profile, and twelve
 * major and twelve minor triads to match against.
 *
 * The degree is the part that matters for this project. A chord name is a fact
 * about the audio; a degree is a fact about the music and about hearing - the
 * same F major is IV in C, I in F, and bVII in G - and "what is this chord doing
 * here" is a question about attention that a listener can answer and a chord
 * name cannot express.
 *
 * Deliberately not clever. No seventh chords, no inversions, no modulation
 * tracking: a first version that is wrong in ways its reader can predict is worth
 * more than one that is wrong in ways nobody can. What it does say, it says with
 * a confidence, and low confidence is stored as low confidence.
 */
public final class Harmony {
    /** The pitch classes, in the order the chroma vector is filled. */
    public static final String[] PITCH =
            {"C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"};

    /**
     * Scale degrees, with the case and the accidental a textbook uses.
     *
     * Full-width Roman numerals (Ⅰ Ⅳ Ⅴ) rather than the letters I, V, X: that is
     * how a Chinese theory text prints them, and a degree written "IV" next to a
     * chord written "F 大三和弦" is two conventions in one line. Lower case is the
     * minor triad (ⅳ), and a degree taken from outside the key carries its
     * accidental (bⅥ, #Ⅳ).
     */
    private static final String[] DEGREE = {
            "\u2160", "b\u2161", "\u2161", "b\u2162", "\u2162", "\u2163",
            "#\u2163", "\u2164", "b\u2165", "\u2165", "b\u2166", "\u2166"};
    private static final String[] DEGREE_LOWER = {
            "\u2170", "b\u2171", "\u2171", "b\u2172", "\u2172", "\u2173",
            "#\u2173", "\u2174", "b\u2175", "\u2175", "b\u2176", "\u2176"};

    /**
     * Krumhansl and Kessler's listener-derived key profiles.
     *
     * These are the ones everybody uses, and they carry the assumption everybody
     * who uses them carries: that a key is a weighting over pitches that a
     * Western listener internalised. On music that is not tonal in that way the
     * answer will be confident and wrong, which is why the confidence is
     * reported rather than swallowed.
     */
    private static final double[] MAJOR_PROFILE =
            {6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88};

    /**
     * The analysis frame, and why it is not the onset detector's 2048.
     *
     * At 44.1 kHz a 2048-sample frame resolves 21.5 Hz - most of a semitone - and
     * the root of a chord lives at 65 to 260 Hz, which is exactly where that
     * resolution is not enough. Measured: a B at 246.94 Hz was read as A#, and the
     * leading-note triad of C major came out as the enharmonic A# major. 8192
     * samples is 186 ms and 5.4 Hz per bin: still short enough for a chord display,
     * long enough to tell a bass note from its neighbour.
     */
    private static final int FFT = 8192;
    /** 46 ms between frames: fast enough that a chord change is visible as it happens. */
    private static final int HOP = 2048;
    /** Below this is rumble and above it is cymbals; neither is harmony. */
    private static final double LOW_HZ = 65, HIGH_HZ = 2200;
    /**
     * The bass register, and how much more it counts towards a chord.
     *
     * Measured, not assumed. With every frame normalised but every octave weighted
     * equally, four real tracks were read and two of them came back saying the
     * same chord for the whole window - one of them the same chord for thirty-six
     * seconds of music that changes every bar. A chord is carried by its root, the
     * root is in the bass, and equal weighting lets the vocal and the snare decide;
     * weighting the bottom octave twice is what makes the reading follow the music
     * instead of the mix.
     */
    private static final double BASS_HZ = 260, BASS_WEIGHT = 2.0, HIGH_WEIGHT = 0.5;
    private static final double MID_HZ = 1000;

    /** The key a passage is in. */
    public static final class Key {
        /**
         * The tonic, and nothing else.
         *
         * There used to be a mode here - C major against A minor - and it was
         * doing harm in two ways. Relative keys share all seven notes, so from a
         * chroma the choice between them is a coin toss on weak evidence; and
         * because the two have different tonics, that coin toss moved every degree
         * in the line. A song that never modulates was being described as changing
         * key, which is what the listener noticed and objected to. Until there is
         * something better than a coin toss to decide the mode, the honest answer
         * is the tonic on its own: "C 调" is what a musician says, and it is
         * already enough to say where a chord sits.
         */
        public final String tonic;
        /** The tonic as a pitch class, for degrees and for naming. */
        public final int tonicIndex;

        Key(int tonicIndex) {
            this.tonicIndex = tonicIndex;
            this.tonic = PITCH[tonicIndex];
        }

        public String name() {
            // The accidental goes in front, as it is said and written: #C 调, not
            // "C# 调". Nobody says the second one out loud.
            String accidental = tonic.length() > 1 ? tonic.substring(1) : "";
            String letter = tonic.substring(0, 1);
            return accidental + letter + " 调";
        }

        @Override
        public String toString() {
            return name();
        }
    }

    /** The three qualities this reads. Augmented triads are not among them. */
    public enum Quality {
        MAJOR("大三和弦"), MINOR("小三和弦"), DIMINISHED("减三和弦");

        public final String chinese;

        Quality(String chinese) {
            this.chinese = chinese;
        }
    }

    /** A chord, and how much of the window it accounts for. */
    public static final class Reading {
        /**
         * The chord as a theory text writes it: an upper-case letter for a major
         * triad, a lower-case one for a minor triad (C, d, b), sharp before the
         * letter (#c). Not the pop symbol "Cm" - one notation for the degree and
         * another for the chord on the same line is how a label stops being
         * readable by the people who use the notation professionally.
         */
        public final String chord;
        public final int root;
        public final Quality quality;
        /** Share of the chroma the three tones explain, 0..1. */
        public final double confidence;
        /** The next-best triad's share: what this reading is winning by. */
        public final double runnerUp;

        Reading(String chord, int root, Quality quality, double confidence, double runnerUp) {
            this.chord = chord;
            this.root = root;
            this.quality = quality;
            this.confidence = confidence;
            this.runnerUp = runnerUp;
        }

        /**
         * How far ahead of the next chord this one is.
         *
         * The number that says whether the reading is worth showing. On clean
         * triads it is a third; on real music it is often a hundredth, and a
         * hundredth is two chords fitting equally well - which is when a display
         * should keep what it has rather than flicker between them.
         */
        public double margin() {
            return confidence - runnerUp;
        }

        /** "F 大三和弦". */
        public String name() {
            return chord + " " + quality.chinese;
        }

        @Override
        public String toString() {
            return chord;
        }
    }

    /** A pitch class as a note name, in the case the quality calls for. */
    public static String symbol(int pitch, Quality quality) {
        String raw = PITCH[((pitch % 12) + 12) % 12];
        String letter = raw.substring(0, 1);
        String accidental = raw.length() > 1 ? raw.substring(1) : "";
        if (quality == Quality.MAJOR) return letter + accidental;
        return accidental + letter.toLowerCase(java.util.Locale.US);
    }

    /**
     * The pitch classes of a window, as a distribution rather than a magnitude sum.
     *
     * Each frame is compressed and normalised before it is added, and that is not
     * a detail: summing raw magnitudes lets one loud bass note and one cymbal
     * decide the whole answer, and the first version of this did exactly that -
     * forty-four seconds of real music came back as the same chord throughout.
     * Compressing (log, so a quiet partial still counts) and normalising per frame
     * (so a loud frame does not outvote a quiet one) is what makes the result a
     * statement about pitch classes rather than about level.
     */
    public static double[] chroma(short[] pcm, int offset, int count, int rate) {
        double[] chroma = new double[12];
        if (pcm == null || count < FFT || rate <= 0) return chroma;
        double[] window = OnsetCurves.hannWindow(FFT);
        double[] re = new double[FFT];
        double[] im = new double[FFT];
        double[] magnitude = new double[FFT / 2];
        double[] frame = new double[12];
        int frames = 0;
        int firstBin = Math.max(1, (int) Math.ceil(LOW_HZ * FFT / rate));
        int lastBin = Math.min(FFT / 2 - 2, (int) Math.floor(HIGH_HZ * FFT / rate));
        int last = Math.min(pcm.length, offset + count) - FFT;
        for (int start = Math.max(0, offset); start <= last; start += HOP) {
            for (int i = 0; i < FFT; i++) {
                re[i] = pcm[start + i] * window[i];
                im[i] = 0;
            }
            OnsetCurves.fft(re, im);
            for (int i = 0; i < 12; i++) frame[i] = 0;
            for (int bin = firstBin; bin <= lastBin; bin++) {
                magnitude[bin] = Math.hypot(re[bin], im[bin]);
            }
            // Only the peaks. A sine is one peak and forty smeared neighbours, and
            // summing every bin counts the smear: with log compression on top of
            // it, the neighbours together outweigh the note they came from, which
            // is how a chord reader ends up hearing anything but the chord.
            for (int bin = firstBin + 1; bin < lastBin; bin++) {
                if (magnitude[bin] < magnitude[bin - 1] || magnitude[bin] <= magnitude[bin + 1]) {
                    continue;
                }
                // Where the note actually is, not where the nearest bin is. At
                // 44.1 kHz a 2048-point frame has 21.5 Hz bins - most of a semitone
                // - so rounding the bin's own frequency moves a note to its
                // neighbour often enough to matter: measured on a B sine at
                // 246.94 Hz, the reading landed on A# and the chord came out as the
                // enharmonic major triad instead of the diminished one.
                double left = Math.log(magnitude[bin - 1] + 1e-9);
                double peak = Math.log(magnitude[bin] + 1e-9);
                double right = Math.log(magnitude[bin + 1] + 1e-9);
                double bend = 0.5 * (left - right) / (left - 2 * peak + right);
                if (Double.isNaN(bend) || Math.abs(bend) > 1) bend = 0;
                double trueBin = bin + bend;
                double hz = trueBin * (double) rate / FFT;
                double midi = 69 + 12 * Math.log(hz / 440.0) / Math.log(2);
                int nearest = (int) Math.round(midi);
                // Soft assignment: a note a quarter tone away from a pitch class
                // still counts there, but for three quarters of itself. Hard
                // rounding throws that information - and the information is exactly
                // the difference between B and A#.
                double closeness = 1 - Math.abs(midi - nearest);
                int pitch = (nearest % 12 + 12) % 12;
                double weight = 1.0;
                if (hz <= BASS_HZ) {
                    weight = BASS_WEIGHT;
                } else if (hz >= MID_HZ) {
                    weight = HIGH_WEIGHT;
                }
                frame[pitch] += Math.sqrt(magnitude[bin]) * weight * closeness;
            }
            double norm = 0;
            for (double value : frame) norm += value * value;
            norm = Math.sqrt(norm);
            if (norm <= 0) continue;
            for (int i = 0; i < 12; i++) chroma[i] += frame[i] / norm;
            frames++;
        }
        // Averaged first, then scaled to the loudest pitch class, so that what a
        // caller prints is a shape over the twelve notes rather than a sum that
        // silently depends on how many frames the window happened to contain.
        if (frames > 0) for (int i = 0; i < 12; i++) chroma[i] /= frames;
        double loudest = 0;
        for (double value : chroma) loudest = Math.max(loudest, value);
        if (loudest > 0) for (int i = 0; i < 12; i++) chroma[i] /= loudest;
        return chroma;
    }

    /**
     * The key a passage is in, by correlating its chroma with the profiles.
     *
     * Twelve rotations and two modes, and the best correlation wins. On one chord
     * this is close to meaningless - a C major triad is as much "C major" as it
     * is "F major with the root missing" - so callers who know a whole song should
     * hand in the whole song's chroma, which is why this takes a chroma rather
     * than audio.
     */
    public static Key keyOf(double[] chroma) {
        int best = 0;
        double bestScore = -2;
        for (int tonic = 0; tonic < 12; tonic++) {
            double score = correlate(chroma, MAJOR_PROFILE, tonic);
            if (score > bestScore) {
                bestScore = score; best = tonic;
            }
        }
        return new Key(best);
    }

    /** How well a key explains a chroma: the profile correlation, -1 to 1. */
    public static double fit(double[] chroma, Key key) {
        if (key == null) return -1;
        return correlate(chroma, MAJOR_PROFILE, key.tonicIndex);
    }

    /**
     * The key of a passage, held on to unless the evidence clearly moves.
     *
     * C major, G major and A minor share six of their seven notes, so on a rough
     * chroma the best-matching profile flips between them from one window to the
     * next - and a song with no modulation in it was being described as if it
     * changed key four times. Estimating a key is a decision that should be
     * sticky: the previous answer stands unless another one explains the music by
     * this much more, which is what a real modulation looks like and what noise
     * does not.
     */
    public static Key keyOf(double[] chroma, Key previous, double margin) {
        Key best = keyOf(chroma);
        if (previous == null) return best;
        if (best.tonicIndex == previous.tonicIndex) return best;
        if (fit(chroma, best) - fit(chroma, previous) < margin) return previous;
        return best;
    }

    /**
     * The best-fitting triad, with no key to help.
     *
     * Chroma alone cannot tell some chords apart, and this is not a bug in the
     * implementation: B-D-F is the same three pitch classes as A# major (A#-C𝄪-E#
     * is written differently and heard the same), so with no key both fit equally
     * and the plainer name wins. Callers who know the key must pass it.
     */
    public static Reading chordOf(double[] chroma) {
        return chordOf(chroma, null);
    }

    /**
     * The same, with the key as a prior.
     *
     * A small bonus for the triad that belongs to the key, which is what a
     * listener does without noticing: B-D-F in C major is the leading-note triad,
     * and the fact that it is also A# major spelled oddly is not something anybody
     * hears. Small enough that a chord genuinely outside the key still wins when
     * the evidence is clear - which is how a modulation shows up as one.
     */
    public static Reading chordOf(double[] chroma, Key key) {
        int bestRoot = 0;
        Quality bestQuality = Quality.MAJOR;
        double bestScore = -1;
        double secondScore = -1;
        for (int root = 0; root < 12; root++) {
            for (int i = 0; i < Quality.values().length; i++) {
                Quality quality = Quality.values()[i];
                double score = triadScore(chroma, root, quality)
                        + (belongs(root, quality, key) ? IN_KEY_BONUS : 0);
                // Strictly greater, so ties fall to the plainer chord: a major
                // triad is the common case and an unexplained diminished one is
                // the more surprising claim.
                if (score > bestScore) {
                    secondScore = bestScore;
                    bestScore = score; bestRoot = root; bestQuality = quality;
                } else if (score > secondScore) {
                    secondScore = score;
                }
            }
        }
        return new Reading(symbol(bestRoot, bestQuality), bestRoot, bestQuality,
                Math.max(0, Math.min(1, bestScore)), Math.max(0, Math.min(1, secondScore)));
    }

    /** Enough to separate two spellings of the same three notes, and no more. */
    private static final double IN_KEY_BONUS = 0.05;

    /** Whether that triad is one of the seven a key actually contains. */
    static boolean belongs(int root, Quality quality, Key key) {
        if (key == null) return false;
        int step = ((root - key.tonicIndex) % 12 + 12) % 12;
        Quality expected;
        // The seven triads of the major frame. A song written in the relative
        // minor is read in that same frame, which is how its chords are usually
        // written down anyway - Ⅵ-Ⅳ-Ⅰ-Ⅴ, not ⅰ-Ⅵ-Ⅲ-Ⅶ.
        if (step == 0 || step == 5 || step == 7) expected = Quality.MAJOR;
        else if (step == 2 || step == 4 || step == 9) expected = Quality.MINOR;
        else if (step == 11) expected = Quality.DIMINISHED;
        else return false;
        return quality == expected;
    }

    /** Where that chord sits in that key: "IV", "vi", "bVII". */
    public static String degreeOf(Reading chord, Key key) {
        if (chord == null || key == null) return "";
        int tonic = 0;
        for (int i = 0; i < 12; i++) if (PITCH[i].equals(key.tonic)) tonic = i;
        int step = ((chord.root - tonic) % 12 + 12) % 12;
        // Case is the quality, as it is written on paper, and a diminished triad
        // adds the degree sign: Ⅳ, ⅳ, ⅶ°. The chord name repeats it in letters,
        // which is how a text prints it and how a reader checks it.
        String numeral = chord.quality == Quality.MAJOR ? DEGREE[step] : DEGREE_LOWER[step];
        return chord.quality == Quality.DIMINISHED ? numeral + "\u00B0" : numeral;
    }

    /** The whole thing, for a window whose key is taken from the same window. */
    public static Reading of(short[] pcm, int offset, int count, int rate) {
        double[] chroma = chroma(pcm, offset, count, rate);
        return chordOf(chroma);
    }

    /** The same, with a key known from elsewhere - a whole song, usually. */
    public static Reading of(short[] pcm, int offset, int count, int rate, Key key) {
        return chordOf(chroma(pcm, offset, count, rate), key);
    }

    /**
     * "C 大调 Ⅳ 级（F 大三和弦）", or just the chord when there is no key.
     *
     * The quality is spelled out inside the bracket rather than left to the case
     * of one letter: "（F）" is what a pop chart prints and it is ambiguous about
     * the third, while the degree numeral already says only that the triad is
     * major. Both halves say the same thing in two notations, which is how a
     * reader checks one against the other.
     */
    public static String sentence(Reading chord, Key key) {
        if (chord == null) return "";
        if (key == null) return chord.name();
        return key.name() + " " + degreeOf(chord, key) + " 级（" + chord.name() + "）";
    }

    private static double triadScore(double[] chroma, int root, Quality quality) {
        int third = quality == Quality.MAJOR ? 4 : 3;
        int fifth = quality == Quality.DIMINISHED ? 6 : 7;
        int[] tones = {root, (root + third) % 12, (root + fifth) % 12};
        double inside = 0;
        for (int tone : tones) inside += chroma[tone];
        double total = 0;
        for (double value : chroma) total += value;
        if (total <= 0) return 0;
        // The share of what was heard that the triad accounts for: a chord under
        // a busy mix scores lower than the same chord alone, which is right - the
        // reading is less certain, not less true.
        return inside / total;
    }

    /** Pearson correlation of the chroma with a key profile, rotated to a tonic. */
    private static double correlate(double[] chroma, double[] profile, int tonic) {
        int n = 12;
        double meanChroma = 0, meanProfile = 0;
        for (int i = 0; i < n; i++) {
            meanChroma += chroma[i];
            meanProfile += profile[i];
        }
        meanChroma /= n;
        meanProfile /= n;
        double cov = 0, varChroma = 0, varProfile = 0;
        for (int i = 0; i < n; i++) {
            double a = chroma[(i + tonic) % 12] - meanChroma;
            double b = profile[i] - meanProfile;
            cov += a * b;
            varChroma += a * a;
            varProfile += b * b;
        }
        double denominator = Math.sqrt(varChroma * varProfile);
        return denominator <= 0 ? -1 : cov / denominator;
    }

    private Harmony() {
    }
}
