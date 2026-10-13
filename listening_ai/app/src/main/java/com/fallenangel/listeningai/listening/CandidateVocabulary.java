package com.fallenangel.listeningai.listening;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * The words a candidate explanation is allowed to be.
 *
 * The question bank makes questions countable by giving each one an id. This does
 * the same for answers, and for the same reason: "起音密度增加" in one song and
 * "鼓点变密" in the next are the same thing said twice, and a set of labels that
 * can be anything cannot be counted, grouped or compared. Measured before this
 * existed: with no list the model echoed the facts it was sent, and answered the
 * same fact three times over in three phrasings - which meant a listener choosing
 * between candidates was often only choosing a wording.
 *
 * The price is real and worth stating: a phenomenon that is not in the list
 * cannot be said. 其他 and 都不是 are in it so that the model has somewhere to put
 * "none of these", and the raw answer is kept in the record either way, so nothing
 * is actually lost - it just does not reach a button.
 *
 * A file rather than code, like the bank: the vocabulary is the instrument, and it
 * should be arguable and replaceable without editing Java.
 */
public final class CandidateVocabulary {
    /** No list at all: everything is allowed, which is how this was before. */
    public static final CandidateVocabulary OPEN = new CandidateVocabulary("none", Collections.emptyList());

    public final String version;
    private final List<String> labels;

    public CandidateVocabulary(String version, List<String> labels) {
        this.version = version == null ? "" : version;
        this.labels = new ArrayList<>(labels == null ? Collections.emptyList() : labels);
    }

    public List<String> labels() {
        return Collections.unmodifiableList(labels);
    }

    public boolean isEmpty() {
        return labels.isEmpty();
    }

    /** Whether a candidate may use this label, exactly as written. */
    public boolean allows(String label) {
        if (labels.isEmpty()) return true;
        String wanted = label == null ? "" : label.trim();
        for (String allowed : labels) {
            if (allowed.equals(wanted)) return true;
        }
        return false;
    }

    /** The list, as the one line of the prompt that hands it over. */
    public String instruction() {
        if (labels.isEmpty()) return "";
        StringBuilder out = new StringBuilder("label 必须逐字取自下面这份清单，不许造词，不许改写：");
        out.append('[');
        for (int i = 0; i < labels.size(); i++) {
            if (i > 0) out.append(' ');
            out.append(labels.get(i));
        }
        out.append("]。");
        return out.toString();
    }

    public static CandidateVocabulary fromJson(JSONObject root) {
        if (root == null) return OPEN;
        List<String> labels = new ArrayList<>();
        JSONArray array = root.optJSONArray("labels");
        if (array != null) {
            for (int i = 0; i < array.length(); i++) {
                String label = array.optString(i, "").trim();
                if (!label.isEmpty()) labels.add(label);
            }
        }
        return new CandidateVocabulary(root.optString("version", ""), labels);
    }

    @Override
    public String toString() {
        return version + " (" + labels.size() + " labels)";
    }
}
