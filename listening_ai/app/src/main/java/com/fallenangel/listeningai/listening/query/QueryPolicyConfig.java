package com.fallenangel.listeningai.listening.query;

import org.json.JSONObject;

/**
 * The numbers the policy is drawn with.
 *
 * All of them here rather than in the rules, because every one of them is a
 * guess about this listener and will be tuned by listening. The defaults are the
 * first guesses, and they say what they are for.
 */
public final class QueryPolicyConfig {
    /** Above this, the moment is understood well enough not to ask about it. */
    public final double highConfidenceThreshold;
    /** A single candidate below this is not worth confirming. */
    public final double minimumCandidateConfidence;
    /** Two candidates closer than this are a choice, not a ranking. */
    public final double uncertaintyMargin;
    /** What "something happened" means for a moment nobody can explain. */
    public final double noveltyThreshold;
    public final long cooldownMs;
    /** Consecutive ignored questions before this app stops asking for a while. */
    public final int dismissLimit;
    public final int maxQueriesPerMinute;

    public QueryPolicyConfig(double highConfidenceThreshold, double minimumCandidateConfidence,
                             double uncertaintyMargin, double noveltyThreshold,
                             long cooldownMs, int dismissLimit, int maxQueriesPerMinute) {
        this.highConfidenceThreshold = highConfidenceThreshold;
        this.minimumCandidateConfidence = minimumCandidateConfidence;
        this.uncertaintyMargin = uncertaintyMargin;
        this.noveltyThreshold = noveltyThreshold;
        this.cooldownMs = cooldownMs;
        this.dismissLimit = dismissLimit;
        this.maxQueriesPerMinute = maxQueriesPerMinute;
    }

    /** The first guesses: 0.8 understood, 0.35 the floor for a claim, 0.12 apart. */
    public static QueryPolicyConfig defaults() {
        return new QueryPolicyConfig(0.80, 0.35, 0.12, 0.20, 15_000, 2, 4);
    }

    /** Anything missing keeps the default: a config file is not a contract to break. */
    public static QueryPolicyConfig fromJson(JSONObject json) {
        QueryPolicyConfig base = defaults();
        if (json == null) return base;
        return new QueryPolicyConfig(
                json.optDouble("highConfidenceThreshold", base.highConfidenceThreshold),
                json.optDouble("minimumCandidateConfidence", base.minimumCandidateConfidence),
                json.optDouble("uncertaintyMargin", base.uncertaintyMargin),
                json.optDouble("noveltyThreshold", base.noveltyThreshold),
                json.optLong("cooldownMs", base.cooldownMs),
                json.optInt("dismissLimit", base.dismissLimit),
                json.optInt("maxQueriesPerMinute", base.maxQueriesPerMinute));
    }

    public JSONObject toJson() {
        JSONObject out = new JSONObject();
        try {
            out.put("highConfidenceThreshold", highConfidenceThreshold);
            out.put("minimumCandidateConfidence", minimumCandidateConfidence);
            out.put("uncertaintyMargin", uncertaintyMargin);
            out.put("noveltyThreshold", noveltyThreshold);
            out.put("cooldownMs", cooldownMs);
            out.put("dismissLimit", dismissLimit);
            out.put("maxQueriesPerMinute", maxQueriesPerMinute);
        } catch (Exception ignored) {
        }
        return out;
    }
}
