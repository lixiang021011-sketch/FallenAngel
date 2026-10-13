package com.fallenangel.listeningai.listening.query;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import com.fallenangel.listeningai.listening.PauseQuestion;

import org.junit.Test;

import java.util.Arrays;

/**
 * Turning a decision into words.
 *
 * The policy decides and this speaks, which is the only reason a cloud model can
 * later be dropped in here without the decision changing. Two things are checked
 * that a language model would be trusted to get right and a template is not:
 * every question offers the way out, and a question that was never decided on
 * cannot be rendered.
 */
public class TemplateQuestionRendererTest {
    private static final long TARGET = 44100L * 42;
    private static final TemplateQuestionRenderer RENDERER = new TemplateQuestionRenderer();

    private static QueryDecision decision(QueryType type, QueryOption... options) {
        return new QueryDecision(true, type, "uncertain", Arrays.asList(options), 0.4, 0.6,
                "TEST");
    }

    @Test
    public void aChoiceIsAskedInTheListenersWords() {
        PauseQuestion question = RENDERER.render(
                decision(QueryType.ATTENTION_SOURCE,
                        new QueryOption("guitar_entry", "guitar_entry"),
                        new QueryOption("section_change", "section_change"),
                        new QueryOption("none", "none")),
                TARGET, 12, 3, 2, "B");
        assertEquals("刚才你更注意到哪一种变化？", question.question);
        assertEquals("吉他进入", question.candidates.get(0).label);
        assertEquals("整体段落变化", question.candidates.get(1).label);
        assertEquals(PauseQuestion.NONE_LABEL, question.candidates.get(2).label);
        assertEquals(TARGET, question.targetSample);
        assertEquals(12, question.bar);
        assertEquals(3, question.beat);
        assertEquals(2, question.subdivision);
        assertEquals("B", question.sectionLabel);
    }

    @Test
    public void aConfirmationIsAskedAsAYesOrNo() {
        PauseQuestion question = RENDERER.render(
                decision(QueryType.EVENT_CONFIRMATION,
                        new QueryOption("drum_fill", "drum_fill"),
                        new QueryOption("yes", "yes"),
                        new QueryOption("no", "no"),
                        new QueryOption("unsure", "unsure")),
                TARGET, 4, 1, 0, "");
        assertTrue("names the claim: " + question.question,
                question.question.contains("鼓"));
        assertEquals("鼓的变化 / 过门", question.candidates.get(0).label);
        assertEquals("是", question.candidates.get(1).label);
        assertEquals("不是", question.candidates.get(2).label);
        assertEquals("不确定", question.candidates.get(3).label);
    }

    @Test
    public void salienceAsksWhetherItMatteredAtAll() {
        PauseQuestion question = RENDERER.render(
                decision(QueryType.SALIENCE,
                        new QueryOption("yes", "yes"), new QueryOption("no", "no")),
                TARGET, 4, 1, 0, "");
        assertTrue(question.question.contains("值得注意"));
        assertEquals("有", question.candidates.get(0).label);
        assertEquals("没有", question.candidates.get(1).label);
    }

    /** A type nobody has a word for is shown as it came, not hidden. */
    @Test
    public void anUnknownCandidateIsShownAsItIs() {
        PauseQuestion question = RENDERER.render(
                decision(QueryType.ATTENTION_SOURCE,
                        new QueryOption("sitar_solo", "sitar_solo"),
                        new QueryOption("none", "none")),
                TARGET, 1, 1, 0, "");
        assertEquals("sitar_solo", question.candidates.get(0).label);
    }

    /** Rendering a decision that was never taken is a bug, not an empty card. */
    @Test
    public void aDecisionNotToAskCannotBeRendered() {
        try {
            RENDERER.render(QueryDecision.no("CONFIDENT"), TARGET, 1, 1, 0, "");
            org.junit.Assert.fail("a question that was never decided on has no wording");
        } catch (IllegalArgumentException expected) {
            assertTrue(expected.getMessage().contains("CONFIDENT"));
        }
    }
}
