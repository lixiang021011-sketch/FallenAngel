package com.fallenangel.listeningai.listening;

/**
 * The last few seconds of what was heard, kept so a pause can be looked at.
 *
 * The listener pauses *after* the moment they noticed, so by the time the pause
 * is known the audio that caused it is already past. This is the only reason the
 * buffer exists: to still have those seconds when the system finds out it needs
 * them. Only audio that was playing is written, so the buffer's timeline is the
 * music's rather than the wall clock's, and a pause does not fill it with
 * silence.
 */
public final class RecentAudio {
    private final short[] buffer;
    private long endSample = -1;
    private int filled;
    private int writeAt;

    public RecentAudio(double seconds) {
        buffer = new short[(int) (Math.max(1, seconds) * 44100)];
    }

    /**
     * @param endSample capture-clock sample of the last sample in this block,
     *                  which is what the rest of the system anchors times on
     */
    public void write(short[] pcm, int offset, int count, long endSample) {
        if (count <= 0) return;
        for (int i = offset; i < offset + count; i++) {
            buffer[writeAt] = pcm[i];
            writeAt = (writeAt + 1) % buffer.length;
        }
        filled = Math.min(buffer.length, filled + count);
        this.endSample = endSample;
    }

    public void clear() {
        filled = 0;
        writeAt = 0;
        endSample = -1;
    }

    /** How much is in here, in seconds. */
    public double availableSeconds() {
        return filled / 44100.0;
    }

    public static final class Window {
        public final short[] pcm;
        /** Capture sample of pcm[0]. */
        public final long startSample;

        Window(short[] pcm, long startSample) {
            this.pcm = pcm;
            this.startSample = startSample;
        }

        public long endSample() {
            return startSample + pcm.length;
        }
    }

    /**
     * The last {@code seconds} of audio, oldest first, or null when there is not
     * that much yet - a question about two seconds of audio is not a question
     * about the moment somebody stopped on.
     */
    public Window tail(double seconds) {
        int wanted = (int) (seconds * 44100);
        if (filled < wanted || endSample < 0) return null;
        short[] out = new short[wanted];
        int start = Math.floorMod(writeAt - wanted, buffer.length);
        for (int i = 0; i < wanted; i++) {
            out[i] = buffer[(start + i) % buffer.length];
        }
        return new Window(out, endSample - wanted);
    }
}
