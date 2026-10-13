package com.fallenangel.listeningai.listening;

import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStreamWriter;
import java.io.Writer;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Date;
import java.util.Locale;

/**
 * One song's record, in two forms.
 *
 * The machine form is one JSON object per line, appended and never rewritten.
 * The readable form is the same story in sentences, with times counted from the
 * start of the song, because a file whose times start wherever the app was
 * launched cannot be read by anybody but the app.
 */
public final class SongLog {
    private final String label;
    private final File directory;
    private final File events;
    private final File readable;
    private final ListeningLog machine;
    /**
     * The harmony as it was read, second by second, in its own file.
     *
     * Raw, and separate from the questions, because it is a different kind of
     * thing: an event log records decisions, and this records a measurement that
     * arrives whether or not anybody decided anything. Which also means the "bar 17
     * beat 3 is Am" version can be derived from this later - once the beat grid is
     * trusted - instead of the other way round, where the grid would decide what
     * the harmony is allowed to say.
     */
    private final ListeningLog chords;
    private final File chordsFile;
    /**
     * The lines as they were written, so this song can be exported at the end.

     * In memory rather than read back from the file: the export happens while the
     * song's own directory is still open, and a few hundred small objects cost
     * less than parsing the log again and wondering which line was the last.
     */
    private final List<JSONObject> lines = new ArrayList<>();

    public SongLog(File root, String label, long nowMillis) {
        this.label = label == null || label.isEmpty() ? "未知曲目" : label;
        String stamp = new SimpleDateFormat("yyyyMMdd HHmmss", Locale.US)
                .format(new Date(nowMillis));
        this.directory = free(new File(root, stamp + " " + slug(this.label)));
        directory.mkdirs();
        this.events = new File(directory, "events.jsonl");
        this.readable = new File(directory, "readable.log");
        this.machine = new ListeningLog(events);
        this.chordsFile = new File(directory, "chords.jsonl");
        this.chords = new ListeningLog(chordsFile);
        write(readable, "# " + this.label + " · 开始 "
                + new SimpleDateFormat("HH:mm:ss", Locale.US).format(new Date(nowMillis)) + "\n",
                false);
    }

    public File directory() {
        return directory;
    }

    public File events() {
        return events;
    }

    public File readable() {
        return readable;
    }

    public File chords() {
        return chordsFile;
    }

    /** One reading of the harmony, at the second it was taken. */
    public void chord(JSONObject line, double seconds) {
        if (line == null) return;
        try {
            line.put("song", label);
            line.put("songTime", Math.round(seconds * 1000) / 1000.0);
        } catch (Exception ignored) {
        }
        chords.append(line);
    }

    /** A line in the human log: who is talking, and what they said. */
    public void say(String who, String text, double seconds) {
        if (text == null || text.isEmpty()) return;
        write(readable, clock(seconds) + "  [" + who + "] " + text + "\n", true);
    }

    /**
     * A line in the machine log.
     *
     * The line is written as given, with where the listener was in this song and
     * which song it was added to it: the file is per song now, but a line that
     * has been copied out of it should still be able to say what it belongs to.
     */
    public void event(JSONObject line, double seconds) {
        if (line == null) return;
        try {
            line.put("song", label);
            line.put("songTime", Math.round(seconds * 1000) / 1000.0);
        } catch (Exception ignored) {
        }
        machine.append(line);
        lines.add(line);
    }

    public String label() {
        return label;
    }

    /** Everything written down for this song, in the order it happened. */
    public List<JSONObject> lines() {
        return Collections.unmodifiableList(lines);
    }

    /**
     * Write the desktop validator's document beside the log.

     * Named after the song so a researcher dragging a folder onto the validator
     * gets the pair matched by name; the log stays the original either way.
     */
    public File writeMgmcs(JSONObject document) {
        if (document == null) return null;
        File target = new File(directory, slug(label) + ".mgmcs.json");
        write(target, document.toString(), false);
        return target;
    }

    public File mgmcsFile() {
        return new File(directory, slug(label) + ".mgmcs.json");
    }

    /**
     * A time in the song, as a person reads it.
     *
     * Minutes and tenths, with hours only when there are hours: the tenths are
     * what questions are about, and the rest is what tells a person which
     * minute of the song they are looking at.
     */
    public static String clock(double seconds) {
        if (!(seconds >= 0) || Double.isInfinite(seconds)) return "--:--.-";
        long tenths = Math.round(seconds * 10);
        long hours = tenths / 36000;
        long minutes = (tenths / 600) % 60;
        long rest = (tenths / 10) % 60;
        long one = tenths % 10;
        return hours > 0
                ? String.format(Locale.US, "%d:%02d:%02d.%d", hours, minutes, rest, one)
                : String.format(Locale.US, "%02d:%02d.%d", minutes, rest, one);
    }

    /** A song's name as a directory name: letters and digits, one dash between. */
    public static String slug(String text) {
        if (text == null) return "track";
        String cleaned = text.trim().replaceAll("[^\\p{L}\\p{N}]+", "-");
        while (cleaned.startsWith("-")) cleaned = cleaned.substring(1);
        while (cleaned.endsWith("-")) cleaned = cleaned.substring(0, cleaned.length() - 1);
        if (cleaned.isEmpty()) return "track";
        return cleaned.length() > 60 ? cleaned.substring(0, 60) : cleaned;
    }

    /** A directory nothing is using: two songs can share a name and a second. */
    private static File free(File wanted) {
        if (!wanted.exists()) return wanted;
        for (int attempt = 2; attempt < 100; attempt++) {
            File candidate = new File(wanted.getParentFile(), wanted.getName() + " " + attempt);
            if (!candidate.exists()) return candidate;
        }
        return wanted;
    }

    private static void write(File file, String text, boolean append) {
        try (Writer writer = new OutputStreamWriter(
                new FileOutputStream(file, append), StandardCharsets.UTF_8)) {
            writer.write(text);
        } catch (IOException ignored) {
            // Losing a line of the record is worth less than stopping the music.
        }
    }
}
