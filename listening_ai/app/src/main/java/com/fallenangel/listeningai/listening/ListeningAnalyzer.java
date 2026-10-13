package com.fallenangel.listeningai.listening;

import com.fallenangel.listeningai.localai.AudioFeatures;
import com.fallenangel.listeningai.localai.PcmFeatureExtractor;
import com.fallenangel.listeningai.localai.SectionFeatureExtractor;

import java.util.ArrayList;
import java.util.List;

/**
 * Why might the listener have stopped here?
 *
 * Given the seconds leading up to a pause, this proposes the explanations the
 * audio can actually support, and says which evidence supports each. It is
 * deliberately a small set of measurable contrasts - onset rate, drum pattern,
 * anticipation, timbre, harmony, density - because the point of the exercise is
 * to find out where these measures are *not* enough. A proposal that is wrong
 * in an interesting way is the most useful thing this can produce; a proposal
 * that is vague is not.
 *
 * The moment the listener stopped on is taken to be in the last few seconds
 * before the pause - people pause just after noticing something, not long
 * before - so the window is split there and the two sides are compared.
 */
public final class ListeningAnalyzer {
    /** How much of the window is taken as "what they just heard". */
    private static final double RECENT_SECONDS = 3.0;
    /** Below this there is not enough evidence to name a reason. */
    private static final double FLOOR = 0.25;
    private static final int MAX_CANDIDATES = 4;
    /** Frames closer than this are one drum hit, not two. */
    private static final double ONSET_GAP_SECONDS = 0.08;
    private static final int SAMPLE_RATE = 44100;
    private static final int BANDS = 24, DIMENSIONS = 37;

    private ListeningAnalyzer() {
    }

    /** Where a moment sits on the beat grid. Supplied because a grid is not audio. */
    public interface Grid {
        /** Position of a moment within its bar, or -1 when the grid is not up. */
        int positionInBar(double seconds);

        /** Which bar of the song a moment falls in, counting from the first. */
        default int barAt(double seconds) {
            return -1;
        }

        int positionsPerBar();

        int subdivisionsPerBeat();

        /** Position within the beat, 0..subdivisions-1. */
        default int subdivisionOf(double seconds) {
            int position = positionInBar(seconds);
            return position < 0 ? -1 : position % subdivisionsPerBeat();
        }
    }

    /** A grid that knows nothing: candidates that need it are simply not offered. */
    public static final Grid NO_GRID = new Grid() {
        @Override public int positionInBar(double seconds) { return -1; }
        @Override public int positionsPerBar() { return 16; }
        @Override public int subdivisionsPerBeat() { return 4; }
    };

    /**
     * @param pcm        the window ending at the pause
     * @param startSample capture sample of pcm[0], so proposals keep the song's clock
     */
    public static PauseQuestion analyse(short[] pcm, int offset, int count, long startSample,
                                        Grid grid, String sectionLabel, long sectionBoundarySample) {
        PcmFeatureExtractor.checkRange(pcm, offset, count);
        if (count < SAMPLE_RATE / 2) {
            return new PauseQuestion("你刚才可能注意到的是：", startSample + count, sectionLabel,
                    -1, -1, -1, onlyNone());
        }

        long endSample = startSample + count;
        long split = Math.max(startSample, endSample - (long) (RECENT_SECONDS * SAMPLE_RATE));

        List<AudioFeatures> frames = onsetFrames(pcm, offset, count, startSample);
        List<Onset> onsets = pickOnsets(frames);
        List<SectionFeatureExtractor.Feature> bins = descriptors(pcm, offset, count, startSample);

        List<PauseQuestion.Candidate> found = new ArrayList<>();
        List<Long> targets = new ArrayList<>();

        addDensityChange(onsets, split, found, targets);
        addAnticipation(onsets, split, grid, found, targets);
        addFill(onsets, split, found, targets);
        addPatternChange(onsets, bins, split, grid, found, targets);
        addTimbreChange(bins, split, found, targets);
        addHarmonyChange(bins, split, found, targets);
        addLevelChange(frames, split, found, targets);
        addSectionChange(startSample, endSample, split, sectionBoundarySample, sectionLabel,
                found, targets);

        sort(found, targets);
        List<PauseQuestion.Candidate> offered = new ArrayList<>();
        long target = endSample;
        for (int i = 0; i < found.size() && offered.size() < MAX_CANDIDATES; i++) {
            if (found.get(i).confidence < FLOOR) continue;
            offered.add(found.get(i));
            if (offered.size() == 1) target = targets.get(i);
        }
        offered.add(new PauseQuestion.Candidate("none", PauseQuestion.NONE_LABEL, 0, ""));

        double targetSeconds = target / (double) SAMPLE_RATE;
        int position = grid == null ? -1 : grid.positionInBar(targetSeconds);
        int perBar = grid == null ? 16 : grid.positionsPerBar();
        int perBeat = grid == null ? 4 : grid.subdivisionsPerBeat();
        // The bar a listener stopped in is the song's bar, not its position
        // inside one bar - which was always the first bar, and therefore never
        // said anything.
        int bar = grid == null ? -1 : grid.barAt(targetSeconds);
        if (bar < 0) bar = position < 0 ? -1 : position / perBar;
        int beat = position < 0 ? -1 : (position % perBar) / perBeat + 1;
        int subdivision = position < 0 ? -1 : position % perBeat;

        return new PauseQuestion("你刚才可能注意到的是：", target, sectionLabel,
                bar, beat, subdivision, offered);
    }

