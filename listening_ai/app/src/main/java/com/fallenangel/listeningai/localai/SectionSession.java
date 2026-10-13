package com.fallenangel.listeningai.localai;

import java.util.Collections;
import java.util.List;

/** Applies transport state before PCM. Pause is a context gap, not a musical section boundary. */
public final class SectionSession implements AutoCloseable {
    private final MusicSectionEngine engine = new MusicSectionEngine();
    private long samples, lastSeek;
    private String lastTrack = "";
    private boolean initialized, lastAvailable, lastActive;

    public List<SectionObservation> accept(short[] pcm, int offset, int count,
                                           boolean available, String track, long seek, boolean playing) {
        PcmFeatureExtractor.checkRange(pcm, offset, count);
        if (count == 0) return Collections.emptyList();
        String key = track == null ? "" : track;
        boolean active = !available || playing;
        if (!initialized || available != lastAvailable
                || (available && (!key.equals(lastTrack) || seek != lastSeek))) {
            engine.reset(samples);
        } else if (active != lastActive) {
            engine.resumeAt(samples);
        }
        initialized = true;
        lastAvailable = available;
        lastActive = active;
        lastTrack = key;
        lastSeek = seek;
        if (active) engine.pushPcm(pcm, offset, count);
        samples += count;
        return engine.drainSections();
    }

    public long captureSamples() { return samples; }
    @Override public void close() { engine.close(); }
}
