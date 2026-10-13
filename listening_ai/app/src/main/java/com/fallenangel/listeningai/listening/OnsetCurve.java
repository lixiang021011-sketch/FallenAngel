package com.fallenangel.listeningai.listening;

/**
 * The song's onset curve so far, one number per frame, kept for the whole song.

 * The point of keeping it is the tempo. Twenty seconds of tapping can pin a tempo
 * to about half a percent, and half a percent over three minutes is most of a
 * beat - which is exactly the drift. Those same taps are worth much more as a
 * *phase*: where bar one is, to within a hundred milliseconds or so. So the tempo
 * has to come from the song, which means the onset strength the tracker measures
 * every frame has to be written down instead of used and dropped.

 * At ten milliseconds a frame a four-minute song costs under a hundred kilobytes,
 * which is nothing next to the audio buffer it is derived from.
 */
public final class OnsetCurve {
    private static final int GROWTH = 8192;

    private final double frameSeconds;
    private float[] values = new float[GROWTH];
    private int size;

    public OnsetCurve(double frameSeconds) {
        this.frameSeconds = frameSeconds > 0 ? frameSeconds : 0.01;
    }

    /** One frame of onset strength. Negative values are not a thing. */
    public void push(double value) {
        if (size == values.length) {
            float[] bigger = new float[values.length + GROWTH];
            System.arraycopy(values, 0, bigger, 0, values.length);
            values = bigger;
        }
        values[size++] = (float) Math.max(0, value);
    }

    public int size() {
        return size;
    }

    public boolean isEmpty() {
        return size == 0;
    }

    public double at(int index) {
        return index >= 0 && index < size ? values[index] : 0;
    }

    public double frameSeconds() {
        return frameSeconds;
    }

    /** How much song the curve covers. */
    public double seconds() {
        return size * frameSeconds;
    }

    /** The mean of the curve, which is what a grid's score is read against. */
    public double mean() {
        if (size == 0) return 0;
        double total = 0;
        for (int i = 0; i < size; i++) total += values[i];
        return total / size;
    }

    /** A copy trimmed to what was pushed, for the fit and for tests. */
    public float[] toArray() {
        float[] out = new float[size];
        System.arraycopy(values, 0, out, 0, size);
        return out;
    }

    public void clear() {
        size = 0;
    }

    @Override
    public String toString() {
        return String.format(java.util.Locale.US, "%d frames, %.1f s", size, seconds());
    }
}