    private static List<PauseQuestion.Candidate> onlyNone() {
        List<PauseQuestion.Candidate> out = new ArrayList<>();
        out.add(new PauseQuestion.Candidate("none", PauseQuestion.NONE_LABEL, 0, ""));
        return out;
    }

    /**
     * The same question, with explanations from somewhere else.
     *
     * Where a moment is - the time, the bar, the section - is measured here and
     * does not change. What a moment *meant* is asked of a model, and this is
     * how its answer is put back onto the measurement rather than replacing it:
     * the anchor has to stay the thing this project is sure of.
     */
    public static PauseQuestion withCandidates(PauseQuestion base,
                                               List<PauseQuestion.Candidate> candidates) {
        return new PauseQuestion(base.question, base.targetSample, base.sectionLabel,
                base.bar, base.beat, base.subdivision, new ArrayList<>(candidates));
    }

    // ------------------------------------------------------------------ evidence

    /** Did it get busier or calmer where they stopped? */
    private static void addDensityChange(List<Onset> onsets, long split,
                                         List<PauseQuestion.Candidate> out, List<Long> targets) {
        double before = rate(onsets, Long.MIN_VALUE, split);
        double after = rate(onsets, split, Long.MAX_VALUE);
        if (before < 0.5) return;
        double ratio = after / before;
        if (ratio >= 1.3) {
            out.add(new PauseQuestion.Candidate("density_up", "编曲密度增加",
                    Math.min(1, (ratio - 1) / 1.2),
                    String.format(java.util.Locale.US,
                            "起音密度 %.1f/s → %.1f/s", before, after)));
            targets.add(lastPast(onsets, split, Long.MAX_VALUE));
        } else if (ratio <= 0.7) {
            out.add(new PauseQuestion.Candidate("density_down", "编曲变薄、留出空间",
                    Math.min(1, (1 - ratio) / 0.6),
                    String.format(java.util.Locale.US,
                            "起音密度 %.1f/s → %.1f/s", before, after)));
            targets.add(firstPast(onsets, split));
        }
    }

    /**
     * A low hit landing before the beat it belongs to - the one gesture that most
     * often *is* the push forward people notice.
     */
    private static void addAnticipation(List<Onset> onsets, long split, Grid grid,
                                        List<PauseQuestion.Candidate> out, List<Long> targets) {
        if (grid == null) return;
        double median = medianStrength(onsets);
        if (median <= 0) return;
        for (Onset onset : onsets) {
            if (onset.sample < split || !onset.low) continue;
            int subdivision = grid.subdivisionOf(onset.sample / (double) SAMPLE_RATE);
            if (subdivision != grid.subdivisionsPerBeat() - 1) continue;
            if (onset.strength < median * 1.5) continue;
            out.add(new PauseQuestion.Candidate("anticipation", "低音提前进入（抢在拍前）",
                    Math.min(1, (onset.strength / median - 1) / 2),
                    String.format(java.util.Locale.US,
                            "拍前 %s 位置的强低频起音（%.1f× 中位强度）",
                            "a", onset.strength / median)));
            targets.add(onset.sample);
            return;
        }
    }

