package com.fallenangel.overlaybeat;

/**
 * Deciding whether two systems are talking about the same song.
 *
 * The two sides name songs differently and neither is wrong: a player reports a
 * title, a chart file reports what its author typed, a MIDI file usually reports
 * whatever the download site called it. So a name is reduced to the part both
 * sides can agree on - letters and digits, no case, no punctuation.
 *
 * Letters from any script are kept. The first version of this stripped
 * everything outside a-z0-9, which turned "酸橙色信笺" into an empty string and
 * made every Chinese title match nothing at all.
 */
final class Names {

    private Names() {
    }

    static String normalise(String value) {
        if (value == null) return "";
        StringBuilder out = new StringBuilder(value.length());
        for (int i = 0; i < value.length(); i++) {
            char c = value.charAt(i);
            if (Character.isLetterOrDigit(c)) {
                out.append(Character.toLowerCase(c));
            }
        }
        return out.toString();
    }

    /**
     * How well a name answers to a song, 0 when it does not.
     *
     * Exact is one song, containment is usually one song plus a remix or a
     * difficulty in the name. Used to rank candidates, never to decide alone.
     */
    static int score(String candidate, String wanted) {
        if (candidate.isEmpty() || wanted.isEmpty()) return 0;
        if (candidate.equals(wanted)) return 100;
        if (candidate.contains(wanted) || wanted.contains(candidate)) return 50;
        return 0;
    }
}
