package com.fallenangel.listeningai.listening.query;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.json.JSONObject;
import org.junit.Test;

import java.util.Arrays;
import java.util.Collections;

/**
 * What the listener answered, and what the question was.
 *
 * The point of the record is that a later reanalysis can ask "why was this
 * asked, and what did the person say" without the app being present. So the
 * question's own reasoning travels with the answer: which uncertainty, which
 * moment, which policy version, and where in the music.
 */
public class FeedbackRecordTest {
    private static final MusicMoment MOMENT = new MusicMoment("m7", 1_700_000_000_000L,
            1_700_000_008_000L, 23, 4, 2,
            Arrays.asList(new EventCandidate("guitar_entry", 0.59, "rules", "onset"),
                    new EventCandidate("section_change", 0.56, "rules", "level")),
            Collections.emptyList(), 0.42, 0.59, Collections.emptyList());
    private static final QueryDecision DECISION = new QueryDecision(true,
            QueryType.ATTENTION_SOURCE, "两种解释分不清：guitar_entry / section_change",
            Arrays.asList(new QueryOption("guitar_entry", "guitar_entry"),
                    new QueryOption("section_change", "section_change"),
                    new QueryOption("none", "none")),
            0.44, 0.59, "ATTENTION_SOURCE_CLOSE_CALL");

    @Test
    public void theQuestionLineCarriesItsOwnReasoning() throws Exception {
        JSONObject line = FeedbackRecord.proposed("f1", DECISION, MOMENT, 42.8,
                1_700_000_008_500L);
        assertEquals(FeedbackRecord.KIND, line.getString("kind"));
        assertEquals(FeedbackRecord.STATE_PROPOSED, line.getString("state"));
        assertEquals("f1", line.getString("id"));
        assertEquals("m7", line.getString("momentId"));
        assertEquals("ATTENTION_SOURCE", line.getString("queryType"));
        assertEquals("ATTENTION_SOURCE_CLOSE_CALL", line.getString("reasonCode"));
        assertEquals(0.44, line.getDouble("priority"), 1e-9);
        assertEquals(42.8, line.getDouble("audioTime"), 1e-9);
        assertEquals(23, line.getInt("barIndex"));
        assertEquals(4, line.getInt("beatIndex"));
        assertEquals(2, line.getInt("subdivision"));
        assertEquals(FeedbackRecord.POLICY_VERSION, line.getString("policyVersion"));
        assertTrue("the uncertainty it was about: " + line,
                line.getString("targetUncertainty").contains("guitar_entry"));
    }

    @Test
    public void theAnswerLineIsAboutTheSameQuestion() throws Exception {
        JSONObject chosen = FeedbackRecord.answered("f1", "guitar_entry", false,
                1_700_000_010_000L);
        assertEquals(FeedbackRecord.STATE_CONFIRMED, chosen.getString("state"));
        assertEquals("f1", chosen.getString("id"));
        assertEquals("guitar_entry", chosen.getString("selectedOption"));
        assertFalse(chosen.getBoolean("dismissed"));
        assertEquals(1_700_000_010_000L, chosen.getLong("answeredAtMs"));
    }

    /** Closed without answering is a fact worth keeping, and not an answer. */
    @Test
    public void anIgnoredQuestionHasNoAnswer() throws Exception {
        JSONObject dismissed = FeedbackRecord.answered("f1", "", true,
                1_700_000_020_000L);
        assertTrue(dismissed.getBoolean("dismissed"));
        assertEquals("", dismissed.getString("selectedOption"));
        assertEquals("an ignored question carries no answer, even one passed by mistake",
                "", FeedbackRecord.answered("f1", "yes", true, 0)
                        .getString("selectedOption"));
    }
}