    /** Several hits crowded into one second: a fill, or a roll. */
    private static void addFill(List<Onset> onsets, long split,
                                List<PauseQuestion.Candidate> out, List<Long> targets) {
        long span = (long) (0.9 * SAMPLE_RATE);
        int best = 0;
        long bestAt = -1;
        for (int i = 0; i < onsets.size(); i++) {
            int count = 0;
            for (int j = i; j < onsets.size() && onsets.get(j).sample - onsets.get(i).sample <= span; j++) {
                count++;
            }
            if (count > best) {
                best = count;
                bestAt = onsets.get(i).sample;
            }
        }
        double before = rate(onsets, Long.MIN_VALUE, split);
        if (best >= 4 && bestAt >= split && best >= before * 1.5) {
            out.add(new PauseQuestion.Candidate("fill", "出现 fill / 密集过门",
                    Math.min(1, (best - 3) / 5.0),
                    String.format(java.util.Locale.US, "%.1f 秒内 %d 个起音", 0.9, best)));
            targets.add(bestAt);
        }
    }

    /**
     * The drum figure changed: same tempo, different shape.
     *
     * Counted at eighth-note resolution and required to be *concentrated*. The
     * first version compared all sixteen places and fired on nearly every pause:
     * a busy track differs a little everywhere, so "the pattern changed" was the
     * most common explanation offered, which makes it worthless as an
     * explanation - the most common candidate cannot be the answer. A change
     * somebody stops on has a place.
     */
    private static void addPatternChange(List<Onset> onsets, List<SectionFeatureExtractor.Feature> bins,
                                         long split, Grid grid,
                                         List<PauseQuestion.Candidate> out, List<Long> targets) {
        if (grid == null) return;
        double[] before = pattern(onsets, Long.MIN_VALUE, split, grid, 2);
        double[] after = pattern(onsets, split, Long.MAX_VALUE, grid, 2);
        if (sum(before) < 3 || sum(after) < 3) return;
        double correlation = correlation(before, after);
        if (correlation >= 0.55) return;
        double difference = 0;
        double peak = 0;
        int peakAt = 0;
        for (int i = 0; i < before.length; i++) {
            double delta = Math.abs(after[i] - before[i]);
            difference += delta;
            if (delta > peak) {
                peak = delta;
                peakAt = i;
            }
        }
        if (difference <= 0 || peak < difference * 0.45) return;
        out.add(new PauseQuestion.Candidate("pattern", "鼓型发生变化",
                Math.min(1, (0.55 - correlation) / 0.55),
                String.format(java.util.Locale.US,
                        "八分位置上的鼓型相似度 %.2f，变化集中在第 %d 个八分",
                        correlation, peakAt + 1)));
        targets.add(lastPast(onsets, split, Long.MAX_VALUE));
    }

    /** New instruments, or the same ones rearranged. */
    private static void addTimbreChange(List<SectionFeatureExtractor.Feature> bins, long split,
                                        List<PauseQuestion.Candidate> out, List<Long> targets) {
        double[] before = meanBands(bins, Long.MIN_VALUE, split);
        double[] after = meanBands(bins, split, Long.MAX_VALUE);
        if (before == null || after == null) return;
        double distance = Math.sqrt(SectionTrackerDistance(before, after));
        double baseline = medianDistanceWithin(bins, split);
        if (baseline <= 0 || distance < baseline * 1.6) return;
        out.add(new PauseQuestion.Candidate("timbre", "音色变化 / 新乐器进入",
                Math.min(1, (distance / baseline - 1.6) / 2.4),
                String.format(java.util.Locale.US, "频段分布变化 %.3f（窗口内基线 %.3f）",
                        distance, baseline)));
        targets.add(lastBinPast(bins, split));
    }

