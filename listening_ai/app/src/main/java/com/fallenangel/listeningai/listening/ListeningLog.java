package com.fallenangel.listeningai.listening;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStreamWriter;
import java.io.Writer;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;

import org.json.JSONObject;

/**
 * The record of what was asked and what was answered.
 *
 * One JSON object per line, appended and never rewritten. Appending is the whole
 * design: a rejected proposal, a question the listener closed without answering,
 * a later reanalysis - all of them stay where they are and a new line says what
 * is now believed. A file that gets rewritten cannot be audited, and this data
 * exists to be audited.
 */
public final class ListeningLog {
    private final File file;

    public ListeningLog(File file) {
        this.file = file;
        File parent = file == null ? null : file.getParentFile();
        if (parent != null && !parent.exists()) parent.mkdirs();
    }

    public File file() {
        return file;
    }

    public void append(ListeningEvent event) {
        append(event.toJson());
    }

    /**
     * A line of any shape.
     *
     * The file holds two kinds of record now - what was asked when the listener
     * paused, and what the model heard when they looked at it - and they are
     * written the same way, appended and never rewritten, so one writer for both
     * keeps the promise the class is built on.
     */
    public void append(JSONObject line) {
        if (file == null) return;
        try (Writer writer = new OutputStreamWriter(
                new FileOutputStream(file, true), StandardCharsets.UTF_8)) {
            writer.write(line.toString());
            writer.write('\n');
        } catch (IOException ignored) {
            // Losing the record is worth less than stopping the music, and the
            // question is already answered by the time this runs.
        }
    }

    /** Every line written so far, for checking what was kept. */
    public List<String> read() {
        if (file == null || !file.exists()) return new ArrayList<>();
        try {
            List<String> out = new ArrayList<>();
            for (String line : Files.readAllLines(file.toPath(), StandardCharsets.UTF_8)) {
                if (!line.trim().isEmpty()) out.add(line);
            }
            return out;
        } catch (IOException e) {
            return new ArrayList<>();
        }
    }

    public int size() {
        return read().size();
    }
}
