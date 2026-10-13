package com.fallenangel.listeningai.listening.questions;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertTrue;

import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.Test;

/**
 * Which questions may still be asked, and what happens to the ones that may not.
 *
 * The first round of questions was written before there was any data about how
 * they answered. Two of them turned out to ask about a change the app had already
 * measured - "was there a change here" at a moment the app had stopped the music
 * *because* of one - and the answers came back six to one, which is a premise
 * being recorded rather than a listener being asked. Those questions are retired,
 * not deleted: their answers are only comparable with each other by id, and by id
 * they are the control the next wording is measured against.
 */
public class QuestionBankTest {
    private static JSONArray bank(String... retireIds) throws Exception {
        JSONArray array = new JSONArray();
        array.put(question("STRUCT_02", "有没有转折", "STRUCTURE_CANDIDATE", false));
        array.put(question("STRUCT_03", "是哪一种转折", "STRUCTURE_CANDIDATE", true));
        return array;
    }

    private static JSONObject question(String id, String text, String trigger, boolean retired)
            throws Exception {
        return new JSONObject()
                .put("id", id)
                .put("text", text)
                .put("targetSignal", "perceived_" + id.toLowerCase())
                .put("options", new JSONArray().put("是").put("不是"))
                .put("triggers", new JSONArray().put(trigger))
                .put("retired", retired);
    }

    @Test
    public void aRetiredQuestionIsStillInTheFileAndOutOfTheRotation() throws Exception {
        QuestionBank parsed = QuestionBank.fromJson(bank());

        assertEquals(2, parsed.all().size());
        assertNotNull("a retired question must still be readable by id",
                parsed.byId("STRUCT_03"));
        assertTrue(parsed.byId("STRUCT_03").retired);

        assertEquals(1, parsed.forTrigger("STRUCTURE_CANDIDATE").size());
        assertEquals("STRUCT_02", parsed.forTrigger("STRUCTURE_CANDIDATE").get(0).id);
    }

    @Test
    public void questionsAreNotRetiredUnlessTheFileSaysSo() throws Exception {
        JSONArray array = new JSONArray().put(
                new JSONObject()
                        .put("id", "ATTN_01")
                        .put("text", "最吸引你注意的是谁")
                        .put("targetSignal", "perceived_salience")
                        .put("options", new JSONArray().put("人声").put("鼓"))
                        .put("triggers", new JSONArray().put("NOVELTY_HIGH")));
        QuestionBank parsed = QuestionBank.fromJson(array);
        assertFalse(parsed.byId("ATTN_01").retired);
        assertEquals(1, parsed.forTrigger("NOVELTY_HIGH").size());
    }
}
