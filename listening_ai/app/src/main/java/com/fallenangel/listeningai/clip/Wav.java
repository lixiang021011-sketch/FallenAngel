package com.fallenangel.listeningai.clip;

import java.io.ByteArrayOutputStream;

/**
 * A clip as a WAV file, so it can be sent somewhere.
 *
 * A WAV header is forty-four bytes and no codec, which makes it the one format
 * every audio-capable model accepts without a decoder on this side. The clip is
 * downsampled first: a model listening to a drum pattern does not need 44.1 kHz,
 * and halving the sample rate quarters the size of the request.
 */
public final class Wav {
    private Wav() {
    }

    /** 16-bit mono PCM at the given rate, with a canonical header. */
    public static byte[] of(byte[] pcm16, int sampleRate) {
        ByteArrayOutputStream out = new ByteArrayOutputStream(pcm16.length + 44);
        ascii(out, "RIFF");
        u32(out, 36 + pcm16.length);
        ascii(out, "WAVE");
        ascii(out, "fmt ");
        u32(out, 16);              // PCM header size
        u16(out, 1);               // PCM
        u16(out, 1);               // mono
        u32(out, sampleRate);
        u32(out, sampleRate * 2);  // bytes per second
        u16(out, 2);               // bytes per frame
        u16(out, 16);              // bits per sample
        ascii(out, "data");
        u32(out, pcm16.length);
        out.write(pcm16, 0, pcm16.length);
        return out.toByteArray();
    }

    /**
     * Little-endian PCM16 at the destination rate.
     *
     * Through the same resampler the models use: a clip sent to a model that
     * has been folded down by taking every third sample carries a mirror of the
     * cymbals at a frequency that has nothing to do with the music.
     */
    public static byte[] resample(short[] pcm, int offset, int count, int sourceRate, int targetRate) {
        float[] mono = Resampler.to(pcm, offset, count, sourceRate, targetRate);
        byte[] out = new byte[mono.length * 2];
        for (int i = 0; i < mono.length; i++) {
            int value = Math.max(-32768, Math.min(32767, Math.round(mono[i] * 32768f)));
            out[2 * i] = (byte) value;
            out[2 * i + 1] = (byte) (value >> 8);
        }
        return out;
    }

    private static void ascii(ByteArrayOutputStream out, String text) {
        for (int i = 0; i < text.length(); i++) out.write(text.charAt(i));
    }

    private static void u16(ByteArrayOutputStream out, int value) {
        out.write(value & 0xFF);
        out.write((value >> 8) & 0xFF);
    }

    private static void u32(ByteArrayOutputStream out, int value) {
        out.write(value & 0xFF);
        out.write((value >> 8) & 0xFF);
        out.write((value >> 16) & 0xFF);
        out.write((value >> 24) & 0xFF);
    }
}
