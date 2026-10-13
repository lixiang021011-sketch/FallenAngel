package com.fallenangel.listeningai.clip;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import com.fallenangel.listeningai.listening.PauseQuestion;

import org.json.JSONObject;
import org.junit.Test;

import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * What gets sent, and what comes back.
 *
 * The network is replaced by a fake, so everything that can be wrong about this
 * - the audio that is attached, the facts that go with it, the parsing of an
 * answer, the way out when the answer is useless - is checked without a key and
 * without a call.
 */
public class ExplanationClientTest {
    private static final int RATE = 44100;

    private static final class Fake implements ExplanationClient.Transport {
        String url, body;
        Map<String, String> headers;
        String answer;

        Fake(String answer) {
            this.answer = answer;
        }

        @Override public String post(String url, Map<String, String> headers, String body) {
            this.url = url;
            this.headers = headers;
            this.body = body;
            return answer;
        }
    }

    private static String chat(String content) throws Exception {
        return new JSONObject()
                .put("choices", new org.json.JSONArray().put(
                        new JSONObject().put("message",
                                new JSONObject().put("content", content))))
                .toString();
    }

    private static short[] clip() {
        short[] pcm = new short[RATE * 2];
        for (int i = 0; i < pcm.length; i++) {
            pcm[i] = (short) (Math.sin(2 * Math.PI * 300 * i / RATE) * 8000);
        }
        return pcm;
    }

    @Test
    public void aRemarkIsOneSentenceAndNotASetOfChoices() throws Exception {
        Fake fake = new Fake(chat("  鼓点变密了  \n"));
        ExplanationClient client = new ExplanationClient(
                "https://example.test/v1/chat/completions", "sk-test", "a-model", fake);

        short[] pcm = clip();
        String said = client.remark(pcm, 0, pcm.length, RATE);

        assertEquals("鼓点变密了", said);
        // The audio still travels - that is the whole point of asking a model that
        // can hear - but there is no json contract: a remark is not a question.
        assertTrue(fake.body.contains("input_audio"));
        assertTrue(!fake.body.contains("json_object"));
    }

    @Test
    public void aRemarkThatCameBackWrappedComesBackBare() throws Exception {
        Fake fake = new Fake(chat("「贝斯进来了」"));
        ExplanationClient client = new ExplanationClient("u", "k", null, fake);
        assertEquals("贝斯进来了", client.remark(clip(), 0, clip().length, RATE));
    }

    @Test
    public void anAnswerCutOffByTheTokenLimitStillYieldsTheCandidatesItFinished() {
        // Copied from this app's own logcat: the model ran past the server's token
        // ceiling mid-string. Before this, a usable answer and no answer looked
        // identical from the listener's side - the card fell back to the rules and
        // nothing said why.
        String truncated = "{\"candidates\":["
                + "{\"id\":\"c1\",\"label\":\"鼓型变化\",\"confidence\":0.35,\"evidence\":\"电子\"},"
                + "{\"id\":\"c2\",\"label\":\"小节 5 第 4 拍的鼓型\",\"confidence\":0.3,\"evidence\":\"电子\"},"
                + "{\"id\":\"c3\",\"label\":\"鼓型相似度变化\",\"confidence\":0.3,\"evidence\":\"电子\"},"
                + "{\"id\":\"c4\",\"label\":\"小节 5 的鼓型\",\"confidence\":0.3,\"evidence\":\"电子\"},"
                + "{\"id\":\"c5\",\"label\":\"鼓型发生变化（电";

        java.util.List<com.fallenangel.listeningai.listening.PauseQuestion.Candidate> out =
                ExplanationClient.salvage(truncated);

        assertEquals(4, out.size());
        assertEquals("鼓型变化", out.get(0).label);
        assertEquals("小节 5 的鼓型", out.get(3).label);
        assertEquals(0.35, out.get(0).confidence, 1e-9);
    }

    @Test
    public void aTruncatedAnswerIsUsedRatherThanThrownAway() throws Exception {
        String truncated = "{\"candidates\":["
                + "{\"id\":\"c1\",\"label\":\"底鼓提前\",\"confidence\":0.6,\"evidence\":\"拍前\"},"
                + "{\"id\":\"c2\",\"label\":\"贝斯进";
        Fake fake = new Fake(chat(truncated));
        ExplanationClient client = new ExplanationClient("u", "k", null, fake);

        java.util.List<com.fallenangel.listeningai.listening.PauseQuestion.Candidate> out =
                client.explain(clip(), 0, clip().length, RATE, "facts");

        // The one complete candidate, plus "都不是" which is always appended.
        assertEquals(2, out.size());
        assertEquals("底鼓提前", out.get(0).label);
    }

    @Test
    public void nothingUsefulInTheAnswerIsStillAFailure() {
        assertTrue(ExplanationClient.salvage("{\"candidates\":[{\"id\":\"c1\",\"lab").isEmpty());
        assertTrue(ExplanationClient.salvage("the model said no").isEmpty());
    }

