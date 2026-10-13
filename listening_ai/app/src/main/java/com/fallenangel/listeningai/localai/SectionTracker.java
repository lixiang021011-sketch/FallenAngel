package com.fallenangel.listeningai.localai;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

/** Bounded streaming novelty segmentation and coarse acoustic-family matching.
 * Compares 4 seconds before/after a boundary, confirms peaks for 1 second.
 * For dot-product similarity, mean-vector squared distance equals box-checkerboard novelty
 * (up to scale); no whole-song quadratic similarity matrix is stored.
 */
final class SectionTracker {
    static final int SIDE = 8, CONFIRM = 2, CAPACITY = 128, MAX_FAMILIES = 32;
    static final long MIN_SECTION = 8L * 44100, DELAY = 5L * 44100;
    static final double MIN_NOVELTY = 0.065, FAMILY_DISTANCE = 0.06, SILENCE = 0.004;
    private final ArrayDeque<SectionFeatureExtractor.Feature> history = new ArrayDeque<>();
    private final ArrayDeque<SectionFeatureExtractor.Feature> uncommitted = new ArrayDeque<>();
    private final ArrayDeque<Peak> peaks = new ArrayDeque<>();
    private final ArrayDeque<Double> scores = new ArrayDeque<>();
    private final ArrayDeque<SectionObservation> pending = new ArrayDeque<>();
    private final List<Family> families = new ArrayList<>();
    private final double[] currentSum = new double[SectionFeatureExtractor.DIMENSIONS];
    private long streamId, sectionStart, dropped, previousEnd = -1;
    private int currentLabel = -1, currentCount, sectionIndex;

    private static final class Family {
        double[] mean;
        int sections;
        Family(double[] seed) { mean = seed.clone(); }
    }

    private static final class Peak {
        long boundary;
        double score, threshold;
        double[] right;
        boolean audible;
    }

    void reset() {
        history.clear(); uncommitted.clear(); peaks.clear(); scores.clear(); pending.clear(); families.clear();
        Arrays.fill(currentSum, 0);
        currentCount = 0;
        sectionIndex = 0;
        currentLabel = -1;
        previousEnd = -1;
        streamId++;
    }

    /** Pause/resume keeps the learned families and current section, but never compares across a gap. */
    void breakContext() {
        for (SectionFeatureExtractor.Feature f : uncommitted) commit(f);
        uncommitted.clear(); history.clear(); peaks.clear(); scores.clear(); pending.clear();
        previousEnd = -1;
    }

    void accept(SectionFeatureExtractor.Feature feature) {
        if (previousEnd >= 0 && feature.start != previousEnd) {
            throw new IllegalArgumentException("non-contiguous section features; reset context first");
        }
        previousEnd = feature.end;
        history.addLast(feature);
        if (history.size() > 2 * SIDE) history.removeFirst();
        uncommitted.addLast(feature);
        while (!uncommitted.isEmpty() && uncommitted.peekFirst().end <= feature.end - DELAY) {
            commit(uncommitted.removeFirst());
        }
        List<SectionFeatureExtractor.Feature> window = new ArrayList<>(history);
        if (currentLabel == -1 && window.size() >= SIDE) {
            List<SectionFeatureExtractor.Feature> opening = window.subList(window.size() - SIDE, window.size());
            if (audible(opening)) {
                currentLabel = family(mean(opening));
                sectionStart = opening.get(0).start;
                emit(new SectionObservation(streamId, ++sectionIndex, sectionStart, feature.end, label(currentLabel), "",
                        SectionObservation.Kind.START, 0, 0, 1));
            }
        }
        if (window.size() < 2 * SIDE) return;
        List<SectionFeatureExtractor.Feature> left = window.subList(0, SIDE), right = window.subList(SIDE, 2 * SIDE);
        Peak point = new Peak();
        point.boundary = right.get(0).start;
        point.right = mean(right);
        point.score = distance(mean(left), point.right);
        point.audible = audible(left) && audible(right);
        point.threshold = Math.max(MIN_NOVELTY, medianScore() * 3 + 0.015);
        scores.addLast(point.score);
        if (scores.size() > 120) scores.removeFirst();
        peaks.addLast(point);
        if (peaks.size() < 2 * CONFIRM + 1) return;
        List<Peak> candidates = new ArrayList<>(peaks);
        Peak best = candidates.get(CONFIRM);
        boolean maximum = true;
        for (int i = 0; i < candidates.size(); i++) {
            // Break plateaus consistently in favour of the earliest peak.
            if (i != CONFIRM && (candidates.get(i).score > best.score
                    || (i < CONFIRM && candidates.get(i).score == best.score))) maximum = false;
        }
        if (currentLabel != -1 && best.audible && maximum && best.score >= best.threshold
                && best.boundary - sectionStart >= MIN_SECTION) {
            finalizeCurrent();
            int oldLabel = currentLabel, countBefore = families.size();
            double nearestDistance = nearestDistance(best.right);
            currentLabel = family(best.right);
            sectionStart = best.boundary;
            Arrays.fill(currentSum, 0);
            currentCount = 0;
            SectionObservation.Kind kind = currentLabel >= 0 && currentLabel < countBefore && currentLabel != oldLabel
                    ? SectionObservation.Kind.RETURN : SectionObservation.Kind.CHANGE;
            emit(new SectionObservation(streamId, ++sectionIndex, best.boundary, feature.end,
                    label(currentLabel), label(oldLabel), kind,
                    Math.min(0.99, 0.5 + 0.49 * (1 - best.threshold / best.score)),
                    best.score, Math.max(0, 1 - nearestDistance)));
        }
        peaks.removeFirst();
    }

