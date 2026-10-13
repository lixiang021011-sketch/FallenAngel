package com.fallenangel.listeningai.listening.questions;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import org.json.JSONArray;
import org.junit.Test;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

/**
 * Which of the bank's questions may be asked, and when.

 * The rule is about not being a nuisance and is checkable: questions are at least
 * four bars apart. Nothing else holds a question back - there is no per-question
 * cooldown - so what keeps a question from coming up twice running is the order
 * below, and that is tested here too. A question the listener ignored is over: it
 * is not asked again straight away and nothing follows it up.

 * Among what is allowed, the one asked longest ago goes first. That is not
 * politeness, it is what makes the first round measurable: if one question always
 * came first, the others would collect too few answers to judge, and the whole
 * point of this version is to find out which questions are worth keeping.
 */
public class QuestionPickerTest {
    private static final File BANK = new File("src/main/assets/question_bank.json");

    private static QuestionBank bank() throws Exception {
        String text = new String(Files.readAllBytes(BANK.toPath()), StandardCharsets.UTF_8);
        return QuestionBank.fromJson(new JSONArray(text));
    }

    /** The bank as written, parsed: ten questions, two of them retired. */
    @Test
    public void theShippedBankParsesAndSaysWhatItMeasures() throws Exception {
        QuestionBank bank = bank();
        // Ten in the file, eight in the rotation: STRUCT_02 and ATTN_02 asked
        // whether something happened at a moment the app had stopped the music
        // because it had measured that something had - 6 to 1 "yes". Retired, kept
        // for their answers.
        assertEquals(10, bank.all().size());
        int retired = 0;
        for (QuestionTemplate template : bank.all()) if (template.retired) retired++;
        assertEquals(2, retired);
        assertNotNull(bank.byId("ATTN_01"));
        assertEquals(7, bank.byId("ATTN_01").options.size());
        assertEquals("说不清", bank.byId("ATTN_01").options.get(6));

        for (QuestionTemplate template : bank.all()) {
            assertFalse(template.id, template.triggers.isEmpty());
            assertTrue(template.id, template.options.size() >= 2);
        }
        // Every question measures a different thing, retired ones included: the
        // replacement asks the same phenomenon a different question, and answers
        // to those are not comparable, so they do not share a signal. This is why the
        // picker no longer refuses to ask about the same thing twice running:
        // with no two questions sharing a targetSignal, and repeats already
        // ruled out by the order, that rule could never fire.
        java.util.Set<String> signals = new java.util.HashSet<>();
        for (QuestionTemplate template : bank.all()) signals.add(template.targetSignal);
        assertEquals(bank.all().size(), signals.size());
        // STRUCT_03 answers to a novelty signal as well as a structural one, so
        // the novelty set is five, not the four the plain mapping suggests.
        assertEquals(5, bank.forTrigger("NOVELTY_HIGH").size());
        assertEquals(4, bank.forTrigger("INSTRUMENT_CHANGE").size());
        assertEquals(3, bank.forTrigger("STRUCTURE_CANDIDATE").size());
    }

    @Test
    public void aQuestionComesFromTheBankAndFitsTheTrigger() throws Exception {
        QuestionPicker picker = new QuestionPicker(bank(), 4);
        QuestionTemplate first = picker.pick("NOVELTY_HIGH", 0);
        assertNotNull(first);
        assertTrue(first.triggers.contains("NOVELTY_HIGH"));
        assertNull("a trigger nobody answers to asks nothing",
                picker.pick("NOT_A_TRIGGER", 0));
    }

    @Test
    public void twoQuestionsAreAtLeastFourBarsApart() throws Exception {
        QuestionPicker picker = new QuestionPicker(bank(), 4);
        QuestionTemplate first = picker.pick("NOVELTY_HIGH", 10);
        picker.asked(first, 10);

        assertNull("three bars later is too soon", picker.pick("NOVELTY_HIGH", 13));
        assertNotNull("four bars later is allowed", picker.pick("NOVELTY_HIGH", 14));
    }

