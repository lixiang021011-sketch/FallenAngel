package com.fallenangel.listeningai.listening;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * What has already been offered, and what was actually chosen.
 *
 * The app has been writing every answer into the record since the beginning and
 * never reading any of it back - which means each question was asked as if it were
 * the first one. This is the smallest thing that makes the record worth writing: a
 * count, per label, of how often it was offered and how often it was taken.
 *
 * What it is used for is deliberately modest: a label that has been offered
 * several times and never once chosen is not worth offering again. Anything
 * cleverer - reweighting, ranking, predicting - would be built on a handful of
 * answers and would look like knowledge while being noise.
 *
 * Two rejections are not a pattern, so the rule needs three offers before it takes
 * a label away; and one acceptance keeps a label forever, because a candidate that
 * was right once is a candidate that can be right again, and this app is short of
 * evidence rather than of opinions.
 *
 * Kept away from Android, and away from files: it counts. Reading the record back
 * in is somebody else's job.
 */
public final class AnswerMemory {
    private final Map<String, Integer> offered = new HashMap<>();
    private final Map<String, Integer> chosen = new HashMap<>();

    /** How many times a label has to be offered without ever landing. */
    public static final int TIRED_AFTER = 3;

    /**
     * One answered question: what was on the card, and what the listener picked.
     *
     * The offer counts for every label on the card, including the one chosen - a
     * label that was taken was also offered - and the choice is recorded separately
     * so that "offered nine times, chosen once" is not read as "rejected nine".
     */
    public void learn(List<String> labels, String chosenLabel) {
        if (labels != null) {
            for (String label : labels) {
                String clean = label == null ? "" : label.trim();
                if (!clean.isEmpty()) offered.merge(clean, 1, Integer::sum);
            }
        }
        String taken = chosenLabel == null ? "" : chosenLabel.trim();
        if (!taken.isEmpty()) chosen.merge(taken, 1, Integer::sum);
    }

    /** How often a label has been on the card. */
    public int offeredCount(String label) {
        Integer count = offered.get(label == null ? "" : label.trim());
        return count == null ? 0 : count;
    }

    /** How often a label has been the answer. */
    public int chosenCount(String label) {
        Integer count = chosen.get(label == null ? "" : label.trim());
        return count == null ? 0 : count;
    }

    /**
     * Whether this label has had its chances.
     *
     * Offered enough times to be a pattern, and never once the answer.
     */
    public boolean tiredOf(String label) {
        String clean = label == null ? "" : label.trim();
        if (clean.isEmpty()) return false;
        return offeredCount(clean) >= TIRED_AFTER && chosenCount(clean) == 0;
    }

    /** Labels that have had their chances, for a log line that says why. */
    public List<String> tiredLabels() {
        List<String> out = new ArrayList<>();
        for (String label : offered.keySet()) {
            if (tiredOf(label)) out.add(label);
        }
        java.util.Collections.sort(out);
        return out;
    }

    /** How many labels are known at all, for the log line at start-up. */
    public int size() {
        return offered.size();
    }
}
