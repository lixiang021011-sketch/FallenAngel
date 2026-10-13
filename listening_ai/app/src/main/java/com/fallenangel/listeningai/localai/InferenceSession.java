package com.fallenangel.listeningai.localai;

import java.util.Collections;
import java.util.List;
import java.util.Objects;

/** Capture-worker-owned bridge. Applies transport changes BEFORE analysing the next PCM block.
 * Capture time advances through pauses; segmentId identifies discontinuities. Song time is not
 * inferred from stale media positions. Unknown transport falls back to analysing captured audio.
 */
public final class InferenceSession implements AutoCloseable {
    private final LocalInferenceEngine engine;
    private long captureSamples, lastSeek;
    private String lastTrack = "";
    private boolean initialized, lastAvailable, lastPlaying;

    public InferenceSession(LocalInferenceEngine engine) { this.engine = Objects.requireNonNull(engine); }

    public List<MusicObservation> accept(short[] pcm, int offset, int count,
                                          boolean available, String track, long seek, boolean playing) {
        PcmFeatureExtractor.checkRange(pcm, offset, count);
        if (count == 0) return Collections.emptyList();
        String key = track == null ? "" : track;
        boolean active = !available || playing;
        if (!initialized || available != lastAvailable || active != lastPlaying
                || (available && (!key.equals(lastTrack) || seek != lastSeek))) {
            engine.reset(captureSamples);
        }
        initialized = true;
        lastAvailable = available;
        lastPlaying = active;
        lastTrack = key;
        lastSeek = seek;
        if (active) engine.pushPcm(pcm, offset, count);
        captureSamples += count;
        return engine.drainObservations();
    }

    public long captureSamples() { return captureSamples; }
    @Override public void close() { engine.close(); }
}
