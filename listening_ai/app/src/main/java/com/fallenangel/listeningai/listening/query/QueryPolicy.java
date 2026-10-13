package com.fallenangel.listeningai.listening.query;

/**
 * Whether to ask about this moment, and what to ask.
 *
 * An interface with one method, because the whole point of this layer is that
 * the decision is separable from the music understanding before it and the
 * wording after it: a better policy replaces this, an LLM planner eventually
 * replaces the renderer, and neither has to know about the other.
 */
public interface QueryPolicy {
    QueryDecision evaluate(MusicMoment moment, QueryContext context);
}
