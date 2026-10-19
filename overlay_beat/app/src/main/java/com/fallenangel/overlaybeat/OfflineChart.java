package com.fallenangel.overlaybeat;

import java.util.ArrayList;
import java.util.List;

/**
 * Makes a chart from the whole song at once.
 *
 * The live generator has to commit to a note before it can hear it, which is why
 * it predicts - and every compromise in it comes from that: the lead time, the
 * figure it guesses, the density it cannot afford to get wrong because it cannot
 * take it back. Given the whole file none of that is necessary. The tempo and
 * the phase can be fitted to the entire song instead of tracked through it, each
 * bar can be read as the bar it actually is rather than the bar that was
 * expected, and a note can be placed where the music plays it rather than where
 * the music was going to.
 *
 * The features are the same ones the live path uses, deliberately: a chart made
 * from a file and a chart made while listening should differ because the offline
 * one knows more, not because they measured different things.
 */
public final class OfflineChart {
    public static final int SUBDIVISIONS = 4;
    public static final int BEATS_PER_BAR = 4;
    public static final int POSITIONS = SUBDIVISIONS * BEATS_PER_BAR;

    /** How much of the average energy a position needs to count as an onset. */
    private static final double ONSET_SHARE = 0.5;
    /** How much of a beat's own energy a position needs to count as part of it. */
    private static final double FIGURE_SHARE = 0.4;
    /** Seconds of audio the phase is fitted over at a time. */
    private static final double PHASE_WINDOW_SECONDS = 4;
    private static final double BPM_LO = 70;
    private static final double BPM_HI = 170;
    /** How far the fitted tempo may drift from the autocorrelation's answer. */
    private static final double MAX_DRIFT = 0.02;
    /** Measurement switch: print what each candidate tempo level scored. */
    public static boolean verbose;

    /** Which positions of a beat a difficulty is willing to ask for. */
    public enum Difficulty {
        /** Beats and the halfway point: the shape of the bar, nothing in between. */
        EASY(0b0101),
        /** Everything the music plays. */
        NORMAL(0b1111),
        /** For later, when there is something extra to ask for. */
        HARD(0b1111);

        final int mask;

        Difficulty(int mask) {
            this.mask = mask;
        }
    }

    public final List<ChartNote> notes;
    /**
     * The song's drum bands over time, one value per analysis frame, 0..1.
     *
     * This is what the lane draws when a song is played from its cached audio:
     * the shape of the drums themselves, falling, instead of bricks standing in
     * for them. It is the two bands of the mix rather than a separated drum
     * track - there is no stem to be had from someone else's player - but the
     * hits are the same hits the chart is made of.
     */
    public final float[] envelope;
    /** How many envelope values there are per second. */
    public static final double ENVELOPE_RATE = OnsetCurves.PER_SECOND;
    public final double bpm;
    /** Frames between beats, after the whole-song fit. */
    public final double periodFrames;
    /** Seconds where the first beat of the grid lands. */
    public final double firstBeatSeconds;
    public final int bars;
    /** One 16-bit figure vector per bar: the four beats' subdivisions. */
    public final int[] barFigures;
    /** How often a bar is the same as the bar loop bars before it. */
    public final double repeatRate;
    public final int loopBars;

    private OfflineChart(List<ChartNote> notes, float[] envelope,
                         double bpm, double periodFrames,
                         double firstBeatSeconds, int bars, int[] barFigures,
                         double repeatRate, int loopBars) {
        this.notes = notes;
        this.envelope = envelope;
        this.bpm = bpm;
        this.periodFrames = periodFrames;
        this.firstBeatSeconds = firstBeatSeconds;
        this.bars = bars;
        this.barFigures = barFigures;
        this.repeatRate = repeatRate;
        this.loopBars = loopBars;
    }

    /** One line for the log: what the whole-song pass decided. */
    public String summary() {
        return String.format(java.util.Locale.US,
                "%d notes, %d bars, bpm %.1f, first beat %.2fs, loop %d bars, "
                        + "%.0f%% of bars repeat it",
                notes.size(), bars, bpm, firstBeatSeconds, loopBars, repeatRate * 100);
    }

