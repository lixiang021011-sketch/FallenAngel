package com.fallenangel.listeningai.listening.query;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import com.fallenangel.listeningai.listening.PauseQuestion;

import org.junit.Test;

import java.util.Arrays;
import java.util.Collections;

/**
 * The demo the spec asks to be able to show, as a test.
 *
 * An ambiguous moment becomes a choice question with the way out on it; a moment
 * that is understood becomes silence. Both halves matter: a question layer that
 * only knows how to ask is a nuisance, and the second half of this test is what
 * keeps the first half worth listening to.
 */
public class AcceptanceDemoTest {
    private static final long NOW = 1_700_000_000_000L;
    private static final LocalQueryPolicy POLICY =
            new LocalQueryPolicy(QueryPolicyConfig.defaults());
    private static final TemplateQuestionRenderer RENDERER = new TemplateQuestionRenderer();

    private static MusicMoment moment(EventCandidate... candidates) {
        double top = 0;
        for (EventCandidate candidate : candidates) top = Math.max(top, candidate.confidence);
        return new MusicMoment("m1", NOW - 8_000, NOW, 12, 3, 2, Arrays.asList(candidates),
                Collections.emptyList(), 0.42, top, Collections.emptyList());
    }

    private static EventCandidate event(String type, double confidence) {
        return new EventCandidate(type, confidence, "rules", "");
    }

    @Test
    public void twoCloseCandidatesBecomeAChoiceQuestionWithAWayOut() {
        MusicMoment ambiguous = moment(event("guitar_entry", 0.59),
                event("section_change", 0.56));
        QueryDecision decision = POLICY.evaluate(ambiguous, QueryContext.fresh());
        assertTrue(decision.shouldAsk);
        assertEquals(QueryType.ATTENTION_SOURCE, decision.queryType);

        PauseQuestion question = RENDERER.render(decision, 42, 12, 3, 2, "B");
        assertEquals("刚才你更注意到哪一种变化？", question.question);
        assertEquals("吉他进入", question.candidates.get(0).label);
        assertEquals("整体段落变化", question.candidates.get(1).label);
        assertEquals(PauseQuestion.NONE_LABEL, question.candidates.get(2).label);
    }

    @Test
    public void aConfidentMomentStaysSilent() {
        QueryDecision decision = POLICY.evaluate(moment(event("guitar_entry", 0.93)),
                QueryContext.fresh());
        assertFalse(decision.shouldAsk);
        assertEquals("CONFIDENT", decision.reasonCode);
    }
}
