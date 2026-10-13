package com.fallenangel.listeningai.localai;

/** A delayed, evidence-based acoustic section boundary; never a verse/chorus claim.
 * Times use the capture clock. streamId changes on track/seek, not on pause.
 */
public final class SectionObservation {
    public enum Kind { START, CHANGE, RETURN }
    public final long streamId, boundarySample, detectedAtSample;
    /** One-based section instance; equal family labels do not collapse distinct boundaries. */
    public final int sectionIndex;
    public final String label, previousLabel;
    public final Kind kind;
    /** Heuristic novelty strength and similarity, not calibrated probabilities. */
    public final double confidence, novelty, similarity;

    SectionObservation(long streamId, int sectionIndex, long boundarySample, long detectedAtSample,
                       String label, String previousLabel, Kind kind,
                       double confidence, double novelty, double similarity) {
        this.streamId = streamId;
        this.sectionIndex = sectionIndex;
        this.boundarySample = boundarySample;
        this.detectedAtSample = detectedAtSample;
        this.label = label;
        this.previousLabel = previousLabel;
        this.kind = kind;
        this.confidence = confidence;
        this.novelty = novelty;
        this.similarity = similarity;
    }

    public double boundarySeconds() { return boundarySample / 44100.0; }
    public double detectedAtSeconds() { return detectedAtSample / 44100.0; }
}
