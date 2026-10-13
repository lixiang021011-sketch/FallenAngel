package com.fallenangel.listeningai.listening.query;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import java.util.Arrays;
import java.util.Collections;

/**
 * One line per moment, saying what was decided and why.
 *
 * This is the artifact that makes the policy tunable: with it, "why did it ask
 * here" and "why did it stay quiet there" are questions with answers instead of
 * a feeling. Fixed field order, and a dash rather than nothing when a field has
 * no value, so that a column can be read down the file.
 */
public class QueryDumpTest {
    private static final MusicMoment MOMENT = new MusicMoment("m7", 1_700_000_000_000L,
            1_700_000_008_000L, 23, 4, 2,
            Arrays.asList(new EventCandidate("guitar_entry", 0.59, "rules", ""),
                    new EventCandidate("section_change", 0.56, "rules", "")),
            Collections.emptyList(), 0.42, 0.59, Collections.emptyList());

    @Test
    public void theLineHasEveryFieldInAFixedOrder() {
        QueryDecision asked = new QueryDecision(true, QueryType.ATTENTION_SOURCE,
                "两种解释分不清", Collections.emptyList(), 0.44, 0.59,
                "ATTENTION_SOURCE_CLOSE_CALL");
        String line = QueryDump.oneLine(42.8, MOMENT, asked, "guitar_entry");
        assertEquals("00:42.8 bar=24 beat=5 moment=m7 top=guitar_entry@0.59"
                + " second=section_change@0.56 novelty=0.42 ask=true"
                + " type=ATTENTION_SOURCE priority=0.44 reason=ATTENTION_SOURCE_CLOSE_CALL"
                + " answer=guitar_entry", line);
    }

    @Test
    public void aSilenceSaysWhy() {
        String line = QueryDump.oneLine(42.8, MOMENT, QueryDecision.no("CONFIDENT"), "-");
        assertTrue("the reason is the point: " + line, line.contains("ask=false"));
        assertTrue(line.contains("type=NONE"));
        assertTrue(line.contains("reason=CONFIDENT"));
        assertTrue("nothing was answered", line.endsWith("answer=-"));
    }

    @Test
    public void aMissingSecondCandidateIsADash() {
        MusicMoment alone = new MusicMoment("m8", 0, 1, 0, 0, 0,
                Collections.singletonList(new EventCandidate("drum_fill", 0.66, "rules", "")),
                Collections.emptyList(), 0.06, 0.66, Collections.emptyList());
        String line = QueryDump.oneLine(42.8, alone, QueryDecision.no("COOLDOWN"), "-");
        assertTrue("no invented second candidate: " + line,
                line.contains("second=- novelty"));
        assertTrue(line.contains("top=drum_fill@0.66"));
    }
}