    /** The chord under it changed. */
    private static void addHarmonyChange(List<SectionFeatureExtractor.Feature> bins, long split,
                                         List<PauseQuestion.Candidate> out, List<Long> targets) {
        double[] before = meanChroma(bins, Long.MIN_VALUE, split);
        double[] after = meanChroma(bins, split, Long.MAX_VALUE);
        if (before == null || after == null) return;
        double distance = Math.sqrt(SectionTrackerDistance(before, after));
        double baseline = medianChromaDistanceWithin(bins, split);
        if (baseline <= 0 || distance < baseline * 1.5) return;
        out.add(new PauseQuestion.Candidate("harmony", "和声发生变化",
                Math.min(1, (distance / baseline - 1.5) / 2.5),
                String.format(java.util.Locale.US, "音级分布变化 %.3f（窗口内基线 %.3f）",
                        distance, baseline)));
        targets.add(lastBinPast(bins, split));
    }

    /**
     * It got louder or softer as a whole.
     *
     * Not the same measurement as density: a passage can keep the same hits and
     * still lift, which is most of what a chorus does. People notice that far
     * more reliably than they notice a change in the number of onsets, and it
     * needs no beat grid at all - which matters, because the grid is not always
     * locked and the explanations must not all disappear with it.
     */
    private static void addLevelChange(List<AudioFeatures> frames, long split,
                                       List<PauseQuestion.Candidate> out, List<Long> targets) {
        double before = meanRms(frames, Long.MIN_VALUE, split);
        double after = meanRms(frames, split, Long.MAX_VALUE);
        if (before <= 0.002 || after <= 0.002) return;
        double ratio = after / before;
        if (ratio >= 1.35) {
            out.add(new PauseQuestion.Candidate("level_up", "整体音量上升（段落推进）",
                    Math.min(1, (ratio - 1.35) / 0.9),
                    String.format(java.util.Locale.US, "平均音量上升 %.2f×", ratio)));
            targets.add(lastFramePast(frames, split));
        } else if (ratio <= 0.74) {
            out.add(new PauseQuestion.Candidate("level_down", "整体音量下降（收束或留白）",
                    Math.min(1, (0.74 - ratio) / 0.5),
                    String.format(java.util.Locale.US, "平均音量下降到 %.2f×", ratio)));
            targets.add(firstFramePast(frames, split));
        }
    }

    /**
     * A new part of the song started right where the listener stopped.
     *
     * The section analysis already knows this, and it is the one explanation
     * that is a fact about the song rather than a measurement of the passage: if
     * the music changed part a moment ago, that is very often what somebody
     * stopped on. It is offered even though the rules above found nothing,
     * because it is the case where having nothing to say is least likely to be
     * right.
     */
    private static void addSectionChange(long startSample, long endSample, long split,
                                         long sectionBoundarySample, String sectionLabel,
                                         List<PauseQuestion.Candidate> out, List<Long> targets) {
        if (sectionBoundarySample < 0) return;
        if (sectionBoundarySample < startSample || sectionBoundarySample > endSample) return;
        double ago = (endSample - sectionBoundarySample) / (double) SAMPLE_RATE;
        out.add(new PauseQuestion.Candidate("section", "进入了新的段落",
                Math.max(0.3, 1 - ago / RECENT_SECONDS),
                String.format(java.util.Locale.US, "%.1f 秒前进入段落 %s",
                        ago, sectionLabel == null || sectionLabel.isEmpty() ? "?" : sectionLabel)));
        targets.add(sectionBoundarySample);
    }

    private static double meanRms(List<AudioFeatures> frames, long from, long to) {
        double sum = 0;
        int count = 0;
        for (AudioFeatures frame : frames) {
            if (frame.endSample < from || frame.endSample >= to) continue;
            sum += frame.rms;
            count++;
        }
        return count == 0 ? 0 : sum / count;
    }

    private static long lastFramePast(List<AudioFeatures> frames, long split) {
        long found = -1;
        for (AudioFeatures frame : frames) if (frame.endSample >= split) found = frame.endSample;
        return found < 0 ? split : found;
    }

    private static long firstFramePast(List<AudioFeatures> frames, long split) {
        for (AudioFeatures frame : frames) if (frame.endSample >= split) return frame.endSample;
        return split;
    }

    // -------------------------------------------------------------------- inputs

