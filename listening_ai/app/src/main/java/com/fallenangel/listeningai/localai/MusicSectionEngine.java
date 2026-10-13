package com.fallenangel.listeningai.localai;

import java.util.List;

/** Worker-owned local section analysis; independent of beat generation and question generation. */
public final class MusicSectionEngine implements AutoCloseable {
    private final SectionFeatureExtractor extractor = new SectionFeatureExtractor();
    private final SectionTracker tracker = new SectionTracker();
    private boolean closed;

    public MusicSectionEngine() { tracker.reset(); }

    public void pushPcm(short[] pcm, int offset, int count) {
        requireOpen();
        extractor.push(pcm, offset, count, tracker::accept);
    }

    /** New track or seek: clear every learned family and queued boundary. */
    public void reset(long firstSample) {
        requireOpen();
        extractor.reset(firstSample);
        tracker.reset();
    }

    /** Pause/resume: preserve family memory, discard comparisons across the audio gap. */
    public void resumeAt(long firstSample) {
        requireOpen();
        extractor.reset(firstSample);
        tracker.breakContext();
    }

    public List<SectionObservation> drainSections() { requireOpen(); return tracker.drain(); }
    public long droppedSections() { return tracker.dropped(); }
    @Override public void close() { if (!closed) { tracker.reset(); closed = true; } }
    private void requireOpen() { if (closed) throw new IllegalStateException("section engine is closed"); }
}
