package com.fallenangel.listeningai.listening.questions;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * The questions this build is allowed to ask, read from the asset.

 * A file rather than code because the bank is the research instrument: it is meant
 * to be read, argued with, versioned and replaced by somebody who is not editing
 * Java. A bank that does not describe itself is refused rather than half-used - an
 * option list with two entries where the writer meant six silently changes what an
 * answer means.
 */
public final class QuestionBank {
    private final List<QuestionTemplate> templates;

    public QuestionBank(List<QuestionTemplate> templates) {
        this.templates = new ArrayList<>(templates == null
                ? Collections.emptyList() : templates);
        check();
    }

    /** Every question, in the order the file lists them. */
    public List<QuestionTemplate> all() {
        return Collections.unmodifiableList(templates);
    }

    /**
     * The questions that answer to a signal, in file order.
     *
     * Retired ones are not among them: they are in the file so their answers stay
     * comparable by id, not so they get asked again.
     */
    public List<QuestionTemplate> forTrigger(String trigger) {
        List<QuestionTemplate> out = new ArrayList<>();
        for (QuestionTemplate template : templates) {
            if (!template.retired && template.answersTo(trigger)) out.add(template);
        }
        return out;
    }

    public QuestionTemplate byId(String id) {
        for (QuestionTemplate template : templates) {
            if (template.id.equals(id)) return template;
        }
        return null;
    }

    public static QuestionBank fromJson(JSONArray array) {
        List<QuestionTemplate> parsed = new ArrayList<>();
        if (array != null) {
            for (int i = 0; i < array.length(); i++) {
                JSONObject item = array.optJSONObject(i);
                if (item == null) throw new IllegalArgumentException("question " + i + " is not an object");
                parsed.add(new QuestionTemplate(
                        item.optString("id"),
                        item.optString("text"),
                        item.optString("targetSignal"),
                        item.optString("answerType", "single_choice"),
                        strings(item.optJSONArray("options")),
                        strings(item.optJSONArray("triggers")),
                        item.optBoolean("retired", false)));
            }
        }
        return new QuestionBank(parsed);
    }

    private static List<String> strings(JSONArray array) {
        List<String> out = new ArrayList<>();
        if (array == null) return out;
        for (int i = 0; i < array.length(); i++) out.add(array.optString(i));
        return out;
    }

    /** Everything that has to be true for an answer to mean anything. */
    private void check() {
        List<String> ids = new ArrayList<>();
        for (QuestionTemplate template : templates) {
            if (template.id.isEmpty()) throw new IllegalArgumentException("a question has no id");
            if (template.text.isEmpty()) throw new IllegalArgumentException(template.id + " has no text");
            if (template.targetSignal.isEmpty()) {
                throw new IllegalArgumentException(template.id + " measures nothing");
            }
            if (template.options.size() < 2) {
                throw new IllegalArgumentException(template.id + " has fewer than two answers,"
                        + " so no answer it collects can distinguish anything");
            }
            if (template.triggers.isEmpty()) {
                throw new IllegalArgumentException(template.id + " can never be asked");
            }
            if (ids.contains(template.id)) {
                throw new IllegalArgumentException("two questions share the id " + template.id);
            }
            ids.add(template.id);
        }
    }

    @Override
    public String toString() {
        return templates.size() + " questions";
    }
}
