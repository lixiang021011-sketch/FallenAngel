package com.fallenangel.listeningai.listening;

import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * How much history of one song to keep.
 *
 * Every listen writes its own directory, so a song heard five times has five of
 * them, and the whole point of the project is to accumulate those - they are the
 * record. But unbounded is not free, and the listener asked for three: the newest
 * three listens of each song stay, older ones go.
 *
 * This deletes data, so the rule is written to be as unadventurous as possible.
 * Anything whose name does not parse as a session is left alone. The session that
 * is happening now is never a candidate, whatever its position. Songs are counted
 * separately, so a song heard once is unaffected by a song heard nine times.
 *
 * Kept away from Android: it decides from names, and somebody else does the
 * deleting.
 */
public final class SessionKeeper {
    /** How many listens of one song are kept. */
    public static final int KEEP = 3;

    /**
     * Session directory names are "<yyyyMMdd HHmmss> <song>", which sorts by time
     * as plain text - the whole reason the name is written that way.
     */
    private static final int STAMP_LENGTH = 15;

    /**
     * Which song a session directory belongs to, or "" if it is not one.
     *
     * Directory names are "<yyyyMMdd HHmmss> <song>", and the song part may carry a
     * numeric suffix: two listens that start in the same second cannot have the same
     * directory name, so the second one gets " 2". Which means the same song ends up
     * under two names - "月牙湾" and "月牙湾 2" - and anything matching or counting by
     * the raw name treats them as two songs. Measured 2026-10-13: the chord cache
     * never found a previous listen for exactly this reason, and the retention rule
     * kept three of each.
     */
    public static String songOf(String name) {
        if (name == null || name.length() <= STAMP_LENGTH + 1) return "";
        if (name.charAt(8) != ' ' || name.charAt(STAMP_LENGTH) != ' ') return "";
        for (int i = 0; i < STAMP_LENGTH; i++) {
            char c = name.charAt(i);
            boolean digit = c >= '0' && c <= '9';
            if (!digit && !(i == 8 && c == ' ')) return "";
        }
        String song = name.substring(STAMP_LENGTH + 1).trim();
        int space = song.lastIndexOf(' ');
        if (space > 0) {
            String tail = song.substring(space + 1);
            boolean numbered = !tail.isEmpty();
            for (int i = 0; i < tail.length() && numbered; i++) {
                if (tail.charAt(i) < '0' || tail.charAt(i) > '9') numbered = false;
            }
            if (numbered) song = song.substring(0, space).trim();
        }
        return song;
    }

    /**
     * @param names   what is in the listening directory
     * @param current the directory of the listen happening now, never deleted
     * @return the ones to remove, oldest first
     */
    public static List<String> toDelete(List<String> names, String current) {
        List<String> doomed = new ArrayList<>();
        if (names == null) return doomed;
        Map<String, List<String>> bySong = new HashMap<>();
        for (String name : names) {
            String song = songOf(name);
            if (song.isEmpty()) continue;
            bySong.computeIfAbsent(song, key -> new ArrayList<>()).add(name);
        }
        for (List<String> sessions : bySong.values()) {
            if (sessions.size() <= KEEP) continue;
            Collections.sort(sessions);                    // oldest first, by name
            for (int i = 0; i < sessions.size() - KEEP; i++) {
                String name = sessions.get(i);
                if (!name.equals(current)) doomed.add(name);
            }
        }
        Collections.sort(doomed);
        return doomed;
    }

    private SessionKeeper() {
    }
}
