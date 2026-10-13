package com.fallenangel.listeningai.listening;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

/**
 * One interaction with the listener, written down so it can be argued with later.
 *
 * The point of this project is not the label, it is the trace: what the system
 * believed, what it offered, what the person actually chose, and where in the
 * music that was. So the event keeps the proposals *and* their evidence, the
 * moment the listener stopped, the moment the system thinks they meant, and the
 * three separate confidences - hearing it at all, putting it in the right place,
 * and understanding it - because "the AI was wrong" means three different things
 * and only one of them is about the music.
 *
 * Nothing here is ever revised. Reanalysis produces a new record.
 */
public final class ListeningEvent {
    public static final String STATE_PROPOSED = "AI_PROPOSED";
    public static final String STATE_CONFIRMED = "USER_CONFIRMED";

    public final String id;
    public final String aiVersion;
    /** When the listener stopped, on the capture clock. */
    public final double interactionSeconds;
    /** What the system thinks they stopped on. */
    public final double targetSeconds;
    public final int bar, beat, subdivision;
    public final String subdivisionName, sectionLabel;
    public final double perceptionConfidence, alignmentConfidence, semanticConfidence;
    public final List<PauseQuestion.Candidate> candidates;
    /** Set when the listener has answered; empty while the question is open. */
    public final String chosenId, chosenLabel;
    public final String state;
    public final long createdAtMillis;

    public ListeningEvent(String id, String aiVersion, double interactionSeconds,
                          double targetSeconds, int bar, int beat, int subdivision,
                          String subdivisionName, String sectionLabel,
                          double perceptionConfidence, double alignmentConfidence,
                          double semanticConfidence, List<PauseQuestion.Candidate> candidates,
                          String chosenId, String chosenLabel, String state,
                          long createdAtMillis) {
        this.id = id;
        this.aiVersion = aiVersion;
        this.interactionSeconds = interactionSeconds;
        this.targetSeconds = targetSeconds;
        this.bar = bar;
        this.beat = beat;
        this.subdivision = subdivision;
        this.subdivisionName = subdivisionName;
        this.sectionLabel = sectionLabel;
        this.perceptionConfidence = perceptionConfidence;
        this.alignmentConfidence = alignmentConfidence;
        this.semanticConfidence = semanticConfidence;
        this.candidates = new ArrayList<>(candidates);
        this.chosenId = chosenId == null ? "" : chosenId;
        this.chosenLabel = chosenLabel == null ? "" : chosenLabel;
        this.state = state;
        this.createdAtMillis = createdAtMillis;
    }

    /** What the system records when it asks: no answer yet, nothing overwritten. */
    public static ListeningEvent proposed(String id, PauseQuestion question,
                                          double interactionSeconds, double alignmentConfidence,
                                          long nowMillis) {
        return proposed(id, question, interactionSeconds, alignmentConfidence, nowMillis,
                PauseQuestion.VERSION);
    }

    /** @param version who proposed this: the local rules, or a named model */
    public static ListeningEvent proposed(String id, PauseQuestion question,
                                          double interactionSeconds, double alignmentConfidence,
                                          long nowMillis, String version) {
        double perception = question.candidates.isEmpty()
                ? 0 : question.candidates.get(0).confidence;
        return new ListeningEvent(id, version, interactionSeconds,
                question.targetSeconds(), question.bar, question.beat, question.subdivision,
                question.subdivisionName(), question.sectionLabel,
                perception, alignmentConfidence, 0, question.candidates, "", "",
                STATE_PROPOSED, nowMillis);
    }

    /** What the system records once the listener has answered. */
    public ListeningEvent answered(PauseQuestion.Candidate choice, long nowMillis) {
        return new ListeningEvent(id, aiVersion, interactionSeconds, targetSeconds,
                bar, beat, subdivision, subdivisionName, sectionLabel,
                perceptionConfidence, alignmentConfidence, choice.confidence, candidates,
                choice.id, choice.label, STATE_CONFIRMED, nowMillis);
    }

    public JSONObject toJson() {
        JSONObject out = new JSONObject();
        try {
            out.put("id", id);
            out.put("aiVersion", aiVersion);
            out.put("state", state);
            out.put("createdAt", createdAtMillis);
            out.put("interactionTime", interactionSeconds);
            out.put("targetMusicTime", targetSeconds);
            out.put("bar", bar);
            out.put("beat", beat);
            out.put("subdivision", subdivision);
            out.put("subdivisionName", subdivisionName);
            out.put("section", sectionLabel);
            JSONObject confidence = new JSONObject();
            confidence.put("perception", perceptionConfidence);
            confidence.put("alignment", alignmentConfidence);
            confidence.put("semantic", semanticConfidence);
            out.put("confidence", confidence);
            JSONArray array = new JSONArray();
            for (PauseQuestion.Candidate candidate : candidates) {
                JSONObject item = new JSONObject();
                item.put("id", candidate.id);
                item.put("label", candidate.label);
                item.put("confidence", candidate.confidence);
                item.put("evidence", candidate.evidence);
                array.put(item);
            }
            out.put("candidates", array);
            out.put("chosenId", chosenId);
            out.put("chosenLabel", chosenLabel);
        } catch (Exception ignored) {
            // JSONObject is a map; the only way this throws is out of memory.
        }
        return out;
    }
}
