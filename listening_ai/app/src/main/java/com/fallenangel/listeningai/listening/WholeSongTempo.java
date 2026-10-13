package com.fallenangel.listeningai.listening;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

/**
 * The tempo of the song so far, fitted to the onset curve.

 * This is the DAW's arrangement, moved into the app: in a DAW nobody taps a
 * tempo, the project's tempo is read off the whole file and the person only puts
 * one hit on the bar line. Here the person's single bar-head tap gives the phase,
 * and this gives the tempo, so the grid has nothing left to drift with.

 * It reports its best candidates rather than one answer. A comb fit can always be
 * found at half or double the true period - the same ambiguity as the octave
 * question, and the reason that question exists - and which one is right is not
 * decided here: the runner-up belongs in the answer as much as the winner does.
 * The caller settles it with evidence this class does not have, such as whether
 * the bar lines land on kicks or on snares.
 */
public final class WholeSongTempo {
    /** A tempo and how well the onsets line up with it: 1.0 is the curve's own mean. */
    public static final class Candidate {
        public final double bpm;
        /** What the onsets alone say. */
        public final double raw;
        /** What the ranking uses: `raw` with the preference below applied. */
        public final double score;

        Candidate(double bpm, double raw) {
            this.bpm = bpm;
            this.raw = raw;
            this.score = raw * preference(bpm);
        }

        @Override
        public String toString() {
            return String.format(java.util.Locale.US, "%.2f BPM (%.2f)", bpm, score);
        }
    }

    public static final class Result {
        public final List<Candidate> candidates;
        public final boolean usable;
        public final String why;

        Result(List<Candidate> candidates, boolean usable, String why) {
            this.candidates = Collections.unmodifiableList(candidates);
            this.usable = usable;
            this.why = why;
        }

        public Candidate best() {
            return candidates.isEmpty() ? null : candidates.get(0);
        }

        @Override
        public String toString() {
            if (!usable) return "no tempo (" + why + ")";
            StringBuilder out = new StringBuilder();
            for (Candidate candidate : candidates) out.append(candidate).append(' ');
            return out.toString().trim();
        }
    }

    /** How many candidates the answer carries: the winner and its neighbours. */
    private static final int KEEP = 4;
    /** Candidates this close together are the same candidate. */
    private static final double SEPARATION = 0.02;

    /**
     * A gentle preference for the tempos songs are written in.

     * A comb fitted to evenly spaced clicks fits 60 BPM and 120 BPM equally well -
     * measured, in the test next door - so something has to break that tie, and
     * asking the listener "is it 60 or 120?" would be handing back the part of the
     * job the machine is supposed to do. Every sequencer carries a preference like
     * this one; it is written here, with its scores on display, rather than buried
     * in a constant.
     */
    private static double preference(double bpm) {
        if (bpm >= 70 && bpm <= 180) return 1.0;
        if (bpm < 70) return 0.85 + 0.15 * Math.max(0, (bpm - 40) / 30.0);
        return 0.85 + 0.15 * Math.max(0, (240 - bpm) / 60.0);
    }

    private WholeSongTempo() {
    }

    /**
     * Fit the onsets, given where bar one is.

     * The phase is the listener's, and it is taken as given: a grid that is off by
     * half a beat scores badly at every tempo, so the phase has to be right before
     * the fit means anything.
     */
    public static Result fit(OnsetCurve curve, double phaseSeconds,
                             double minBpm, double maxBpm) {
        if (curve == null || curve.size() < 20) {
            return new Result(new ArrayList<>(), false, "还没听到足够长的音乐");
        }
        double mean = curve.mean();
        if (mean <= 0) {
            return new Result(new ArrayList<>(), false, "这段音乐里没有起音");
        }
        List<Candidate> coarse = new ArrayList<>();
        for (double bpm = minBpm; bpm <= maxBpm; bpm += 0.25) {
            coarse.add(new Candidate(bpm, score(curve, phaseSeconds, bpm) / mean));
        }
        coarse.sort(Comparator.comparingDouble((Candidate c) -> -c.score));

        List<Candidate> kept = new ArrayList<>();
        for (Candidate candidate : coarse) {
            boolean far = true;
            for (Candidate taken : kept) {
                if (Math.abs(candidate.bpm - taken.bpm) < SEPARATION * taken.bpm) {
                    far = false;
                    break;
                }
            }
            if (far) kept.add(candidate);
            if (kept.size() >= KEEP) break;
        }

        List<Candidate> fine = new ArrayList<>();
        for (Candidate candidate : kept) {
            double bestBpm = candidate.bpm;
            double bestScore = candidate.score;
            for (double bpm = candidate.bpm - 0.3; bpm <= candidate.bpm + 0.3; bpm += 0.01) {
                if (bpm < minBpm || bpm > maxBpm) continue;
                double got = score(curve, phaseSeconds, bpm) / mean;
                if (got * preference(bpm) > bestScore) {
                    bestScore = got * preference(bpm);
                    bestBpm = bpm;
                }
            }
            fine.add(new Candidate(bestBpm, bestScore));
        }
        fine.sort(Comparator.comparingDouble((Candidate c) -> -c.score));
        return new Result(fine, true, "");
    }

    /** The mean onset strength on this tempo's grid, starting from the phase. */
    private static double score(OnsetCurve curve, double phaseSeconds, double bpm) {
        double period = 60.0 / bpm;
        double frameSeconds = curve.frameSeconds();
        double length = curve.seconds();
        double total = 0;
        int count = 0;
        for (double t = phaseSeconds - period * Math.floor(phaseSeconds / period); t < length;
             t += period) {
            if (t < 0) continue;
            total += curve.at((int) (t / frameSeconds));
            count++;
        }
        return count == 0 ? 0 : total / count;
    }
}
