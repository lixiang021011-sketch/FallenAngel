package com.fallenangel.listeningai.listening.query;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/**
 * Whether to ask about this moment, as a short list of things that can stop it.
 *
 * The order is the design. Uncertainty first, because a moment that is
 * understood has nothing to ask about however interesting it was. Then whether an
 * answer could change anything, because a question that cannot is worse than no
 * question. Then the ordinary manners: not the same question again, not now, not
 * while somebody is busy. The last step picks the cheapest kind of question that
 * would settle this uncertainty, because the point of asking is to get an answer,
 * not to be interesting.
 *
 * No natural language here. This decides; the renderer speaks.
 */
public final class LocalQueryPolicy implements QueryPolicy {
    /** A choice between two real explanations says more than a yes. */
    private static final double CHOICE_UTILITY = 1.0;
    private static final double CONFIRMATION_UTILITY = 0.6;

    private final QueryPolicyConfig config;

    public LocalQueryPolicy(QueryPolicyConfig config) {
        this.config = config == null ? QueryPolicyConfig.defaults() : config;
    }

    public QueryPolicyConfig config() {
        return config;
    }

    @Override
    public QueryDecision evaluate(MusicMoment moment, QueryContext context) {
        if (moment == null || context == null) return QueryDecision.no("NO_MOMENT");

        // 1. Is there any real uncertainty?
        if (moment.overallConfidence >= config.highConfidenceThreshold) {
            return QueryDecision.no("CONFIDENT");
        }
        EventCandidate top = moment.top();
        EventCandidate second = moment.second();

        // 3. Could an answer change a label? Nothing to explain is nothing to
        // ask about, however much the music changed.
        if (top == null) return QueryDecision.no("NO_HYPOTHESIS");

        // The last step, decided here because step 4 has to know what would be
        // asked in order to recognise that it was asked before.
        QueryType wanted = cheapestType(moment, top, second);
        if (wanted == QueryType.NONE) return QueryDecision.no("TOO_WEAK");

        // 4. The same question again is not a new question.
        if (context.recentlyAskedTypes.contains(wanted.name())) {
            return QueryDecision.no("ASKED_RECENTLY");
        }

        // 5. Is now a moment somebody can answer in?
        if (!context.playbackActive) return QueryDecision.no("NOT_PLAYING");
        if (context.userIsInteracting) return QueryDecision.no("BUSY");
        if (context.lastQueryTimeMs > 0
                && moment.endTimeMs - context.lastQueryTimeMs < config.cooldownMs) {
            return QueryDecision.no("COOLDOWN");
        }
        if (context.recentDismissCount >= config.dismissLimit) {
            return QueryDecision.no("DISMISSED");
        }
        if (context.queriesLastMinute >= config.maxQueriesPerMinute) {
            return QueryDecision.no("TOO_MANY");
        }

        return ask(wanted, moment, top, second);
    }

    /**
     * The cheapest question that would settle this.
     *
     * A choice between two explanations when they are close enough that the
     * ranking is noise; otherwise, when something changed and nothing explains
     * it, whether it mattered at all; otherwise confirmation of the one claim
     * that is neither strong enough to trust nor weak enough to ignore.
     */
    private QueryType cheapestType(MusicMoment moment, EventCandidate top,
                                   EventCandidate second) {
        if (second != null && top.confidence >= config.minimumCandidateConfidence
                && top.confidence - second.confidence < config.uncertaintyMargin) {
            return QueryType.ATTENTION_SOURCE;
        }
        if (moment.noveltyScore >= config.noveltyThreshold) return QueryType.SALIENCE;
        if (top.confidence >= config.minimumCandidateConfidence) {
            return QueryType.EVENT_CONFIRMATION;
        }
        return QueryType.NONE;
    }

    /** Build the decision, with the value it is expected to have. */
    private QueryDecision ask(QueryType type, MusicMoment moment, EventCandidate top,
                              EventCandidate second) {
        List<QueryOption> options = new ArrayList<>();
        String target;
        String reason;
        double uncertainty;
        double utility;
        if (type == QueryType.ATTENTION_SOURCE) {
            options.add(new QueryOption(top.type, top.type));
            options.add(new QueryOption(second.type, second.type));
            options.add(new QueryOption("none", "none"));
            double gap = Math.max(0, top.confidence - second.confidence);
            uncertainty = clamp(1 - gap / Math.max(1e-6, config.uncertaintyMargin));
            target = "两种解释分不清：" + top.type + " / " + second.type;
            reason = "ATTENTION_SOURCE_CLOSE_CALL";
            utility = CHOICE_UTILITY;
        } else if (type == QueryType.SALIENCE) {
            options.add(new QueryOption("yes", "yes"));
            options.add(new QueryOption("no", "no"));
            uncertainty = clamp(1 - top.confidence);
            target = String.format(Locale.US, "听到了变化但说不清是什么（%.2f）",
                    moment.noveltyScore);
            reason = "SALIENCE_UNEXPLAINED_CHANGE";
            utility = CHOICE_UTILITY;
        } else {
            options.add(new QueryOption(top.type, top.type));
            options.add(new QueryOption("yes", "yes"));
            options.add(new QueryOption("no", "no"));
            options.add(new QueryOption("unsure", "unsure"));
            uncertainty = clamp(1 - top.confidence);
            target = "只有一个不太确定的解释：" + top.type;
            reason = "EVENT_CONFIRMATION_WEAK_CLAIM";
            utility = CONFIRMATION_UTILITY;
        }

        // The four factors the spec asks for. Availability is 1 by the time the
        // code reaches here - the reasons it would be 0 have already returned -
        // and stays in the product so that the score keeps its shape.
        double importance = clamp(Math.max(moment.noveltyScore, top.confidence));
        double availability = 1;
        double expectedValue = clamp(utility * importance);
        double priority = clamp(uncertainty * importance * utility * availability);
        return new QueryDecision(true, type, target, options, priority, expectedValue, reason);
    }

    private static double clamp(double value) {
        return Math.max(0, Math.min(1, value));
    }
}
