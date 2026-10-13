package com.fallenangel.listeningai.listening.query;

/**
 * One thing that might have happened in a moment, and how sure anything is.
 *
 * `source` says who is claiming it - today the local rules, later a model - so
 * that a future opinion can be added beside the existing ones rather than
 * replacing them. Which of them a person was answering is part of the data.
 */
public final class EventCandidate {
    public final String type;
    public final double confidence;
    public final String source;
    /** What was measured, in words, so a claim can be checked later. */
    public final String evidence;

    public EventCandidate(String type, double confidence, String source, String evidence) {
        this.type = type == null ? "" : type;
        this.confidence = Math.max(0, Math.min(1, confidence));
        this.source = source == null ? "" : source;
        this.evidence = evidence == null ? "" : evidence;
    }

    /** The existing rules spell "none of these" as a candidate with this type. */
    public boolean isNone() {
        return "none".equals(type) || type.isEmpty();
    }

    @Override
    public String toString() {
        return type + " " + String.format(java.util.Locale.US, "%.2f", confidence);
    }
}
