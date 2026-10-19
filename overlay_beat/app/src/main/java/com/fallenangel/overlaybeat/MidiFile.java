package com.fallenangel.overlaybeat;

import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * A Standard MIDI File, read into the raw facts it contains.
 *
 * Hand-written for the same reason the JSON reader is: this project takes no
 * dependencies and needs only a small, well-defined part of the format.
 *
 * This layer stops at "file -> data". It resolves ticks into seconds, because a
 * tick means nothing without the tempo map, but it makes no musical decision at
 * all: what becomes a note for the player, how fast is too fast, which lane -
 * that is {@link MidiChart}'s job, and nothing here knows about the game.
 *
 * Supports format 0 / 1 / 2, variable-length delta times, running status,
 * note-on/note-off pairing, and the meta events that matter here (track name
 * 0x03, tempo 0x51, time signature 0x58, end of track 0x2F). SMPTE division is
 * rejected rather than guessed at.
 */
public final class MidiFile {

    /** The file is not a Standard MIDI File, or is one this reader cannot trust. */
    public static final class MidiParseException extends Exception {
        public MidiParseException(String message) {
            super(message);
        }
    }

    /** One note-on with its matching note-off, in raw ticks. */
    public static final class RawNote {
        public final int track;
        public final int channel;
        public final int pitch;
        /** The note-on velocity, 1-127. */
        public final int velocity;
        public final int program;
        public final long startTick;
        public final long endTick;
        public final String trackName;

        RawNote(int track, int channel, int pitch, int velocity, int program,
                long startTick, long endTick, String trackName) {
            this.track = track;
            this.channel = channel;
            this.pitch = pitch;
            this.velocity = velocity;
            this.program = program;
            this.startTick = startTick;
            this.endTick = endTick;
            this.trackName = trackName;
        }

        public long durationTicks() {
            return Math.max(0L, endTick - startTick);
        }
    }

    /** A tempo change. A file that states none is played at 120 bpm. */
    public static final class Tempo {
        public final long tick;
        public final double microsPerQuarter;
        public final double bpm;

        Tempo(long tick, double microsPerQuarter) {
            this.tick = tick;
            this.microsPerQuarter = microsPerQuarter;
            this.bpm = 60_000_000.0 / microsPerQuarter;
        }
    }

    public static final class TimeSignature {
        public final long tick;
        public final int numerator;
        public final int denominator;

        TimeSignature(long tick, int numerator, int denominator) {
            this.tick = tick;
            this.numerator = numerator;
            this.denominator = denominator;
        }
    }

    public final int format;
    public final int ticksPerQuarter;
    public final List<RawNote> notes;
    /** Only the tempo changes the file states, in tick order. */
    public final List<Tempo> tempos;
    public final List<TimeSignature> timeSignatures;
    /** Track names in track order; empty string where a track states none. */
    public final List<String> trackNames;
    /** Notes still sounding when their track ended; they are kept, zero-length. */
    public final int truncatedNotes;

    private final long[] tempoTicks;
    private final double[] tempoMicros;

    private MidiFile(int format, int ticksPerQuarter, List<RawNote> notes, List<Tempo> tempos,
                     List<TimeSignature> timeSignatures, List<String> trackNames,
                     int truncatedNotes) {
        this.format = format;
        this.ticksPerQuarter = ticksPerQuarter;
        this.notes = notes;
        this.tempos = tempos;
        this.timeSignatures = timeSignatures;
        this.trackNames = trackNames;
        this.truncatedNotes = truncatedNotes;

        // The tempo map always has to start somewhere, and 120 bpm is what a
        // file that never states a tempo is played at.
        List<Tempo> resolved = new ArrayList<>();
        if (tempos.isEmpty() || tempos.get(0).tick > 0) {
            resolved.add(new Tempo(0, 500_000));
        }
        resolved.addAll(tempos);
        tempoTicks = new long[resolved.size()];
        tempoMicros = new double[resolved.size()];
        for (int i = 0; i < resolved.size(); i++) {
            tempoTicks[i] = resolved.get(i).tick;
            tempoMicros[i] = resolved.get(i).microsPerQuarter;
        }
    }