    public static OfflineChart analyse(short[] pcm, Difficulty difficulty) {
        Curves curves = Curves.of(pcm);

        // The tempo, fitted to the whole song: a period that explains every part
        // of it is the one worth having, which is also what stops it picking a
        // half or double reading - those line up with some of the song, never
        // with all of it.
        double period = bestPeriod(curves.strength);

        // The phase, per window and then fitted: a period alone does not stay on
        // the music for four minutes, and the residual drift is what the live
        // path needs a phase-locked loop for.
        Fit fit = fitGrid(curves.strength, period);
        period = fit.periodFrames;

        // Positions and the figure each bar actually plays. The fitted phase is
        // an absolute frame - the grid's line, not its first point on this song -
        // so the first point is found by walking back to the start of the file.
        double step = period / SUBDIVISIONS;
        double firstGrid = fit.phaseFrame
                + Math.ceil(-fit.phaseFrame / step) * step;
        int firstFrame = (int) Math.round(firstGrid);
        int lastFrame = curves.strength.length - 1;
        int positions = (int) Math.floor((lastFrame - firstGrid) / step) + 1;
        int bars = positions / POSITIONS;
        int[] barFigures = new int[bars];
        double[][] energies = new double[bars][POSITIONS];
        double[] kick = new double[bars * POSITIONS];
        double[] snare = new double[bars * POSITIONS];

        int half = Math.max(1, (int) Math.round(step / 6));
        double total = 0;
        int counted = 0;
        for (int bar = 0; bar < bars; bar++) {
            for (int position = 0; position < POSITIONS; position++) {
                int centre = (int) Math.round(firstFrame + (bar * POSITIONS + position) * step);
                double combined = 0, kickPeak = 0, snarePeak = 0;
                for (int i = Math.max(0, centre - half);
                     i < Math.min(curves.strength.length, centre + half + 1); i++) {
                    combined = Math.max(combined, curves.strength[i]);
                    kickPeak = Math.max(kickPeak, curves.kick[i]);
                    snarePeak = Math.max(snarePeak, curves.snare[i]);
                }
                int index = bar * POSITIONS + position;
                energies[bar][position] = combined;
                kick[index] = kickPeak;
                snare[index] = snarePeak;
                total += combined;
                counted++;
            }
        }
        double average = counted == 0 ? 0 : total / counted;

        for (int bar = 0; bar < bars; bar++) {
            int vector = 0;
            for (int beat = 0; beat < BEATS_PER_BAR; beat++) {
                double beatMax = 0;
                for (int i = 0; i < SUBDIVISIONS; i++) {
                    beatMax = Math.max(beatMax, energies[bar][beat * SUBDIVISIONS + i]);
                }
                for (int i = 0; i < SUBDIVISIONS; i++) {
                    double value = energies[bar][beat * SUBDIVISIONS + i];
                    boolean onset = value > 0 && value >= average * ONSET_SHARE
                            && value >= beatMax * FIGURE_SHARE;
                    if (onset) vector |= 1 << (beat * SUBDIVISIONS + i);
                }
            }
            barFigures[bar] = vector;
        }

        // What the song is doing at the level above the bar: the loop it repeats
        // and the sections those repeats come in. A chart that copies each bar
        // and stops there plays the same eight bars eight times, which is what
        // "correct but with no idea in it" sounds like. A person writing the
        // chart holds some of it back and gives it later - the first pass through
        // a loop is not the fourth.
        double[] barEnergy = new double[bars];
        for (int bar = 0; bar < bars; bar++) {
            double sum = 0;
            for (int position = 0; position < POSITIONS; position++) {
                sum += energies[bar][position];
            }
            barEnergy[bar] = sum;
        }
        double medianEnergy = median(barEnergy);
        int loopBars = bestLoop(energies, barEnergy, bars);
        int[] sectionStart = sections(barEnergy, bars);

        List<ChartNote> notes = new ArrayList<>();
        for (int bar = 0; bar < bars; bar++) {
            double energyFactor = clamp(barEnergy[bar] / Math.max(medianEnergy, 1e-9)
                    * 0.5 + 0.5, 0.7, 1.15);
            double repeatFactor = repeatCurve(bar, sectionStart, loopBars);
            double factor = clamp(energyFactor * repeatFactor, 0.45, 1.15);
            for (int beat = 0; beat < BEATS_PER_BAR; beat++) {
                int figure = (barFigures[bar] >> (beat * SUBDIVISIONS)) & 0b1111;
                int chosen = figure & difficulty.mask;
                int count = Integer.bitCount(chosen);
                if (count == 0) continue;
                // Which of the places the music plays this bar is worth asking
                // for is the chart's own decision: the loudest ones are the ones
                // that carry it, and the rest are what gets held back when the
                // music is not at full strength.
                int budget = Math.max(1, (int) Math.round(count * factor));
                int[] order = byEnergy(chosen, energies[bar], beat);
                for (int k = 0; k < Math.min(budget, order.length); k++) {
                    int i = order[k];
                    int index = bar * POSITIONS + beat * SUBDIVISIONS + i;
                    double time = frameSeconds(firstGrid + index * step);
                    boolean accent = kick[index] + snare[index] >= 2 * average * ONSET_SHARE;
                    notes.add(new ChartNote(time, lane(kick[index], snare[index]),
                            ChartNote.Kind.TAP, accent, 0));
                }
            }
        }

        double bestRepeat = 0;
        if (loopBars > 0) {
            int same = 0;
            for (int bar = loopBars; bar < bars; bar++) {
                if (samePhrase(energies[bar], energies[bar - loopBars],
                        barEnergy[bar], barEnergy[bar - loopBars])) same++;
            }
            bestRepeat = same / (double) (bars - loopBars);
        }
        double bpm = 60.0 * OnsetCurves.PER_SECOND / period;
        return new OfflineChart(notes, normalise(curves.envelope), bpm, period,
                frameSeconds(firstFrame), bars,
                barFigures, bestRepeat, loopBars);
    }

