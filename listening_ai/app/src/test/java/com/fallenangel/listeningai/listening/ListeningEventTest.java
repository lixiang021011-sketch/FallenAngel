package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.json.JSONObject;
import org.junit.Test;

import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;

/**
 * What the system writes down, and that answering never rewrites the question.
 *
 * The research value of this data is that the AI's proposal and the person's
 * answer are both in it, separately. A record that only keeps the answer cannot
 * be used to find out where the AI is wrong, which is the whole point.
 */
public class ListeningEventTest {
    private static PauseQuestion question() {
        List<PauseQuestion.Candidate> candidates = new ArrayList<>();
        candidates.add(new PauseQuestion.Candidate("pattern", "鼓型发生变化", 0.62,
                "八分位置上的鼓型相似度 0.21"));
        candidates.add(new PauseQuestion.Candidate("fill", "出现 fill / 密集过门", 0.40,
                "0.9 秒内 5 个起音"));
        candidates.add(new PauseQuestion.Candidate("none", PauseQuestion.NONE_LABEL, 0, ""));
        return new PauseQuestion("你刚才可能注意到的是：", 42L * 44100, "B",
                23, 4, 2, candidates);
    }

    @Test
    public void aProposalSaysWhatWasOfferedAndWhere() throws Exception {
        ListeningEvent event = ListeningEvent.proposed("ev1", question(), 42.830, 0.9, 1000L);

        assertEquals(ListeningEvent.STATE_PROPOSED, event.state);
        assertEquals("", event.chosenId);
        assertEquals(42.830, event.interactionSeconds, 1e-9);
        assertEquals(42.0, event.targetSeconds, 1e-9);
        assertEquals(23, event.bar);
        assertEquals(4, event.beat);
        assertEquals("&", event.subdivisionName);
        assertEquals("B", event.sectionLabel);
        assertEquals("the strongest explanation is the perception confidence",
                0.62, event.perceptionConfidence, 1e-9);
        assertEquals("nothing is claimed about meaning before an answer",
                0.0, event.semanticConfidence, 1e-9);

        JSONObject json = event.toJson();
        assertEquals(42.83, json.getDouble("interactionTime"), 1e-9);
        assertEquals(42.0, json.getDouble("targetMusicTime"), 1e-9);
        assertEquals(23, json.getInt("bar"));
        assertEquals("&", json.getString("subdivisionName"));
        assertEquals(3, json.getJSONArray("candidates").length());
        assertEquals("pattern", json.getJSONArray("candidates").getJSONObject(0).getString("id"));
        assertEquals(0.62, json.getJSONObject("confidence").getDouble("perception"), 1e-9);
    }

    @Test
    public void answeringAddsARecordAndLeavesTheQuestionAlone() {
        ListeningEvent proposed = ListeningEvent.proposed("ev2", question(), 42.830, 0.9, 1000L);
        ListeningEvent answered = proposed.answered(proposed.candidates.get(0), 2000L);

        assertEquals(ListeningEvent.STATE_CONFIRMED, answered.state);
        assertEquals("pattern", answered.chosenId);
        assertEquals("鼓型发生变化", answered.chosenLabel);
        assertEquals("the chosen explanation carries its own confidence",
                0.62, answered.semanticConfidence, 1e-9);
        assertEquals("the same proposal is still in the confirmed record",
                3, answered.candidates.size());
        assertEquals("and the original question is untouched",
                ListeningEvent.STATE_PROPOSED, proposed.state);
        assertEquals("", proposed.chosenId);
    }

    @Test
    public void theLogKeepsBothLinesAndNeverRewrites() throws Exception {
        java.io.File file = new java.io.File(
                Files.createTempDirectory("listening").toFile(), "events.jsonl");
        ListeningLog log = new ListeningLog(file);

        ListeningEvent proposed = ListeningEvent.proposed("ev3", question(), 42.830, 0.9, 1000L);
        log.append(proposed);
        log.append(proposed.answered(proposed.candidates.get(1), 3000L));

        List<String> lines = new ListeningLog(file).read();
        assertEquals(2, lines.size());
        JSONObject first = new JSONObject(lines.get(0));
        JSONObject second = new JSONObject(lines.get(1));
        assertEquals("AI_PROPOSED", first.getString("state"));
        assertEquals("USER_CONFIRMED", second.getString("state"));
        assertEquals("the offer is still in the first line",
                3, first.getJSONArray("candidates").length());
        assertEquals("fill", second.getString("chosenId"));
        assertFalse(second.has("revision"));
        assertTrue(new ListeningLog(file).size() == 2);
    }
}
