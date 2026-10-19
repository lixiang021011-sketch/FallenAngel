package com.fallenangel.overlaybeat;

import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;

/**
 * The audio of songs this phone has already played, kept so the next time can
 * have a whole chart instead of a guess.
 *
 * The live path has to commit to a note before it can hear it, and everything
 * awkward about it comes from that. A song that has been heard once does not
 * have that problem: the audio is on disk, the grid can be fitted to the whole
 * song, and the chart can be made from what the music actually does. So the
 * first time a song plays it is recorded; from the second time it is analysed
 * first and played from the answer.
 *
 * What makes a recording worth keeping is that it is the song, in order, from
 * the beginning. A seek jumps the audio, a second play-through appends to the
 * first, and starting in the middle leaves a hole - any of those makes a file
 * that is not the song, and a chart made from it would be wrong in a way that
 * looks like a broken chart rather than a broken recording. Those passes are
 * thrown away instead.
 */
public final class SongCache {
    /** Capture that starts later than this into the song is not the whole song. */
    private static final double MAX_START_POSITION_SECONDS = 2.0;
    /** A recording has to reach this much of the song's length to count. */
    private static final double COMPLETE_SHARE = 0.9;
    /** Songs kept before the least recently used are dropped. */
    private static final int KEEP = 12;

    private final File dir;
    private final File indexFile;

    public SongCache(File dir) {
        this.dir = dir;
        this.indexFile = dir == null ? null : new File(dir, "index.json");
        if (dir != null && !dir.exists()) dir.mkdirs();
        sweepOrphans();
    }

    /**
     * Delete recordings the index does not know about.
     *
     * These are passes that were interrupted - the service stopped, the phone
     * killed the app - and they are the one kind of rubbish a cache must not
     * keep, because there is nothing to say how long they are or what they are.
     * A real phone had a two second fragment of a song sitting in here, in the
     * way of a cache that was supposed to be answers.
     */
    public void sweepOrphans() {
        if (dir == null) return;
        File[] files = dir.listFiles((d, name) -> name.endsWith(".raw"));
        if (files == null) return;
        JSONObject known = readIndex();
        for (File file : files) {
            String name = file.getName();
            String key = name.substring(0, name.length() - 4);
            if (!known.has(key)) file.delete();
        }
    }

    public File directory() {
        return dir;
    }

    /** What is known about a song that has been recorded. */
    public static final class Entry {
        public final String key;
        public final String title;
        public final long samples;
        public final boolean complete;
        public final long used;

        Entry(String key, String title, long samples, boolean complete, long used) {
            this.key = key;
            this.title = title;
            this.samples = samples;
            this.complete = complete;
            this.used = used;
        }
    }

    public Entry get(String key) {
        JSONObject all = readIndex();
        JSONObject entry = all.optJSONObject(key);
        if (entry == null) return null;
        return new Entry(key, entry.optString("title", ""),
                entry.optLong("samples", 0), entry.optBoolean("complete", false),
                entry.optLong("used", 0));
    }

    public int size() {
        return readIndex().length();
    }

    /** The whole song's audio, or null when there is not a usable recording. */
    public short[] read(String key) {
        Entry entry = get(key);
        if (entry == null || !entry.complete) return null;
        File file = new File(dir, entry.key + ".raw");
        if (!file.exists()) return null;
        try {
            byte[] bytes = Files.readAllBytes(file.toPath());
            short[] pcm = new short[bytes.length / 2];
            for (int i = 0; i < pcm.length; i++) {
                pcm[i] = (short) ((bytes[2 * i] & 0xFF) | (bytes[2 * i + 1] << 8));
            }
            return pcm;
        } catch (Exception e) {
            return null;
        }
    }

    /** Note that a cached song has just been used, so eviction leaves it alone. */
    public void touch(String key) {
        JSONObject all = readIndex();
        JSONObject entry = all.optJSONObject(key);
        if (entry == null) return;
        try {
            entry.put("used", System.currentTimeMillis());
            writeIndex(all);
        } catch (Exception ignored) {
        }
    }

    /** Start recording a pass of a song. */
    public Recorder begin(String key, String title, double startPositionSeconds) {
        File file = new File(dir, key + ".raw");
        try {
            OutputStream out = new FileOutputStream(file, false);
            return new Recorder(key, title, startPositionSeconds, file, out);
        } catch (Exception e) {
            return null;
        }
    }

    /** One pass at a song, from the first buffer to the last. */
    public final class Recorder {
        private final String key;
        private final String title;
        private final double startPosition;
        private final File file;
        private final OutputStream out;
        private long samples;
        private boolean valid = true;
        private boolean closed;

        Recorder(String key, String title, double startPosition, File file, OutputStream out) {
            this.key = key;
            this.title = title;
            this.startPosition = startPosition;
            this.file = file;
            this.out = out;
        }

        /** Whether this pass is still the whole song from the beginning. */
        public boolean usable() {
            return valid && !closed && startPosition <= MAX_START_POSITION_SECONDS;
        }