    private static final class Onset {
        final long sample;
        final double strength;
        final boolean low;

        Onset(long sample, double strength, boolean low) {
            this.sample = sample;
            this.strength = strength;
            this.low = low;
        }
    }

    private static List<AudioFeatures> onsetFrames(short[] pcm, int offset, int count, long start) {
        PcmFeatureExtractor extractor = new PcmFeatureExtractor();
        extractor.reset(start);
        List<AudioFeatures> out = new ArrayList<>();
        extractor.push(pcm, offset, count, out::add);
        return out;
    }

    private static List<SectionFeatureExtractor.Feature> descriptors(short[] pcm, int offset,
                                                                    int count, long start) {
        SectionFeatureExtractor extractor = new SectionFeatureExtractor();
        extractor.reset(start);
        List<SectionFeatureExtractor.Feature> out = new ArrayList<>();
        extractor.push(pcm, offset, count, out::add);
        return out;
    }

    /** Frames loud enough and rising enough to be a drum hit, thinned to one per hit. */
    private static List<Onset> pickOnsets(List<AudioFeatures> frames) {
        double[] strengths = new double[frames.size()];
        for (int i = 0; i < frames.size(); i++) {
            strengths[i] = Math.max(frames.get(i).kickOnset, frames.get(i).snareOnset);
        }
        double median = median(strengths);
        double threshold = Math.max(0.6, median * 2.2);
        List<Onset> out = new ArrayList<>();
        long last = Long.MIN_VALUE;
        for (int i = 0; i < frames.size(); i++) {
            AudioFeatures f = frames.get(i);
            double strength = Math.max(f.kickOnset, f.snareOnset);
            if (strength <= threshold || f.rms < 0.004) continue;
            long sample = f.endSample;
            if (last != Long.MIN_VALUE && sample - last < ONSET_GAP_SECONDS * SAMPLE_RATE) continue;
            out.add(new Onset(sample, strength, f.kickOnset >= f.snareOnset));
            last = sample;
        }
        return out;
    }

    private static double rate(List<Onset> onsets, long from, long to) {
        int count = 0;
        for (Onset onset : onsets) if (onset.sample >= from && onset.sample < to) count++;
        long span = (to == Long.MAX_VALUE ? lastSample(onsets) : to) - (from == Long.MIN_VALUE ? firstSample(onsets) : from);
        double seconds = span / (double) SAMPLE_RATE;
        return seconds <= 0.2 ? 0 : count / seconds;
    }

    private static long firstSample(List<Onset> onsets) {
        return onsets.isEmpty() ? 0 : onsets.get(0).sample;
    }

    private static long lastSample(List<Onset> onsets) {
        return onsets.isEmpty() ? 0 : onsets.get(onsets.size() - 1).sample;
    }

    private static long firstPast(List<Onset> onsets, long split) {
        for (Onset onset : onsets) if (onset.sample >= split) return onset.sample;
        return lastSample(onsets);
    }

    private static long lastPast(List<Onset> onsets, long from, long to) {
        long found = -1;
        for (Onset onset : onsets) if (onset.sample >= from && onset.sample < to) found = onset.sample;
        return found < 0 ? firstPast(onsets, from) : found;
    }

    private static long lastBinPast(List<SectionFeatureExtractor.Feature> bins, long split) {
        long found = -1;
        for (SectionFeatureExtractor.Feature bin : bins) if (bin.start >= split) found = bin.end;
        return found < 0 ? (bins.isEmpty() ? 0 : bins.get(bins.size() - 1).end) : found;
    }

    private static double medianStrength(List<Onset> onsets) {
        double[] values = new double[onsets.size()];
        for (int i = 0; i < values.length; i++) values[i] = onsets.get(i).strength;
        return median(values);
    }

    private static double median(double[] values) {
        if (values.length == 0) return 0;
        double[] sorted = values.clone();
        java.util.Arrays.sort(sorted);
        return sorted[sorted.length / 2];
    }

