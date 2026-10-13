package com.fallenangel.listeningai.listening.query;

import com.fallenangel.listeningai.listening.SongLog;

import java.util.Locale;

/**
 * One line per moment considered, whether or not it became a question.
 *
 * The policy is only tunable if its refusals are as visible as its questions. A
 * dash stands in for a value that is not there, so a column can be read down the
 * file without counting fields.
 */
public final class QueryDump {
    private QueryDump() {
    }

    /**
     * @param songSeconds   where in the song the moment was
     * @param userResponse  what the listener chose, or "-" when not answered yet
     */
    public static String oneLine(double songSeconds, MusicMoment moment, QueryDecision decision,
                                 String userResponse) {
        EventCandidate top = moment == null ? null : moment.top();
        EventCandidate second = moment == null ? null : moment.second();
        return String.format(Locale.US,
                "%s bar=%d beat=%d moment=%s top=%s second=%s novelty=%.2f ask=%s type=%s"
                        + " priority=%.2f reason=%s answer=%s",
                SongLog.clock(songSeconds),
                moment == null ? -1 : moment.barIndex + 1,
                moment == null ? -1 : moment.beatIndex + 1,
                moment == null ? "-" : moment.momentId,
                candidate(top), candidate(second),
                moment == null ? 0 : moment.noveltyScore,
                decision != null && decision.shouldAsk,
                decision == null ? QueryType.NONE : decision.queryType,
                decision == null ? 0 : decision.priority,
                decision == null ? "-" : decision.reasonCode,
                userResponse == null || userResponse.isEmpty() ? "-" : userResponse);
    }

    private static String candidate(EventCandidate candidate) {
        return candidate == null
                ? "-"
                : String.format(Locale.US, "%s@%.2f", candidate.type, candidate.confidence);
    }
}
