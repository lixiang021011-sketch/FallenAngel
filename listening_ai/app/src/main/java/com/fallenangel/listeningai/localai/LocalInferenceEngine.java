package com.fallenangel.listeningai.localai;

import java.util.List;

/** Single-owner streaming API, independent of Android, UI, playback globals and BeatDetector. */
public interface LocalInferenceEngine extends AutoCloseable {
    int SAMPLE_RATE = 44100;

    /** Mono signed PCM16 at SAMPLE_RATE. Arbitrary chunk sizes; input is not retained. */
    void pushPcm(short[] pcm, int offset, int count);

    /** Clears windows, model history and pending output; next PCM starts at this capture sample. */
    void reset(long firstSample);

    /** Transfers pending observations to the caller, in sample order. */
    List<MusicObservation> drainObservations();

    /** Lifetime overflow count. Oldest observations are dropped if the consumer falls behind. */
    long droppedObservations();

    @Override void close();
}