    /**
     * The same question waits for the others, not for a clock.

     * There is no cooldown of its own any more, so the only thing standing
     * between a question and being asked again is that somebody else, asked less
     * recently, comes first.
     */
    @Test
    public void theSameQuestionIsNotAskedTwiceRunning() throws Exception {
        QuestionPicker picker = new QuestionPicker(bank(), 4);
        QuestionTemplate first = picker.pick("NOVELTY_HIGH", 0);
        picker.asked(first, 0);

        QuestionTemplate second = picker.pick("NOVELTY_HIGH", 4);
        assertNotNull(second);
        assertFalse("not the same question again", first.id.equals(second.id));
        picker.asked(second, 4);

        QuestionTemplate third = picker.pick("NOVELTY_HIGH", 8);
        assertNotNull(third);
        assertFalse("and not the one just asked either",
                second.id.equals(third.id));
    }

    /** Nothing is held back for long: asked all five, the first comes round again. */
    @Test
    public void theRotationComesBackAround() throws Exception {
        QuestionBank bank = bank();
        QuestionPicker picker = new QuestionPicker(bank, 4);
        java.util.List<String> asked = new java.util.ArrayList<>();
        int bar = 0;
        while (asked.size() < bank.forTrigger("NOVELTY_HIGH").size()) {
            QuestionTemplate template = picker.pick("NOVELTY_HIGH", bar);
            assertNotNull("bar " + bar + " has something to ask", template);
            asked.add(template.id);
            picker.asked(template, bar);
            bar += 4;
        }
        QuestionTemplate again = picker.pick("NOVELTY_HIGH", bar);
        assertNotNull(again);
        assertEquals("the one asked longest ago goes next", asked.get(0), again.id);
    }

    /** Every question gets its turn before any is repeated. */
    @Test
    public void theBankIsWorkedThroughRatherThanReread() throws Exception {
        QuestionBank bank = bank();
        QuestionPicker picker = new QuestionPicker(bank, 4);
        java.util.List<String> asked = new java.util.ArrayList<>();
        int bar = 0;
        while (asked.size() < bank.forTrigger("NOVELTY_HIGH").size()) {
            QuestionTemplate template = picker.pick("NOVELTY_HIGH", bar);
            assertNotNull("bar " + bar + " has something to ask", template);
            asked.add(template.id);
            picker.asked(template, bar);
            bar += 4;
        }
        java.util.Set<String> distinct = new java.util.HashSet<>(asked);
        assertEquals("every novelty question before any repeat",
                distinct.size(), asked.size());
    }

    /**
     * The numbers the first round is read by have to be countable.

     * How often a question was answered and how often it was not is the whole
     * report on a question, so the picker has to keep both rather than just the
     * last one.
     */
    @Test
    public void whatWasAnsweredAndWhatWasNotAreCountedPerQuestion() throws Exception {
        QuestionPicker picker = new QuestionPicker(bank(), 4);
        QuestionTemplate ignored = picker.pick("NOVELTY_HIGH", 0);
        picker.unanswered(ignored);
        QuestionTemplate answered = picker.pick("NOVELTY_HIGH", 4);
        picker.asked(answered, 4);

        assertEquals(1, picker.askedCount(ignored.id));
        assertEquals(1, picker.unansweredCount(ignored.id));
        assertEquals(1, picker.askedCount(answered.id));
        assertEquals(0, picker.unansweredCount(answered.id));
        assertEquals(0, picker.askedCount("ROLE_01"));
        assertEquals(0, picker.unansweredCount("ROLE_01"));
    }

    /** A question nobody answered is still a question that was asked. */
    @Test
    public void anIgnoredQuestionIsOverRatherThanFollowedUp() throws Exception {
        QuestionPicker picker = new QuestionPicker(bank(), 4);
        QuestionTemplate ignored = picker.pick("NOVELTY_HIGH", 0);
        picker.asked(ignored, 0);
        picker.unanswered(ignored);

        QuestionTemplate next = picker.pick("NOVELTY_HIGH", 4);
        assertNotNull(next);
        assertFalse("not the one they ignored", ignored.id.equals(next.id));
        assertNull("and nothing is asked about it again right away",
                picker.pick("NOVELTY_HIGH", 6));
    }
}
