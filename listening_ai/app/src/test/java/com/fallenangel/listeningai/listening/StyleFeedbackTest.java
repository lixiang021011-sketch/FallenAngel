package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import com.fallenangel.listeningai.clip.StyleReading;

import org.json.JSONObject;
import org.junit.Test;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

/**
 * A tag the model claimed, and whether the listener agreed.
 *
 * Both answers are kept, and a rejection is written exactly like an acceptance:
 * the places a tagger and a listener disagree are the data this whole thing is
 * for, and a log that quietly dropped them would leave only the easy half.
 */
public class StyleFeedbackTest {
    private static final StyleReading READING = new StyleReading("爵士", 0.62, 0.03);

    @Test
    public void theClaimIsWrittenInWordsAndAnswersToItStaySeparate() throws Exception {
        String id = StyleFeedback.newId(1_700_000_000_000L);
        JSONObject proposed = StyleFeedback.proposed(id, "models/musicnn_msd.onnx",
                42.83, READING, 1_700_000_000_000L);
        assertEquals(StyleFeedback.KIND, proposed.getString("kind"));
        assertEquals(StyleFeedback.STATE_PROPOSED, proposed.getString("state"));
        assertEquals("爵士", proposed.getString("style"));
        assertEquals("听起来像爵士，和刚才比很稳定", proposed.getString("phrase"));
        assertEquals(42.83, proposed.getDouble("audioTime"), 1e-9);
        assertEquals("models/musicnn_msd.onnx", proposed.getString("model"));

        JSONObject no = StyleFeedback.confirmed(id, false, 1_700_000_001_000L);
        assertEquals(StyleFeedback.STATE_CONFIRMED, no.getString("state"));
        assertEquals("the same claim, answered", id, no.getString("id"));
        assertEquals("rejected", no.getString("choice"));
        assertTrue(StyleFeedback.confirmed(id, true, 0).getString("choice").equals("accepted"));
    }

    /** Anything appended stays, in the order it happened. */
    @Test
    public void bothLinesLandInTheSameLog() throws Exception {
        File file = File.createTempFile("interactions", ".jsonl");
        file.delete();
        ListeningLog log = new ListeningLog(file);
        String id = StyleFeedback.newId(7);
        log.append(StyleFeedback.proposed(id, "m", 12.5, READING, 7));
        log.append(StyleFeedback.confirmed(id, true, 8));

        java.util.List<String> lines = Files.readAllLines(file.toPath(), StandardCharsets.UTF_8);
        assertEquals(2, lines.size());
        assertTrue(lines.get(0).contains("\"AI_PROPOSED\""));
        assertTrue(lines.get(1).contains("\"USER_CONFIRMED\""));
        file.delete();
    }
}
