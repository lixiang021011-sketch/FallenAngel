package com.fallenangel.listeningai.listening.questions;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * One question, as data rather than as code.

 * The point of a bank is that the same phenomenon is asked about the same way
 * every time: answers to differently-worded questions are not answers to the same
 * question, and a pile of those cannot be counted. So a question's id and its
 * options are the stable part - wording may change, and when it does the id stays
 * and the version moves, which is how old answers survive a re-write.
 */
public final class QuestionTemplate {
    /** What the answer is evidence about: "perceived_salience", "surprise", ... */
    public final String id;
    public final String text;
    /** The thing this question measures. Answers are only comparable within one. */
    public final String targetSignal;
    public final String answerType;
    public final List<String> options;
    /** Which signals may bring this question up. */
    public final List<String> triggers;

    public QuestionTemplate(String id, String text, String targetSignal, String answerType,
                            List<String> options, List<String> triggers) {
        this.id = id == null ? "" : id;
        this.text = text == null ? "" : text;
        this.targetSignal = targetSignal == null ? "" : targetSignal;
        this.answerType = answerType == null ? "" : answerType;
        this.options = options == null
                ? Collections.emptyList() : new ArrayList<>(options);
        this.triggers = triggers == null
                ? Collections.emptyList() : new ArrayList<>(triggers);
    }

    /** Whether this question may be asked because of that signal. */
    public boolean answersTo(String trigger) {
        return trigger != null && triggers.contains(trigger);
    }

    @Override
    public String toString() {
        return id + " (" + targetSignal + ")";
    }
}