    private void commit(SectionFeatureExtractor.Feature f) {
        if (currentLabel == -1 || f.start < sectionStart || f.rms < SILENCE) return;
        for (int i = 0; i < currentSum.length; i++) currentSum[i] += f.vector[i];
        currentCount++;
    }

    private void finalizeCurrent() {
        if (currentLabel < 0 || currentCount == 0) return;
        Family target = families.get(currentLabel);
        for (int i = 0; i < currentSum.length; i++) {
            target.mean[i] = (target.mean[i] * target.sections + currentSum[i] / currentCount)
                    / (target.sections + 1);
        }
        target.sections++;
    }

    private int family(double[] descriptor) {
        int closest = -1;
        double best = Double.POSITIVE_INFINITY;
        for (int i = 0; i < families.size(); i++) {
            double d = distance(families.get(i).mean, descriptor);
            if (d < best) { best = d; closest = i; }
        }
        if (best <= FAMILY_DISTANCE) return closest;
        if (families.size() == MAX_FAMILIES) return -2; // Explicit unknown; never force a bad match.
        families.add(new Family(descriptor));
        return families.size() - 1;
    }

    private double nearestDistance(double[] descriptor) {
        double best = 1;
        for (Family known : families) best = Math.min(best, distance(known.mean, descriptor));
        return best;
    }

    private static String label(int index) {
        if (index < 0) return "UNKNOWN";
        return String.valueOf((char) ('A' + index % 26)) + (index < 26 ? "" : (index / 26 + 1));
    }

    private double medianScore() {
        if (scores.isEmpty()) return 0;
        double[] sorted = new double[scores.size()];
        int i = 0;
        for (double score : scores) sorted[i++] = score;
        Arrays.sort(sorted);
        return sorted[sorted.length / 2];
    }

    private static boolean audible(List<SectionFeatureExtractor.Feature> frames) {
        int heard = 0;
        for (SectionFeatureExtractor.Feature f : frames) if (f.rms >= SILENCE) heard++;
        return heard >= frames.size() * 0.875;
    }

    private static double[] mean(List<SectionFeatureExtractor.Feature> frames) {
        double[] out = new double[SectionFeatureExtractor.DIMENSIONS];
        for (SectionFeatureExtractor.Feature f : frames) {
            for (int i = 0; i < out.length; i++) out[i] += f.vector[i] / frames.size();
        }
        return out;
    }

    static double distance(double[] a, double[] b) {
        double result = 0;
        for (int i = 0; i < a.length; i++) result += (a[i] - b[i]) * (a[i] - b[i]);
        return result * 0.5;
    }

    private void emit(SectionObservation observation) {
        if (pending.size() == CAPACITY) { pending.removeFirst(); dropped++; }
        pending.addLast(observation);
    }

    List<SectionObservation> drain() {
        List<SectionObservation> out = new ArrayList<>(pending);
        pending.clear();
        return out;
    }
    long dropped() { return dropped; }
}