    /** Scale the envelope so the loudest drum in the song is the full width. */
    private static float[] normalise(double[] values) {
        double[] sorted = values.clone();
        java.util.Arrays.sort(sorted);
        // The ninety-fifth percentile rather than the maximum: one clipped
        // sample should not make every other drum hit look small.
        double reference = sorted.length == 0 ? 0 : sorted[(int) (sorted.length * 0.95)];
        float[] out = new float[values.length];
        if (reference <= 0) return out;
        for (int i = 0; i < values.length; i++) {
            out[i] = (float) Math.min(1, values[i] / reference);
        }
        return out;
    }

    private static int lane(double kick, double snare) {
        return kick >= snare ? 0 : 1;
    }

    private static double clamp(double value, double lo, double hi) {
        return Math.max(lo, Math.min(hi, value));
    }

    private static double median(double[] values) {
        if (values.length == 0) return 0;
        double[] sorted = values.clone();
        java.util.Arrays.sort(sorted);
        return sorted[sorted.length / 2];
    }

    /** The subdivision indices of a figure, loudest first. */
    private static int[] byEnergy(int bits, double[] barEnergies, int beat) {
        List<Integer> order = new ArrayList<>();
        for (int i = 0; i < SUBDIVISIONS; i++) {
            if ((bits & (1 << i)) != 0) order.add(i);
        }
        order.sort((a, b) -> Double.compare(
                barEnergies[beat * SUBDIVISIONS + b],
                barEnergies[beat * SUBDIVISIONS + a]));
        int[] out = new int[order.size()];
        for (int i = 0; i < out.length; i++) out[i] = order.get(i);
        return out;
    }

    /**
     * How long the song's loop is, in bars, by how much the music repeats
     * itself at that distance.
     *
     * The longest loop that still repeats is taken, because a four-bar phrase
     * that repeats also repeats every two bars and every bar - the shorter
     * answers are true and say less.
     */
    private static int bestLoop(double[][] energies, double[] barEnergy, int bars) {
        int best = 0;
        double bestRate = 0;
        for (int loop : new int[] {1, 2, 4, 8, 16}) {
            if (bars <= loop * 2) continue;
            int same = 0;
            for (int bar = loop; bar < bars; bar++) {
                if (samePhrase(energies[bar], energies[bar - loop],
                        barEnergy[bar], barEnergy[bar - loop])) same++;
            }
            double rate = same / (double) (bars - loop);
            if (verbose) {
                System.out.printf(java.util.Locale.US,
                        "  loop %d bars: %.0f%% of bars repeat it%n", loop, rate * 100);
            }
            if (rate >= 0.6 && rate >= bestRate * 0.9) {
                best = loop;
                bestRate = Math.max(bestRate, rate);
            }
        }
        return best;
    }

