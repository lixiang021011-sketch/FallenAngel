package com.fallenangel.listeningai.listening.query;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * Everything about whether now is a good time, none of it about the music.
 *
 * Split out from the moment on purpose: the same musical uncertainty is worth
 * asking about once and not four times, and the difference is entirely in here.
 */
public final class QueryContext {
    /** When the last question was asked, on the same clock as `nowMillis`. */
    public final long lastQueryTimeMs;
    public final List<String> recentlyAskedTypes;
    /** Consecutive questions closed without an answer. */
    public final int recentDismissCount;
    public final boolean userIsInteracting;
    public final boolean playbackActive;
    /** How many questions have been asked in the last minute. */
    public final int queriesLastMinute;

    public QueryContext(long lastQueryTimeMs, List<String> recentlyAskedTypes,
                        int recentDismissCount, boolean userIsInteracting,
                        boolean playbackActive) {
        this(lastQueryTimeMs, recentlyAskedTypes, recentDismissCount, userIsInteracting,
                playbackActive, 0);
    }

    public QueryContext(long lastQueryTimeMs, List<String> recentlyAskedTypes,
                        int recentDismissCount, boolean userIsInteracting,
                        boolean playbackActive, int queriesLastMinute) {
        this.lastQueryTimeMs = lastQueryTimeMs;
        this.recentlyAskedTypes = recentlyAskedTypes == null
                ? Collections.emptyList() : new ArrayList<>(recentlyAskedTypes);
        this.recentDismissCount = Math.max(0, recentDismissCount);
        this.userIsInteracting = userIsInteracting;
        this.playbackActive = playbackActive;
        this.queriesLastMinute = Math.max(0, queriesLastMinute);
    }

    /** Nothing has happened yet: the state a service starts in. */
    public static QueryContext fresh() {
        return new QueryContext(0, Collections.emptyList(), 0, false, true);
    }
}