    public static MidiFile parse(File file) throws IOException, MidiParseException {
        return parse(Files.readAllBytes(file.toPath()));
    }

    public static MidiFile parse(byte[] data) throws MidiParseException {
        Reader in = new Reader(data);
        if (!"MThd".equals(in.ascii(4))) {
            throw new MidiParseException("not a MIDI file: the first four bytes are not MThd");
        }
        long headerLength = in.u32();
        if (headerLength < 6) {
            throw new MidiParseException("header chunk is " + headerLength + " bytes, at least 6 are required");
        }
        int format = (int) in.u16();
        in.u16(); // declared track count; the chunks themselves are the truth
        int division = (int) in.u16();
        if ((division & 0x8000) != 0) {
            throw new MidiParseException("SMPTE time division is not supported");
        }
        if (division == 0) {
            throw new MidiParseException("time division is zero");
        }
        in.skip(headerLength - 6);

        List<RawNote> notes = new ArrayList<>();
        List<Tempo> tempos = new ArrayList<>();
        List<TimeSignature> signatures = new ArrayList<>();
        List<String> names = new ArrayList<>();
        int truncated = 0;

        while (in.remaining() >= 8) {
            String id = in.ascii(4);
            long length = in.u32();
            if (!"MTrk".equals(id)) {
                in.skip(length);
                continue;
            }
            ParsedTrack track = readTrack(in.slice(length), names.size());
            notes.addAll(track.notes);
            tempos.addAll(track.tempos);
            signatures.addAll(track.signatures);
            names.add(track.name);
            truncated += track.truncated;
        }

        // Format 2 gives every track its own sequence and so its own tempo map;
        // merging them is not strictly right, but a file like that is a kit of
        // separate pieces rather than one song, and merging keeps the rest of
        // this reader honest about what it did.
        tempos.sort((a, b) -> Long.compare(a.tick, b.tick));
        signatures.sort((a, b) -> Long.compare(a.tick, b.tick));

        return new MidiFile(format, division, notes, tempos, signatures, names, truncated);
    }

    /** Seconds from the start of the file, following the tempo map. */
    public double secondsAt(long tick) {
        double seconds = 0;
        long anchor = 0;
        double micros = tempoMicros[0];
        for (int i = 0; i < tempoTicks.length; i++) {
            if (tempoTicks[i] > tick) break;
            seconds += (tempoTicks[i] - anchor) * micros / 1e6 / ticksPerQuarter;
            anchor = tempoTicks[i];
            micros = tempoMicros[i];
        }
        if (tick > anchor) {
            seconds += (tick - anchor) * micros / 1e6 / ticksPerQuarter;
        }
        return seconds;
    }

    /** The tempo in force at a tick, in bpm. */
    public double bpmAt(long tick) {
        double bpm = 60_000_000.0 / tempoMicros[0];
        for (int i = 0; i < tempoTicks.length; i++) {
            if (tempoTicks[i] > tick) break;
            bpm = 60_000_000.0 / tempoMicros[i];
        }
        return bpm;
    }

    /** How long the file plays for, at the last note-off. */
    public double durationSeconds() {
        long last = 0;
        for (RawNote note : notes) {
            if (note.endTick > last) last = note.endTick;
        }
        return secondsAt(last);
    }

    // ---------------------------------------------------------------- reading

    private static final class ParsedTrack {
        final List<RawNote> notes = new ArrayList<>();
        final List<Tempo> tempos = new ArrayList<>();
        final List<TimeSignature> signatures = new ArrayList<>();
        String name = "";
        int truncated;
    }

    /** A note-on waiting for its note-off. */
    private static final class OpenNote {
        final long startTick;
        final int velocity;
        final int program;

