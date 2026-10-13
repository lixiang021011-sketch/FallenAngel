package com.fallenangel.listeningai.listening.query;

import org.json.JSONArray;
import org.json.JSONObject;

/**
 * A question this layer decided to ask, and what came back.
 *
 * Two lines, appended and never rewritten, like every other record here. What
 * makes this one different is that the question's own reasoning is part of it:
 * which uncertainty it was about, which moment, which version of the policy, and
 * how much it expected the answer to be worth. Reanalysis later should be able
 * to ask "was asking here ever a good idea" - which is unanswerable from a log
 * that only kept the answers.
 */
public final class FeedbackRecord {
    /** Bumped when the policy's rules change: two versions are not the same data. */
    public static final String POLICY_VERSION = "query-policy-v1";
    public static final String KIND = "QUERY";
    public static final String STATE_PROPOSED = "AI_PROPOSED";
    public static final String STATE_CONFIRMED = "USER_CONFIRMED";

    private FeedbackRecord() {
    }

    public static String newId(long nowMillis) {
        return "f" + Long.toHexString(nowMillis);
    }

    public static JSONObject proposed(String id, QueryDecision decision, MusicMoment moment,
                                      double audioTimeSeconds, long nowMillis) {
        JSONObject line = new JSONObject();
        try {
            line.put("kind", KIND);
            line.put("state", STATE_PROPOSED);
            line.put("id", id);
            line.put("createdAt", nowMillis);
            line.put("momentId", moment == null ? "" : moment.momentId);
            line.put("queryType", decision.queryType.name());
            line.put("targetUncertainty", decision.targetUncertainty);
            line.put("reasonCode", decision.reasonCode);
            line.put("priority", round(decision.priority));
            line.put("expectedValue", round(decision.expectedValue));
            line.put("options", optionIds(decision));
            line.put("policyVersion", POLICY_VERSION);
            line.put("audioTime", round(audioTimeSeconds));
            line.put("barIndex", moment == null ? -1 : moment.barIndex);
            line.put("beatIndex", moment == null ? -1 : moment.beatIndex);
            line.put("subdivision", moment == null ? -1 : moment.subdivision);
            line.put("novelty", moment == null ? 0 : round(moment.noveltyScore));
            line.put("overallConfidence",
                    moment == null ? 0 : round(moment.overallConfidence));
        } catch (Exception ignored) {
        }
        return line;
    }

    public static JSONObject answered(String id, String selectedOption, boolean dismissed,
                                      long nowMillis) {
        JSONObject line = new JSONObject();
        try {
            line.put("kind", KIND);
            line.put("state", STATE_CONFIRMED);
            line.put("id", id);
            line.put("createdAt", nowMillis);
            line.put("answeredAtMs", nowMillis);
            line.put("dismissed", dismissed);
            line.put("selectedOption", dismissed || selectedOption == null
                    ? "" : selectedOption);
        } catch (Exception ignored) {
        }
        return line;
    }

    private static JSONArray optionIds(QueryDecision decision) {
        JSONArray out = new JSONArray();
        for (QueryOption option : decision.options) out.put(option.id);
        return out;
    }

    private static double round(double value) {
        return Math.round(value * 1000) / 1000.0;
    }
}