    @Test
    public void aRemarkThatAsksAQuestionIsNotARemark() throws Exception {
        Fake fake = new Fake(chat("这里是换段了吗？"));
        ExplanationClient client = new ExplanationClient("u", "k", null, fake);
        try {
            client.remark(clip(), 0, clip().length, RATE);
            org.junit.Assert.fail("a question is not a remark - it is a question with "
                    + "worse manners, and the caller must be able to fall back");
        } catch (Exception expected) {
            // The app says what the local reading says instead, or says nothing.
        }
    }

    @Test
    public void anEmptyRemarkIsAFailureNotAnEmptyLine() throws Exception {
        Fake fake = new Fake(chat("   "));
        ExplanationClient client = new ExplanationClient("u", "k", null, fake);
        try {
            client.remark(clip(), 0, clip().length, RATE);
            org.junit.Assert.fail("an empty remark should have been refused");
        } catch (Exception expected) {
            // Said nothing is the one thing a remark may not do; the caller falls
            // back to what the local reading says rather than showing a blank line.
        }
    }

    @Test
    public void theClipAndTheFactsBothGoWithTheQuestion() throws Exception {
        Fake fake = new Fake(chat(new JSONObject()
                .put("candidates", new org.json.JSONArray().put(new JSONObject()
                        .put("id", "c1").put("label", "底鼓提前").put("confidence", 0.7)
                        .put("evidence", "拍前 60 毫秒有低频起音")))
                .toString()));
        ExplanationClient client = new ExplanationClient(
                "https://example.test/v1/chat/completions", "sk-test", "a-model", fake);

        short[] pcm = clip();
        List<PauseQuestion.Candidate> candidates = client.explain(pcm, 0, pcm.length, RATE,
                "bpm 128, section B, 本地规则: 密度上升 0.4");

        assertEquals("https://example.test/v1/chat/completions", fake.url);
        assertEquals("Bearer sk-test", fake.headers.get("Authorization"));
        assertTrue("audio is attached", fake.body.contains("input_audio"));
        assertTrue("as a wav", fake.body.contains("\"format\":\"wav\""));
        assertTrue("and the facts go with it", fake.body.contains("bpm 128"));
        assertTrue("the model is named", fake.body.contains("a-model"));
        assertEquals(2, candidates.size());
        assertEquals("底鼓提前", candidates.get(0).label);
        assertEquals(0.7, candidates.get(0).confidence, 1e-9);
        assertEquals("拍前 60 毫秒有低频起音", candidates.get(0).evidence);
    }

    @Test
    public void theWayOutIsAlwaysOffered() throws Exception {
        Fake fake = new Fake(chat(new JSONObject()
                .put("candidates", new org.json.JSONArray().put(new JSONObject()
                        .put("id", "c1").put("label", "鼓型变化").put("confidence", 0.5)))
                .toString()));
        ExplanationClient client = new ExplanationClient("u", "k", null, fake);

        List<PauseQuestion.Candidate> candidates =
                client.explain(clip(), 0, clip().length, RATE, "facts");
        PauseQuestion.Candidate last = candidates.get(candidates.size() - 1);
        assertEquals("a model that is never allowed to be wrong needs a way to say so",
                PauseQuestion.NONE_LABEL, last.label);
        assertEquals(0.0, last.confidence, 1e-9);
    }

    @Test
    public void anAnswerWithNothingUsableIsAnError() throws Exception {
        Fake fake = new Fake(chat("{\"candidates\":[]}"));
        ExplanationClient client = new ExplanationClient("u", "k", null, fake);
        try {
            client.explain(clip(), 0, clip().length, RATE, "facts");
            assertTrue("an empty answer is not a question", false);
        } catch (IOException expected) {
            assertTrue(expected.getMessage().contains("candidates"));
        }
    }

    @Test
    public void aModelThatIsNotConfiguredIsNotCalled() {
        assertFalse(ExplanationClient.configured("", "key"));
        assertFalse(ExplanationClient.configured("https://x", ""));
        assertTrue(ExplanationClient.configured("https://x", "key"));
    }

    @Test
    public void atMostFourProposalsAndNeverAMissingLabel() throws Exception {
        List<JSONObject> items = new ArrayList<>();
        for (int i = 0; i < 8; i++) {
            items.add(new JSONObject().put("id", "c" + i).put("label", "候选" + i)
                    .put("confidence", 0.5));
        }
        items.add(new JSONObject().put("id", "empty").put("label", "  "));
        org.json.JSONArray array = new org.json.JSONArray();
        for (JSONObject item : items) array.put(item);
        Fake fake = new Fake(chat(new JSONObject().put("candidates", array).toString()));

        List<PauseQuestion.Candidate> candidates =
                new ExplanationClient("u", "k", null, fake)
                        .explain(clip(), 0, clip().length, RATE, "facts");
        assertEquals("four proposals plus 都不是", 5, candidates.size());
        for (PauseQuestion.Candidate candidate : candidates) {
            assertFalse(candidate.label.trim().isEmpty());
        }
    }
}
