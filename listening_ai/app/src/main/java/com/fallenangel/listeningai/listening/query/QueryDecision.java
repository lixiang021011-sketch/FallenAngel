package com.fallenangel.listeningai.listening.query;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * What the layer decided, including when it decided to be quiet.
 *
 * A decision not to ask carries a reason code for the same reason the rest of
 * this project does: "it did not ask" and "it could not ask" look identical from
 * outside, and the second one is a bug report.
 */
public final class QueryDecision {
    public final boolean shouldAsk;
    public final QueryType queryType;
    /** Which uncertainty this is about, in words, for the record. */
    public final String targetUncertainty;
    public final List<QueryOption> options;
    public final double priority;
    public final double expectedValue;
    public final String reasonCode;

    public QueryDecision(boolean shouldAsk, QueryType queryType, String targetUncertainty,
                         List<QueryOption> options, double priority, double expectedValue,
                         String reasonCode) {
        this.shouldAsk = shouldAsk;
        this.queryType = queryType == null ? QueryType.NONE : queryType;
        this.targetUncertainty = targetUncertainty == null ? "" : targetUncertainty;
        this.options = options == null ? Collections.emptyList() : new ArrayList<>(options);
        this.priority = priority;
        this.expectedValue = expectedValue;
        this.reasonCode = reasonCode == null ? "" : reasonCode;
    }

    public static QueryDecision no(String reasonCode) {
        return new QueryDecision(false, QueryType.NONE, "", Collections.emptyList(), 0, 0,
                reasonCode);
    }

    @Override
    public String toString() {
        return (shouldAsk ? queryType + " priority=" + String.format(java.util.Locale.US, "%.2f", priority)
                : "no (" + reasonCode + ")");
    }
}
