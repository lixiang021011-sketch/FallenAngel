package com.fallenangel.listeningai.listening;

import java.util.Collections;
import java.util.List;

/**
 * The question the system asks about a moment the listener stopped on.
 *
 * The listener's pause says *this mattered*. It does not say why, and the
 * analysis cannot know: the same off-beat kick is the whole point of one groove
 * and decoration in the next. So what the analysis produces here is a ranked
 * set of explanations it can defend from evidence, each with how strong that
 * evidence is, and the listener's answer is what turns one of them into
 * knowledge.
 *
 * Every candidate carries the evidence behind it, because a proposal that
 * cannot say what made it propose is not auditable, and this system is supposed
 * to end up as a traceable record rather than a pile of labels.
 */
public final class PauseQuestion {
    /** Bumped whenever the candidate rules change: two versions are not the same data. */
    public static final String VERSION = "candidates-v1";
    /** Always offered, and never scored: the analysis may simply be wrong. */
    public static final String NONE_LABEL = "都不是";

    public static final class Candidate {
        public final String id;
        public final String label;
        /** Heuristic strength of the evidence, 0..1. Not a calibrated probability. */
        public final double confidence;
        /** What was measured, in words, so the proposal can be checked later. */
        public final String evidence;

        public Candidate(String id, String label, double confidence, String evidence) {
            this.id = id;
            this.label = label;
            this.confidence = Math.max(0, Math.min(1, confidence));
            this.evidence = evidence;
        }
    }

    public final String question;
    /** Sample the evidence points at, on the capture clock. */
    public final long targetSample;
    public final String sectionLabel;
    /** Bar, beat and subdivision of the target, or -1 when the grid is not up. */
    public final int bar, beat, subdivision;
    public final List<Candidate> candidates;

    /**
     * Public because there are two producers of questions now: the analysis that
     * reads a pause, and the query layer that decides on its own. Both hand the
     * same thing to the same card.
     */
    public PauseQuestion(String question, long targetSample, String sectionLabel,
                         int bar, int beat, int subdivision, List<Candidate> candidates) {
        this.question = question;
        this.targetSample = targetSample;
        this.sectionLabel = sectionLabel == null ? "" : sectionLabel;
        this.bar = bar;
        this.beat = beat;
        this.subdivision = subdivision;
        this.candidates = Collections.unmodifiableList(candidates);
    }

    public double targetSeconds() {
        return targetSample / 44100.0;
    }

    /** The musical name of the subdivision: 1, e, &, a. */
    public String subdivisionName() {
        switch (subdivision) {
            case 0: return "1";
            case 1: return "e";
            case 2: return "&";
            case 3: return "a";
            default: return "?";
        }
    }
}
