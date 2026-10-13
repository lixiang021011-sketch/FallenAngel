package com.fallenangel.listeningai.listening.questions;

import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * Which question may be asked now, given what has already been asked.

 * The bank says what may be asked; this says when. One rule, about not turning
 * listening into a survey: two questions are a few bars apart. Nothing else
 * holds a question back - a question that came up again a bar later is not a
 * nuisance if it is the one the music is actually asking about, and the order
 * below keeps a repeat from happening by itself.

 * Among what is allowed, the one asked longest ago goes first. Because asking
 * moves a question to the back of that queue, this is also what keeps the same
 * question from being asked twice running: it has to wait for the others. That
 * ordering is not politeness, it is what makes the first round readable - a bank
 * that always led with the same question would collect a pile of answers about
 * that question and nothing about the rest, and this version exists to find out
 * which of the eight are worth keeping. Finishing a question the listener
 * ignored does not change that - it counts as asked, because they saw it.
 */
public final class QuestionPicker {
    /** Never a bar number, so "not asked yet" can be compared with real ones. */
    private static final int NEVER = Integer.MIN_VALUE;

    private final QuestionBank bank;
    private final int minGapBars;

    /** The bar each question was last handed out at; absent means never. */
    private final Map<String, Integer> lastAskedBarById = new HashMap<>();
    /** How often each was shown, and how often the listener let it pass. */
    private final Map<String, Integer> askedCountById = new HashMap<>();
    private final Map<String, Integer> ignoredCountById = new HashMap<>();
    private int lastAskedBar = NEVER;

    public QuestionPicker(QuestionBank bank, int minGapBars) {
        this.bank = bank;
        this.minGapBars = Math.max(1, minGapBars);
    }

    /**
     * The question to ask for this signal, or null if none may be asked.

     * Answering with a question hands it out: the spacing rule counts from here,
     * so a question the caller decides not to show still keeps the next one a few
     * bars away. Call it when the question is actually going to be shown.
     */
    public QuestionTemplate pick(String trigger, int currentBar) {
        if (lastAskedBar != NEVER && currentBar - lastAskedBar < minGapBars) return null;

        List<QuestionTemplate> candidates = bank.forTrigger(trigger);
        if (candidates.isEmpty()) return null;

        QuestionTemplate oldest = null;
        int oldestBar = NEVER;
        for (QuestionTemplate template : candidates) {
            Integer lastAt = lastAskedBarById.get(template.id);
            int at = lastAt == null ? NEVER : lastAt;
            if (oldest == null || at < oldestBar) {
                oldest = template;
                oldestBar = at;
            }
        }
        record(oldest, currentBar);
        return oldest;
    }

    /**
     * The question reached the listener at this bar.

     * For a caller that shows a question it did not take straight from
     * {@link #pick}, or shows it a little later than it was chosen. The same bar
     * twice is the same showing written down twice, not two questions.
     */
    public void asked(QuestionTemplate template, int bar) {
        if (template == null) return;
        Integer lastAt = lastAskedBarById.get(template.id);
        if (lastAt != null && lastAt.intValue() == bar) return;
        record(template, bar);
    }

    /**
     * The listener let the question pass.

     * It stays asked - it was on screen - and nothing is built on it. The
     * listener asked for no follow-up when they have already shown they are not
     * answering.
     */
    public void unanswered(QuestionTemplate template) {
        if (template == null) return;
        // Bookkeeping only: the question stands where it was recorded, and the
        // count is kept because the answer rate per question is one of the four
        // numbers this round is read by.
        ignoredCountById.merge(template.id, 1, Integer::sum);
    }

    /** How many times a question was shown. */
    public int askedCount(String id) {
        Integer count = askedCountById.get(id);
        return count == null ? 0 : count;
    }

    /** How many of those the listener left unanswered. */
    public int unansweredCount(String id) {
        Integer count = ignoredCountById.get(id);
        return count == null ? 0 : count;
    }

    private void record(QuestionTemplate template, int bar) {
        lastAskedBarById.put(template.id, bar);
        askedCountById.merge(template.id, 1, Integer::sum);
        if (bar > lastAskedBar) lastAskedBar = bar;
    }

    @Override
    public String toString() {
        return bank.all().size() + " questions, " + minGapBars + " bars apart";
    }
}
