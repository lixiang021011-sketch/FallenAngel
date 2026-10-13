package com.fallenangel.listeningai.listening.query;

/**
 * The kinds of question this layer is allowed to ask.
 *
 * Three, deliberately. Each one is a different shape of uncertainty, so each one
 * has a different cheapest way to resolve it: whether a moment mattered at all,
 * which of two nearly-equal explanations it was, or whether one explanation is
 * right. Anything else the system is unsure about is not yet a question worth a
 * person's attention.
 */
public enum QueryType {
    /** "Was there something worth noticing here?" - when change is heard but not understood. */
    SALIENCE,
    /** "Which of these did you notice?" - when two explanations are too close to call. */
    ATTENTION_SOURCE,
    /** "Was it this?" - when one explanation is the best guess but not a safe one. */
    EVENT_CONFIRMATION,
    /** Not asking. A decision has to be able to say so in the same vocabulary. */
    NONE
}