        OpenNote(long startTick, int velocity, int program) {
            this.startTick = startTick;
            this.velocity = velocity;
            this.program = program;
        }
    }

    /** A note-off, held until the track's name is known so the note can carry it. */
    private static final class ClosedNote {
        final int channel;
        final int pitch;
        final int velocity;
        final int program;
        final long startTick;
        final long endTick;

        ClosedNote(int channel, int pitch, int velocity, int program, long startTick, long endTick) {
            this.channel = channel;
            this.pitch = pitch;
            this.velocity = velocity;
            this.program = program;
            this.startTick = startTick;
            this.endTick = endTick;
        }
    }

    private static ParsedTrack readTrack(Reader in, int trackIndex) throws MidiParseException {
        ParsedTrack out = new ParsedTrack();
        List<ClosedNote> closed = new ArrayList<>();
        Map<Long, ArrayDeque<OpenNote>> open = new HashMap<>();
        Map<Integer, Integer> programs = new HashMap<>();

        long tick = 0;
        int runningStatus = 0;

        while (in.remaining() > 0) {
            tick += in.vlq();
            int status = in.peek();

            if (status == 0xFF) {
                in.u8();
                int type = in.u8();
                long length = in.vlq();
                if (type == 0x2F) { // end of track
                    break;
                } else if (type == 0x03 && length > 0) { // track name
                    byte[] text = in.bytes((int) length);
                    if (out.name.isEmpty()) {
                        out.name = new String(text, StandardCharsets.UTF_8).trim();
                    }
                } else if (type == 0x51 && length == 3) {
                    out.tempos.add(new Tempo(tick, in.u24()));
                } else if (type == 0x58 && length == 4) {
                    int numerator = in.u8();
                    int denominator = 1 << in.u8();
                    in.u8();
                    in.u8();
                    out.signatures.add(new TimeSignature(tick, numerator, denominator));
                } else {
                    in.skip(length);
                }
                continue;
            }

            if (status == 0xF0 || status == 0xF7) { // system exclusive
                in.u8();
                in.skip(in.vlq());
                continue;
            }

            if (status >= 0x80) {
                runningStatus = in.u8();
            } else if (runningStatus == 0) {
                throw new MidiParseException("running status byte before any status byte");
            }

            int command = runningStatus & 0xF0;
            int channel = runningStatus & 0x0F;
            switch (command) {
                case 0x80:
                case 0x90: {
                    int pitch = in.data();
                    int velocity = in.data();
                    boolean on = command == 0x90 && velocity > 0;
                    // A note-on with velocity 0 is a note-off; files use either.
                    long key = channel * 128L + pitch;
                    ArrayDeque<OpenNote> stack = open.get(key);
                    if (on) {
                        if (stack == null) {
                            stack = new ArrayDeque<>();
                            open.put(key, stack);
                        }
                        stack.push(new OpenNote(tick, velocity, programs.getOrDefault(channel, -1)));
                    } else if (stack != null && !stack.isEmpty()) {
                        // LIFO: a repeated note closes the one that opened last.
                        OpenNote started = stack.pop();
                        closed.add(new ClosedNote(channel, pitch, started.velocity, started.program,
                                started.startTick, tick));
                    }
                    break;
                }
                case 0xA0: // polyphonic aftertouch
                case 0xB0: // control change
                case 0xE0: // pitch bend
                    in.data();
                    in.data();
                    break;
                case 0xC0: // program change
                    programs.put(channel, in.data());
                    break;
                case 0xD0: // channel aftertouch
                    in.data();
                    break;
                default:
                    // 0xF1-0xFE inside a track: system messages this reader has
                    // no note meaning for. Their length is not safe to guess, so
                    // the track ends here rather than being read as garbage.
                    out.truncated += closeRemaining(closed, open, tick);
                    out.name = finish(out, closed, trackIndex);
                    return out;
            }
        }

        out.truncated += closeRemaining(closed, open, tick);
        out.name = finish(out, closed, trackIndex);
        return out;
    }

