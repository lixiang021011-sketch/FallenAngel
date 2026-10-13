package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;

import org.junit.Test;

/**
 * The buffer that makes looking back possible, checked on the one thing that
 * matters about a ring: that what comes out is the last N seconds *in order*,
 * including when it has wrapped.
 */
public class RecentAudioTest {
    private static final int RATE = 44100;

    /** A ramp, so every sample says where in the stream it came from. */
    private static short[] block(int from, int count) {
        short[] out = new short[count];
        for (int i = 0; i < count; i++) out[i] = (short) ((from + i) % 30000);
        return out;
    }

    @Test
    public void nothingComesBackUntilThereIsThatMuch() {
        RecentAudio audio = new RecentAudio(8);
        audio.write(block(0, RATE), 0, RATE, RATE);
        assertNull("one second is not eight", audio.tail(8));
        assertNotNull(audio.tail(0.5));
        assertEquals(1.0, audio.availableSeconds(), 1e-9);
    }

    @Test
    public void theTailIsTheLastSecondsInOrder() {
        RecentAudio audio = new RecentAudio(8);
        for (int second = 0; second < 8; second++) {
            audio.write(block(second * RATE, RATE), 0, RATE, (second + 1L) * RATE);
        }
        RecentAudio.Window window = audio.tail(3);
        assertNotNull(window);
        assertEquals(3 * RATE, window.pcm.length);
        assertEquals("the window starts where the audio does", 5L * RATE, window.startSample);
        assertEquals(block(5 * RATE, 1)[0], window.pcm[0]);
        assertEquals(block(8 * RATE - 1, 1)[0], window.pcm[window.pcm.length - 1]);
    }

    @Test
    public void wrappingKeepsTheOrderAndTheClock() {
        RecentAudio audio = new RecentAudio(4);
        for (int second = 0; second < 10; second++) {
            audio.write(block(second * RATE, RATE), 0, RATE, (second + 1L) * RATE);
        }
        RecentAudio.Window window = audio.tail(4);
        assertNotNull(window);
        assertEquals("ten seconds written, four kept, so it starts at second six",
                6L * RATE, window.startSample);
        for (int i = 0; i < window.pcm.length; i++) {
            if (i % RATE == 0) {
                assertEquals("second " + (i / RATE) + " of the window",
                        block((int) (6L * RATE) + i, 1)[0], window.pcm[i]);
            }
        }
        assertEquals(10L * RATE, window.endSample());
    }

    @Test
    public void clearingForgetsTheSong() {
        RecentAudio audio = new RecentAudio(8);
        audio.write(block(0, 8 * RATE), 0, 8 * RATE, 8L * RATE);
        assertNotNull(audio.tail(8));
        audio.clear();
        assertNull(audio.tail(1));
        assertEquals(0.0, audio.availableSeconds(), 1e-9);
    }
}