    /**
     * Whether two bars are the same phrase played again.
     *
     * Not "the same sixteen bits": a real drummer never plays a bar twice
     * identically, and an exact comparison finds no repetition in any recording
     * ever made - which is how the first version of this reported that a
     * loop-based track had no loop in it. A few places different out of sixteen,
     * at a similar strength, is the same phrase.
     */
    private static boolean samePhrase(double[] a, double[] b, double energyA, double energyB) {
        double ratio = energyA / Math.max(energyB, 1e-9);
        if (ratio <= 0.6 || ratio >= 1.7) return false;
        // The shape of the bar, not which bits happened to be set: a drummer
        // playing the same phrase twice puts the weight in the same places and
        // never hits exactly the same sixteenths, so comparing the sixteen
        // strengths is comparing the phrase and comparing the bits is comparing
        // the performance.
        double meanA = 0, meanB = 0;
        for (int i = 0; i < POSITIONS; i++) {
            meanA += a[i] / POSITIONS;
            meanB += b[i] / POSITIONS;
        }
        double numerator = 0, spreadA = 0, spreadB = 0;
        for (int i = 0; i < POSITIONS; i++) {
            double x = a[i] - meanA;
            double y = b[i] - meanB;
            numerator += x * y;
            spreadA += x * x;
            spreadB += y * y;
        }
        if (spreadA <= 0 || spreadB <= 0) return false;
        return numerator / Math.sqrt(spreadA * spreadB) >= 0.6;
    }

    /**
     * Where the music changes level: a run of bars whose energy is on one side
     * of the song's own median, with a hysteresis so a single loud bar inside a
     * quiet part does not split it in two.
     *
     * @return the first bar of each section, in order, always starting with 0
     */
    private static int[] sections(double[] barEnergy, int bars) {
        List<Integer> starts = new ArrayList<>();
        starts.add(0);
        if (bars == 0) return new int[] {0};
        double median = median(barEnergy);
        boolean loud = barEnergy[0] >= median;
        int quietRun = 0;
        for (int bar = 1; bar < bars; bar++) {
            boolean isLoud = barEnergy[bar] >= median;
            if (isLoud == loud) {
                quietRun = 0;
                continue;
            }
            // A section has to be four bars long before it counts as one, or
            // every other bar is a section and the intensity curve is noise.
            if (++quietRun >= 4) {
                starts.add(bar - 3);
                loud = isLoud;
                quietRun = 0;
            }
        }
        int[] out = new int[starts.size()];
        for (int i = 0; i < out.length; i++) out[i] = starts.get(i);
        return out;
    }

    /**
     * How full the chart should be here, from how far into a run of the loop it
     * has got.
     *
     * This is the part that is a decision rather than a measurement. A loop that
     * has come round four times is not asking for the same thing the first time
     * through: holding a little back at the start and giving it later is what
     * makes the last time through feel like the top of something instead of the
     * fourth copy.
     */
    private static double repeatCurve(int bar, int[] sectionStart, int loopBars) {
        if (loopBars <= 0 || sectionStart.length == 0) return 1;
        int start = sectionStart[0];
        for (int candidate : sectionStart) {
            if (candidate <= bar) start = candidate;
            else break;
        }
        int repeats = Math.max(1, (int) Math.ceil((bar - start + 1) / (double) loopBars));
        double position = (bar - start) % loopBars / (double) Math.max(1, loopBars);
        // Within a repeat it climbs, and from repeat to repeat it climbs again,
        // so the section has an inside and an end rather than a flat level.
        return 0.85 + 0.15 * position + 0.05 * Math.min(repeats, 4);
    }

    private static double frameSeconds(double frameIndex) {
        return OnsetCurves.frameSeconds((int) Math.round(frameIndex));
    }

    // ------------------------------------------------------------------ features

    private static final class Curves {
        final double[] strength;
        final double[] kick;
        final double[] snare;
        /** The drum bands' loudness, not their onset: what a waveform is. */
        final double[] envelope;