    /**
     * Onset strength per place in the bar, at the given division of the grid.
     *
     * @param perPosition grid positions per place, so 2 pools the sixteenths into
     *                    eighths
     */
    private static double[] pattern(List<Onset> onsets, long from, long to, Grid grid,
                                    int perPosition) {
        double[] out = new double[Math.max(1, grid.positionsPerBar() / perPosition)];
        for (Onset onset : onsets) {
            if (onset.sample < from || onset.sample >= to) continue;
            int position = grid.positionInBar(onset.sample / (double) SAMPLE_RATE);
            if (position < 0) continue;
            int index = position / perPosition;
            if (index < out.length) out[index] += onset.strength;
        }
        return out;
    }

    private static double correlation(double[] a, double[] b) {
        double meanA = sum(a) / a.length, meanB = sum(b) / b.length;
        double numerator = 0, spreadA = 0, spreadB = 0;
        for (int i = 0; i < a.length; i++) {
            double x = a[i] - meanA, y = b[i] - meanB;
            numerator += x * y;
            spreadA += x * x;
            spreadB += y * y;
        }
        if (spreadA <= 0 || spreadB <= 0) return 0;
        return numerator / Math.sqrt(spreadA * spreadB);
    }

    private static double sum(double[] values) {
        double total = 0;
        for (double value : values) total += value;
        return total;
    }

    private static double[] meanBands(List<SectionFeatureExtractor.Feature> bins, long from, long to) {
        return mean(bins, from, to, 0, BANDS);
    }

    private static double[] meanChroma(List<SectionFeatureExtractor.Feature> bins, long from, long to) {
        return mean(bins, from, to, BANDS, BANDS + 12);
    }

    private static double[] mean(List<SectionFeatureExtractor.Feature> bins, long from, long to,
                                 int dimensionFrom, int dimensionTo) {
        double[] out = new double[dimensionTo - dimensionFrom];
        int count = 0;
        for (SectionFeatureExtractor.Feature bin : bins) {
            if (bin.start < from || bin.end > to) continue;
            for (int i = 0; i < out.length; i++) out[i] += bin.vector[dimensionFrom + i];
            count++;
        }
        if (count == 0) return null;
        for (int i = 0; i < out.length; i++) out[i] /= count;
        return out;
    }

    /** How much these descriptors usually differ from each other in this window. */
    private static double medianDistanceWithin(List<SectionFeatureExtractor.Feature> bins, long split) {
        List<Double> distances = new ArrayList<>();
        for (int i = 1; i < bins.size(); i++) {
            double[] a = new double[DIMENSIONS];
            System.arraycopy(bins.get(i - 1).vector, 0, a, 0, DIMENSIONS);
            distances.add(Math.sqrt(distance(a, bins.get(i).vector, 0, BANDS)));
        }
        return median(list(distances));
    }

    private static double medianChromaDistanceWithin(List<SectionFeatureExtractor.Feature> bins,
                                                     long split) {
        List<Double> distances = new ArrayList<>();
        for (int i = 1; i < bins.size(); i++) {
            distances.add(Math.sqrt(distance(bins.get(i - 1).vector, bins.get(i).vector,
                    BANDS, BANDS + 12)));
        }
        return median(list(distances));
    }

    private static double[] list(List<Double> values) {
        double[] out = new double[values.size()];
        for (int i = 0; i < out.length; i++) out[i] = values.get(i);
        return out;
    }

    private static double distance(double[] a, double[] b, int from, int to) {
        double result = 0;
        for (int i = from; i < to; i++) result += (a[i] - b[i]) * (a[i] - b[i]);
        return result * 0.5;
    }

    private static double SectionTrackerDistance(double[] a, double[] b) {
        double result = 0;
        for (int i = 0; i < a.length; i++) result += (a[i] - b[i]) * (a[i] - b[i]);
        return result * 0.5;
    }

    private static void sort(List<PauseQuestion.Candidate> candidates, List<Long> targets) {
        for (int i = 1; i < candidates.size(); i++) {
            for (int j = i; j > 0 && candidates.get(j).confidence > candidates.get(j - 1).confidence; j--) {
                PauseQuestion.Candidate candidate = candidates.get(j);
                candidates.set(j, candidates.get(j - 1));
                candidates.set(j - 1, candidate);
                Long target = targets.get(j);
                targets.set(j, targets.get(j - 1));
                targets.set(j - 1, target);
            }
        }
    }
}
