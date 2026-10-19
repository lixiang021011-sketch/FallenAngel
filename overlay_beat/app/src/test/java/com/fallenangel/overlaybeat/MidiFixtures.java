package com.fallenangel.overlaybeat;

import java.io.ByteArrayOutputStream;
import java.nio.charset.StandardCharsets;

/**
 * A Standard MIDI File built by hand, so a test can say what it means.
 *
 * The tests here are about the reader, and a reader is only worth as much as
 * the files it is shown. Writing the bytes in the test keeps the fixture visible
 * (what delta, what velocity, what tempo) instead of hiding it in a binary in
 * the repository that nobody can read or change.
 */
final class MidiFixtures {

    private MidiFixtures() {
    }

    static final class Track {
        private final ByteArrayOutputStream out = new ByteArrayOutputStream();

        Track delta(long ticks) {
            writeVlq(out, ticks);
            return this;
        }

        Track noteOn(int channel, int pitch, int velocity) {
            out.write(0x90 | channel);
            out.write(pitch);
            out.write(velocity);
            return this;
        }

        Track noteOff(int channel, int pitch) {
            out.write(0x80 | channel);
            out.write(pitch);
            out.write(0);
            return this;
        }

        Track program(int channel, int program) {
            out.write(0xC0 | channel);
            out.write(program);
            return this;
        }

        Track name(String text) {
            return meta(0x03, text.getBytes(StandardCharsets.UTF_8));
        }

        Track tempo(long microsPerQuarter) {
            return meta(0x51, new byte[] {
                    (byte) (microsPerQuarter >> 16),
                    (byte) (microsPerQuarter >> 8),
                    (byte) microsPerQuarter});
        }

        /** Data bytes with no status byte of their own, for running status. */
        Track raw(int... bytes) {
            for (int b : bytes) out.write(b);
            return this;
        }

        Track end() {
            return meta(0x2F, new byte[0]);
        }

        byte[] bytes() {
            return out.toByteArray();
        }

        private Track meta(int type, byte[] data) {
            out.write(0xFF);
            out.write(type);
            writeVlq(out, data.length);
            out.write(data, 0, data.length);
            return this;
        }
    }

    static byte[] smf(int format, int ticksPerQuarter, byte[]... tracks) {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        ascii(out, "MThd");
        u32(out, 6);
        u16(out, format);
        u16(out, tracks.length);
        u16(out, ticksPerQuarter);
        for (byte[] track : tracks) {
            ascii(out, "MTrk");
            u32(out, track.length);
            out.write(track, 0, track.length);
        }
        return out.toByteArray();
    }

    private static void writeVlq(ByteArrayOutputStream out, long value) {
        int shift = 21;
        while (shift > 0 && ((value >> shift) & 0x7F) == 0) shift -= 7;
        for (; shift > 0; shift -= 7) {
            out.write((int) (((value >> shift) & 0x7F) | 0x80));
        }
        out.write((int) (value & 0x7F));
    }

    private static void ascii(ByteArrayOutputStream out, String text) {
        for (int i = 0; i < text.length(); i++) out.write(text.charAt(i));
    }

    private static void u16(ByteArrayOutputStream out, int value) {
        out.write((value >> 8) & 0xFF);
        out.write(value & 0xFF);
    }

    private static void u32(ByteArrayOutputStream out, int value) {
        u16(out, value >> 16);
        u16(out, value & 0xFFFF);
    }
}
