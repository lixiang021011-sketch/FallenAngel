package com.fallenangel.listeningai.listening.query;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * One moment of music, as everything upstream understands it.
 *
 * This is the layer's only input, and it is a snapshot rather than a live view:
 * the policy answers a question about a thing that has already happened, and a
 * decision that changes because the music moved on is a decision nobody can
 * check afterwards.
 *
 * `eventCandidates` is what the moment might have been; `overallConfidence` is
 * how well any of them is understood, which is a different thing from how
 * interesting the moment is (`noveltyScore`). Almost every rule below is about
 * the gap between those two.
 */
public final class MusicMoment {
    public final String momentId;
    public final long startTimeMs;
    public final long endTimeMs;
    public final int barIndex;
    public final int beatIndex;
    public final int subdivision;
    public final List<EventCandidate> eventCandidates;
    /** What was perceived: model and rule observations, in words, newest last. */
    public final List<String> observations;
    public final double noveltyScore;
    public final double overallConfidence;
    /** The previous moments, oldest first, for "is this the same thing again". */
    public final List<String> recentHistory;

    public MusicMoment(String momentId, long startTimeMs, long endTimeMs, int barIndex,
                       int beatIndex, int subdivision, List<EventCandidate> eventCandidates,
                       List<String> observations, double noveltyScore,
                       double overallConfidence, List<String> recentHistory) {
        this.momentId = momentId == null ? "" : momentId;
        this.startTimeMs = startTimeMs;
        this.endTimeMs = endTimeMs;
        this.barIndex = barIndex;
        this.beatIndex = beatIndex;
        this.subdivision = subdivision;
        this.eventCandidates = eventCandidates == null
                ? Collections.emptyList() : new ArrayList<>(eventCandidates);
        this.observations = observations == null
                ? Collections.emptyList() : new ArrayList<>(observations);
        this.noveltyScore = Math.max(0, Math.min(1, noveltyScore));
        this.overallConfidence = Math.max(0, Math.min(1, overallConfidence));
        this.recentHistory = recentHistory == null
                ? Collections.emptyList() : new ArrayList<>(recentHistory);
    }

    /** The strongest candidate, or null when there is nothing to talk about. */
    public EventCandidate top() {
        EventCandidate best = null;
        for (EventCandidate candidate : eventCandidates) {
            if (candidate.isNone()) continue;
            if (best == null || candidate.confidence > best.confidence) best = candidate;
        }
        return best;
    }

    /** The next strongest, which is what makes a moment ambiguous. */
    public EventCandidate second() {
        EventCandidate best = top();
        EventCandidate next = null;
        for (EventCandidate candidate : eventCandidates) {
            if (candidate.isNone() || candidate == best) continue;
            if (next == null || candidate.confidence > next.confidence) next = candidate;
        }
        return next;
    }
}