        private Curves(double[] strength, double[] kick, double[] snare, double[] envelope) {
            this.strength = strength;
            this.kick = kick;
            this.snare = snare;
            this.envelope = envelope;
        }

        static Curves of(short[] pcm) {
            int frames = Math.max(0, (pcm.length - OnsetCurves.FFT_SIZE)
                    / OnsetCurves.HOP_SIZE + 1);
            double[] strength = new double[frames];
            double[] kick = new double[frames];
            double[] snare = new double[frames];
            double[] envelope = new double[frames];
            OnsetCurves.FrameAnalyser analyser = new OnsetCurves.FrameAnalyser();
            double[] hann = OnsetCurves.hann();
            double[] windowed = new double[OnsetCurves.FFT_SIZE];
            for (int frame = 0; frame < frames; frame++) {
                int start = frame * OnsetCurves.HOP_SIZE;
                for (int i = 0; i < OnsetCurves.FFT_SIZE; i++) {
                    windowed[i] = pcm[start + i] / 32768.0 * hann[i];
                }
                analyser.analyse(windowed);
                strength[frame] = analyser.strength;
                kick[frame] = analyser.kickOnset;
                snare[frame] = analyser.snareOnset;
                envelope[frame] = Math.sqrt(Math.max(0, analyser.kickEnergy + analyser.snareEnergy));
            }
            return new Curves(strength, kick, snare, envelope);
        }
    }

    // -------------------------------------------------------------------- grid

    /**
     * The period the song is in.
     *
     * Autocorrelation first, over the whole song, to choose the metrical level:
     * a comb score on its own cannot do this job, because a longer period has
     * fewer chances to be wrong and so always looks tidier than the right
     * answer - the first version of this picked a bar and a half per beat and
     * called the song 42 bpm. Autocorrelation has no such bias. Then the period
     * is refined in fractions of a frame, since a whole frame is five
     * milliseconds and the grid is meant to be tighter than that.
     */
    private static double bestPeriod(double[] curve) {
        int n = curve.length;
        if (n < 2) return 100;
        double mean = 0;
        for (double value : curve) mean += value;
        mean /= n;
        double[] centred = new double[n];
        for (int i = 0; i < n; i++) centred[i] = curve[i] - mean;

        int lo = Math.max(2, (int) Math.floor(60.0 * OnsetCurves.PER_SECOND / BPM_HI));
        int hi = Math.min(n / 2, (int) Math.ceil(60.0 * OnsetCurves.PER_SECOND / BPM_LO));
        double bestScore = -1;
        int best = lo;
        for (int lag = lo; lag <= hi; lag++) {
            double sum = autocorrelation(centred, lag);
            if (sum > bestScore) {
                bestScore = sum;
                best = lag;
            }
        }

        // Which metrical level. Autocorrelation is happy to answer with the
        // eighth note, because a grid that puts a point on every eighth lines up
        // with the music at least as well as one that puts a point on every
        // beat - it simply says less. Doubling is taken wherever it is not
        // clearly worse: a slow reading that fits is the beat a person would
        // tap, and a fast one is the same music counted twice.
        int window = (int) (PHASE_WINDOW_SECONDS * OnsetCurves.PER_SECOND);
        double reference = autocorrelation(centred, best);
        if (verbose) {
            System.out.printf(java.util.Locale.US,
                    "  autocorrelation lag %d (%.1f bpm) scores %.4f%n",
                    best, 60.0 * OnsetCurves.PER_SECOND / best, reference);
        }
        for (int multiple = 2; multiple <= 4; multiple++) {
            int candidate = best * multiple;
            if (candidate > hi) break;
            double score = autocorrelation(centred, candidate);
            if (verbose) {
                System.out.printf(java.util.Locale.US,
                        "  x%d lag %d (%.1f bpm) scores %.4f%n",
                        multiple, candidate, 60.0 * OnsetCurves.PER_SECOND / candidate,
                        score);
            }
            if (score < reference * 0.8) break;
            best = candidate;
            reference = score;
        }

        double bestFine = best;
        double bestFineScore = combMean(curve, best, window);
        for (double period = best * 0.97; period <= best * 1.03; period += 0.25) {
            if (period < 2) continue;
            double score = combMean(curve, period, window);
            if (score > bestFineScore) {
                bestFineScore = score;
                bestFine = period;
            }
        }
        return bestFine;
    }