    /** Notes left open at the end of a track: kept, counted, zero length. */
    private static int closeRemaining(List<ClosedNote> closed, Map<Long, ArrayDeque<OpenNote>> open,
                                      long tick) {
        int count = 0;
        for (Map.Entry<Long, ArrayDeque<OpenNote>> entry : open.entrySet()) {
            int channel = (int) (entry.getKey() / 128);
            int pitch = (int) (entry.getKey() % 128);
            while (!entry.getValue().isEmpty()) {
                OpenNote started = entry.getValue().pop();
                closed.add(new ClosedNote(channel, pitch, started.velocity, started.program,
                        started.startTick, tick));
                count++;
            }
        }
        open.clear();
        return count;
    }

    /** Now that the track name is known, put the notes in their final shape. */
    private static String finish(ParsedTrack track, List<ClosedNote> closed, int trackIndex) {
        closed.sort((a, b) -> Long.compare(a.startTick, b.startTick));
        for (ClosedNote note : closed) {
            track.notes.add(new RawNote(trackIndex, note.channel, note.pitch, note.velocity,
                    note.program, note.startTick, note.endTick, track.name));
        }
        closed.clear();
        return track.name;
    }

    /** A cursor over the bytes, with the track boundary as its end. */
    private static final class Reader {
        private final byte[] data;
        private int pos;
        private final int end;

        Reader(byte[] data) {
            this(data, 0, data.length);
        }

        private Reader(byte[] data, int start, int end) {
            this.data = data;
            this.pos = start;
            this.end = end;
        }

        int remaining() {
            return end - pos;
        }

        int peek() throws MidiParseException {
            if (pos >= end) throw new MidiParseException("the file ended in the middle of an event");
            return data[pos] & 0xFF;
        }

        int u8() throws MidiParseException {
            int value = peek();
            pos++;
            return value;
        }

        /** A data byte: its high bit must be clear. */
        int data() throws MidiParseException {
            int value = u8();
            if (value >= 0x80) {
                throw new MidiParseException("expected a data byte, found 0x" + Integer.toHexString(value));
            }
            return value;
        }

        long u16() throws MidiParseException {
            return (u8() << 8) | u8();
        }

        long u24() throws MidiParseException {
            return (u8() << 16) | (u8() << 8) | u8();
        }

        long u32() throws MidiParseException {
            return (u16() << 16) | u16();
        }

        long vlq() throws MidiParseException {
            long value = 0;
            for (int i = 0; i < 4; i++) {
                int b = u8();
                value = (value << 7) | (b & 0x7F);
                if ((b & 0x80) == 0) return value;
            }
            throw new MidiParseException("variable-length quantity longer than four bytes");
        }

        String ascii(int length) throws MidiParseException {
            if (length > remaining()) {
                throw new MidiParseException("the file ended in the middle of a chunk header");
            }
            StringBuilder text = new StringBuilder(length);
            for (int i = 0; i < length; i++) {
                text.append((char) u8());
            }
            return text.toString();
        }

        byte[] bytes(int length) throws MidiParseException {
            if (length > remaining()) {
                throw new MidiParseException("an event runs past the end of its track");
            }
            byte[] out = new byte[length];
            System.arraycopy(data, pos, out, 0, length);
            pos += length;
            return out;
        }

        void skip(long length) throws MidiParseException {
            if (length < 0 || length > remaining()) {
                throw new MidiParseException("an event runs past the end of its track");
            }
            pos += (int) length;
        }

        Reader slice(long length) throws MidiParseException {
            if (length < 0 || length > remaining()) {
                throw new MidiParseException("a chunk runs past the end of the file");
            }
            Reader sub = new Reader(data, pos, pos + (int) length);
            pos += (int) length;
            return sub;
        }
    }
}
