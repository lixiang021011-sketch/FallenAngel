package com.fallenangel.overlaybeat;

import java.io.File;
import java.util.Locale;

/**
 * The MIDI files sitting on this phone, and which one is the song playing.
 *
 * Matching is on the file name, because that is all a MIDI file reliably
 * carries: the download that produced it named it after the song, while its
 * track names are usually instruments ("Piano", "PART DRUMS"). Nothing is
 * parsed here - a file is only read once the song playing has claimed it, so a
 * folder of files costs a directory listing, not a folder of parsing.
 */
public final class MidiLibrary {
    private static final String[] EXTENSIONS = {".mid", ".midi"};

    private final File dir;

    public MidiLibrary(File dir) {
        this.dir = dir;
    }

    public File directory() {
        return dir;
    }

    public int count() {
        File[] files = files();
        return files == null ? 0 : files.length;
    }

    /** The best file for a song, or null when nothing is close enough. */
    public File find(String artist, String title) {
        String wantedTitle = Names.normalise(title);
        if (wantedTitle.isEmpty()) return null;
        String wantedArtist = Names.normalise(artist);

        File best = null;
        int bestScore = 0;
        File[] files = files();
        if (files == null) return null;
        for (File file : files) {
            // Everything the site put in the name, minus what the format added.
            String name = Names.normalise(strip(file.getName()));
            int score = Names.score(name, wantedTitle);
            if (score == 0) continue;
            if (!wantedArtist.isEmpty() && name.contains(wantedArtist)) score += 50;
            if (score > bestScore) {
                bestScore = score;
                best = file;
            }
        }
        return best;
    }

    private static String strip(String fileName) {
        String lower = fileName.toLowerCase(Locale.ROOT);
        for (String extension : EXTENSIONS) {
            if (lower.endsWith(extension)) {
                return fileName.substring(0, fileName.length() - extension.length());
            }
        }
        return fileName;
    }

    private File[] files() {
        if (dir == null) return null;
        return dir.listFiles((d, name) -> {
            String lower = name.toLowerCase(Locale.ROOT);
            for (String extension : EXTENSIONS) {
                if (lower.endsWith(extension)) return true;
            }
            return false;
        });
    }
}