        public void write(short[] buffer, int count) {
            if (!usable() || count <= 0) return;
            byte[] bytes = new byte[count * 2];
            for (int i = 0; i < count; i++) {
                bytes[2 * i] = (byte) buffer[i];
                bytes[2 * i + 1] = (byte) (buffer[i] >> 8);
            }
            try {
                out.write(bytes);
                samples += count;
            } catch (IOException e) {
                invalidate();
            }
        }

        /** The recording stopped being the song - a seek, or a new song. */
        public void invalidate() {
            valid = false;
        }

        /** How many seconds of audio have been recorded so far. */
        public double secondsWritten() {
            return samples / (double) OnsetCurves.SAMPLE_RATE;
        }

        /**
         * The playhead moved. Only a move that puts the song and the recording
         * out of step matters.
         *
         * Every track change arrives here as a seek to zero - the session resets
         * its position and reports it as a jump - and the first version of this
         * threw the recording away for it, so nothing was ever cached at all.
         * What makes a recording wrong is a jump forward, which leaves a hole in
         * the audio, or a jump back, which duplicates it. The test is whether
         * the file still matches where the song is.
         */
        public void seek(double positionSeconds) {
            if (!usable() || positionSeconds < 0) return;
            double written = samples / (double) OnsetCurves.SAMPLE_RATE;
            double expected = positionSeconds - startPosition;
            if (Math.abs(written - expected) > 1.0) invalidate();
        }

        /**
         * Close the pass. It is kept only if it is the song from the beginning
         * and reaches the end; anything else is deleted, because a partial file
         * is worse than no file: it looks like an answer.
         *
         * @return true when the recording was kept
         */
        public boolean finish(double durationSeconds, String resolvedTitle) {
            if (closed) return false;
            // Read the standing of the pass before closing it: "usable" means
            // "still recording and still the song", and a finished pass is
            // neither, which is how the first version of this threw away every
            // recording it was given.
            boolean worthwhile = valid && startPosition <= MAX_START_POSITION_SECONDS;
            closed = true;
            try {
                out.close();
            } catch (IOException ignored) {
            }
            double seconds = samples / (double) OnsetCurves.SAMPLE_RATE;
            boolean whole = worthwhile && durationSeconds > 0
                    && seconds >= durationSeconds * COMPLETE_SHARE;
            if (!whole) {
                file.delete();
                return false;
            }
            JSONObject all = readIndex();
            try {
                JSONObject entry = new JSONObject();
                entry.put("title", resolvedTitle == null || resolvedTitle.isEmpty()
                        ? title : resolvedTitle);
                entry.put("samples", samples);
                entry.put("complete", true);
                entry.put("used", System.currentTimeMillis());
                all.put(key, entry);
                writeIndex(all);
            } catch (Exception e) {
                file.delete();
                return false;
            }
            evict();
            return true;
        }

        /** Give up on this pass without keeping anything. */
        public void abandon() {
            invalidate();
            finish(0, null);
        }
    }

    /** Drop the least recently used songs, and any entry whose file has gone. */
    public void evict() {
        JSONObject all = readIndex();
        for (java.util.Iterator<String> it = all.keys(); it.hasNext(); ) {
            String key = it.next();
            if (!new File(dir, key + ".raw").exists()) it.remove();
        }
        List<String> keys = new ArrayList<>();
        for (java.util.Iterator<String> it = all.keys(); it.hasNext(); ) keys.add(it.next());
        keys.sort((a, b) -> Long.compare(
                all.optJSONObject(b).optLong("used", 0),
                all.optJSONObject(a).optLong("used", 0)));
        for (int i = KEEP; i < keys.size(); i++) {
            String key = keys.get(i);
            all.remove(key);
            new File(dir, key + ".raw").delete();
        }
        writeIndex(all);
    }

    /**
     * A file name for a song, from whatever the player calls it.
     *
     * A hash rather than the name itself: song titles contain slashes, colons
     * and question marks, and a cache that writes files named after them is a
     * cache that occasionally writes somewhere else.
     */
    public static String keyFor(String trackKey) {
        String normalised = Names.normalise(trackKey);
        if (normalised.isEmpty()) return null;
        try {
            byte[] hash = java.security.MessageDigest.getInstance("SHA-256")
                    .digest(normalised.getBytes(StandardCharsets.UTF_8));
            StringBuilder out = new StringBuilder();
            for (int i = 0; i < 8; i++) out.append(String.format(java.util.Locale.US, "%02x", hash[i]));
            return out.toString();
        } catch (Exception e) {
            return null;
        }
    }

    private JSONObject readIndex() {
        if (indexFile == null || !indexFile.exists()) return new JSONObject();
        try {
            String text = new String(Files.readAllBytes(indexFile.toPath()),
                    StandardCharsets.UTF_8);
            return new JSONObject(text);
        } catch (Exception e) {
            return new JSONObject();
        }
    }

    private void writeIndex(JSONObject all) {
        if (indexFile == null) return;
        try {
            Files.write(indexFile.toPath(), all.toString().getBytes(StandardCharsets.UTF_8));
        } catch (Exception ignored) {
        }
    }
}