    /**
     * Mean onset strength at the grid this period draws, best phase per window.
     *
     * Each part of the song gets to choose its own phase, which is what makes it
     * a measurement of the period rather than of one lucky alignment.
     */
    private static double combMean(double[] curve, double period, int window) {
        double total = 0;
        int windows = 0;
        int steps = (int) Math.ceil(period);
        for (int start = 0; start + window <= curve.length; start += window) {
            double best = 0;
            int count = 0;
            for (int phase = 0; phase < steps; phase++) {
                double sum = 0;
                int points = 0;
                for (double at = start + phase; at < start + window; at += period) {
                    int index = (int) Math.round(at);
                    if (index >= 0 && index < curve.length) {
                        sum += curve[index];
                        points++;
                    }
                }
                if (points > 0 && sum / points > best) {
                    best = sum / points;
                    count = points;
                }
            }
            if (count > 0) {
                total += best;
                windows++;
            }
        }
        return windows == 0 ? 0 : total / windows;
    }

    private static double autocorrelation(double[] centred, int lag) {
        double sum = 0;
        for (int i = 0; i + lag < centred.length; i++) sum += centred[i] * centred[i + lag];
        return sum / (centred.length - lag);
    }

    private static final class Fit {
        double phaseFrame;
        double periodFrames;
    }

    /**
     * Where the grid actually sits, and how fast it is really going.
     *
     * The phase is found for each part of the song on its own, then unwrapped
     * into a continuous line and fitted. A period fitted once at the start is
     * never quite right three minutes later - that is the drift the live path
     * needs a phase-locked loop for - and here it can simply be measured.
     */
    private static Fit fitGrid(double[] curve, double period) {
        int window = (int) (PHASE_WINDOW_SECONDS * OnsetCurves.PER_SECOND);
        List<double[]> phases = new ArrayList<>();
        int steps = (int) Math.ceil(period);
        for (int start = 0; start + window <= curve.length; start += window) {
            double best = -1;
            int bestPhase = 0;
            for (int phase = 0; phase < steps; phase++) {
                double sum = 0;
                int count = 0;
                for (double at = start + phase; at < start + window; at += period) {
                    int index = (int) Math.round(at);
                    if (index >= 0 && index < curve.length) {
                        sum += curve[index];
                        count++;
                    }
                }
                double score = count == 0 ? 0 : sum / count;
                if (score > best) {
                    best = score;
                    bestPhase = phase;
                }
            }
            phases.add(new double[] {start + window / 2.0, bestPhase});
        }
        Fit fit = new Fit();
        if (phases.size() < 2) {
            fit.phaseFrame = phases.isEmpty() ? 0 : phases.get(0)[1];
            fit.periodFrames = period;
            return fit;
        }

        // Unwrap: each window's phase is only known within one period, so pick
        // the copy nearest to where the last window said the grid would be.
        double[] absolute = new double[phases.size()];
        absolute[0] = phases.get(0)[1];
        for (int i = 1; i < phases.size(); i++) {
            double centre = phases.get(i)[0];
            double previousCentre = phases.get(i - 1)[0];
            double expected = absolute[i - 1] + (centre - previousCentre);
            double phase = phases.get(i)[1];
            double k = Math.round((expected - phase) / period);
            absolute[i] = phase + k * period;
        }

        double meanX = 0, meanY = 0;
        for (int i = 0; i < phases.size(); i++) {
            meanX += phases.get(i)[0];
            meanY += absolute[i];
        }
        meanX /= phases.size();
        meanY /= phases.size();
        double covariance = 0, variance = 0;
        for (int i = 0; i < phases.size(); i++) {
            double dx = phases.get(i)[0] - meanX;
            covariance += dx * (absolute[i] - meanY);
            variance += dx * dx;
        }
        double slope = variance == 0 ? 0 : covariance / variance;
        // A grid that speeds up or slows down by more than a couple of percent
        // across a song is not a grid that drifted; it is a fit that went wrong.
        slope = Math.max(-MAX_DRIFT, Math.min(MAX_DRIFT, slope));
        fit.periodFrames = period * (1 + slope);
        fit.phaseFrame = meanY - slope * meanX;
        return fit;
    }
}
