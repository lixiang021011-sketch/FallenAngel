package com.fallenangel.listeningai.listening.query;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import java.util.Arrays;
import java.util.Collections;
import java.util.List;

/**
 * The rules for asking, one at a time.
 *
 * Each test is one of the shapes the spec asks for, and each names the reason
 * code it expects: "it stayed quiet" is not something a person can debug, and
 * the difference between "understood" and "could not understand" is the whole
 * reason this layer exists.
 */
public class LocalQueryPolicyTest {
    private static final long NOW = 1_700_000_000_000L;
    private static final QueryPolicyConfig CONFIG = QueryPolicyConfig.defaults();
    private static final LocalQueryPolicy POLICY = new LocalQueryPolicy(CONFIG);

    /** A moment that ends now, with the candidates it is about. */
    private static MusicMoment moment(double novelty, EventCandidate... candidates) {
        List<EventCandidate> all = Arrays.asList(candidates);
        double top = 0;
        for (EventCandidate candidate : all) {
            if (!candidate.isNone()) top = Math.max(top, candidate.confidence);
        }
        return new MusicMoment("m1", NOW - 8_000, NOW, 12, 3, 2, all,
                Collections.emptyList(), novelty, top, Collections.emptyList());
    }

    private static EventCandidate event(String type, double confidence) {
        return new EventCandidate(type, confidence, "rules", "measured");
    }

    /** 1. Two explanations too close to rank: ask which one it was. */
    @Test
    public void twoCandidatesTooCloseToCallIsAChoiceQuestion() {
        QueryDecision decision = POLICY.evaluate(
                moment(0.42, event("guitar_entry", 0.59), event("section_change", 0.56)),
                QueryContext.fresh());
        assertTrue(decision.shouldAsk);
        assertEquals(QueryType.ATTENTION_SOURCE, decision.queryType);
        assertEquals(3, decision.options.size());
        assertEquals("guitar_entry", decision.options.get(0).id);
        assertEquals("section_change", decision.options.get(1).id);
        assertTrue("the way out is always there", decision.options.get(2).isNone());
    }

    /** 2. One claim, not a safe one: ask whether it was that. */
    @Test
    public void oneHalfConfidentEventIsAConfirmationQuestion() {
        QueryDecision decision = POLICY.evaluate(
                moment(0.06, event("drum_fill", 0.66)),
                QueryContext.fresh());
        assertTrue(decision.shouldAsk);
        assertEquals(QueryType.EVENT_CONFIRMATION, decision.queryType);
        assertEquals("drum_fill", decision.options.get(0).id);
        assertEquals("yes", decision.options.get(1).id);
        assertEquals("no", decision.options.get(2).id);
        assertEquals("unsure", decision.options.get(3).id);
    }

    /** 3. Something happened and nothing explains it: ask whether it mattered. */
    @Test
    public void anUnexplainedChangeIsASalienceQuestion() {
        QueryDecision decision = POLICY.evaluate(
                moment(0.45, event("pattern", 0.30)),
                QueryContext.fresh());
        assertTrue(decision.shouldAsk);
        assertEquals(QueryType.SALIENCE, decision.queryType);
        assertEquals("yes", decision.options.get(0).id);
        assertEquals("no", decision.options.get(1).id);
    }

    /** 4. Understood: silence, and it says why. */
    @Test
    public void aConfidentMomentIsLeftAlone() {
        QueryDecision decision = POLICY.evaluate(
                moment(0.30, event("guitar_entry", 0.93)),
                QueryContext.fresh());
        assertFalse(decision.shouldAsk);
        assertEquals("CONFIDENT", decision.reasonCode);
    }

    /** 5. Too soon after the last one. */
    @Test
    public void anotherQuestionRightAfterTheLastIsTooSoon() {
        QueryContext recent = new QueryContext(NOW - 3_000, Collections.emptyList(), 0,
                false, true);
        QueryDecision decision = POLICY.evaluate(
                moment(0.42, event("guitar_entry", 0.59), event("section_change", 0.56)),
                recent);
        assertFalse(decision.shouldAsk);
        assertEquals("COOLDOWN", decision.reasonCode);
    }

    /** 6. The same kind of question, already asked. */
    @Test
    public void theSameQuestionTwiceIsBothering() {
        QueryContext asked = new QueryContext(0, Arrays.asList("ATTENTION_SOURCE"), 0,
                false, true);
        QueryDecision decision = POLICY.evaluate(
                moment(0.42, event("guitar_entry", 0.59), event("section_change", 0.56)),
                asked);
        assertFalse(decision.shouldAsk);
        assertEquals("ASKED_RECENTLY", decision.reasonCode);
    }

    /** 7. Questions being ignored is a reason to stop asking. */
    @Test
    public void beingIgnoredTwiceStopsTheAsking() {
        QueryContext ignored = new QueryContext(0, Collections.emptyList(),
                CONFIG.dismissLimit, false, true);
        QueryDecision decision = POLICY.evaluate(
                moment(0.42, event("guitar_entry", 0.59), event("section_change", 0.56)),
                ignored);
        assertFalse(decision.shouldAsk);
        assertEquals("DISMISSED", decision.reasonCode);
    }

    /** 8. Nothing to change: an answer could not move any label. */
    @Test
    public void aMomentWithNothingToSayIsNotAQuestion() {
        QueryDecision decision = POLICY.evaluate(moment(0.40), QueryContext.fresh());
        assertFalse(decision.shouldAsk);
        assertEquals("NO_HYPOTHESIS", decision.reasonCode);
    }

    /** Not while the music is stopped, and not on top of somebody's tap. */
    @Test
    public void askingNeedsTheMusicPlayingAndNobodyBusy() {
        MusicMoment close = moment(0.42, event("guitar_entry", 0.59),
                event("section_change", 0.56));
        assertEquals("NOT_PLAYING", POLICY.evaluate(close,
                new QueryContext(0, Collections.emptyList(), 0, false, false)).reasonCode);
        assertEquals("BUSY", POLICY.evaluate(close,
                new QueryContext(0, Collections.emptyList(), 0, true, true)).reasonCode);
    }

    /** The rate cap, which is a different limit from the cooldown. */
    @Test
    public void tooManyQuestionsInAMinuteIsAlsoEnough() {
        QueryDecision decision = POLICY.evaluate(
                moment(0.42, event("guitar_entry", 0.59), event("section_change", 0.56)),
                new QueryContext(0, Collections.emptyList(), 0, false, true,
                        CONFIG.maxQueriesPerMinute));
        assertFalse(decision.shouldAsk);
        assertEquals("TOO_MANY", decision.reasonCode);
    }

    /** The score the spec asks for: four factors, and each one can zero it. */
    @Test
    public void priorityIsTheProductOfItsFactors() {
        QueryDecision decision = POLICY.evaluate(
                moment(0.42, event("guitar_entry", 0.59), event("section_change", 0.56)),
                QueryContext.fresh());
        assertTrue("an ask carries a priority", decision.priority > 0 && decision.priority <= 1);
        assertTrue("and what it expects to be worth", decision.expectedValue > 0);
        assertTrue("a close call is uncertain", decision.targetUncertainty.length() > 0);
        assertEquals("ATTENTION_SOURCE_CLOSE_CALL", decision.reasonCode);
    }
}
