package com.fallenangel.listeningai.listening;

import com.fallenangel.listeningai.clip.StyleReading;

import org.json.JSONObject;

/**
 * The listener agreeing or disagreeing with what the model heard.
 *
 * This is the cheapest useful label in the whole system: the model says what it
 * thinks the music is, the person looks at it and taps once. What it produces is
 * not a tag accuracy score - it is a record of where a music tagger and a
 * listener differ, time-anchored, from somebody who was actually listening.
 *
 * Written the same way as the other interactions: two lines, the claim and then
 * the answer, and neither is ever rewritten. A rejection is worth as much as an
 * acceptance and is kept the same way.
 */
public final class StyleFeedback {
    /** Distinguishes these lines from the pause-interaction records in the same file. */
    public static final String KIND = "STYLE";
    public static final String STATE_PROPOSED = "AI_PROPOSED";
    public static final String STATE_CONFIRMED = "USER_CONFIRMED";

    private StyleFeedback() {
    }

    public static String newId(long nowMillis) {
        return "s" + Long.toHexString(nowMillis);
    }

    /** What the model claimed, in words, at a time in the music. */
    public static JSONObject proposed(String id, String modelId, double atSeconds,
                                      StyleReading reading, long nowMillis) {
        JSONObject line = new JSONObject();
        try {
            line.put("kind", KIND);
            line.put("state", STATE_PROPOSED);
            line.put("id", id);
            line.put("createdAt", nowMillis);
            line.put("audioTime", round(atSeconds));
            line.put("model", modelId == null ? "" : modelId);
            line.put("style", reading.style);
            line.put("styleConfidence", round(reading.confidence));
            line.put("novelty", round(reading.novelty));
            line.put("phrase", reading.sentence());
        } catch (Exception ignored) {
        }
        return line;
    }

    /** And what the listener said about it. */
    public static JSONObject confirmed(String id, boolean accepted, long nowMillis) {
        JSONObject line = new JSONObject();
        try {
            line.put("kind", KIND);
            line.put("state", STATE_CONFIRMED);
            line.put("id", id);
            line.put("createdAt", nowMillis);
            line.put("choice", accepted ? "accepted" : "rejected");
        } catch (Exception ignored) {
        }
        return line;
    }

    private static double round(double value) {
        return Math.round(value * 1000) / 1000.0;
    }
}
