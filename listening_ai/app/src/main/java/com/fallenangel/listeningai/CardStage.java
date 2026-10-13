package com.fallenangel.listeningai;

/**
 * What the floating window is showing, and who is still waiting on it.
 *
 * The window is written by five different things - a question, a note about why
 * there is no question, a cloud answer arriving seconds later, the model's own
 * question, and what the model hears when the button is tapped - and until this
 * existed they all just wrote to it. Two bugs came out of that, and both were
 * the same bug: the card could not say what was on it.
 *
 * The first: after a question was answered the card still answered "a question"
 * when asked, because answering only hid it. So tapping the button opened the
 * question that had just been answered instead of what the model hears - and it
 * happened only sometimes, because a note shown in between happened to clear
 * that field.
 *
 * The second: a slow answer from the cloud, or a note's own timed tidying, would
 * act on whatever was on the card by then - which is how a question could end up
 * on top of something newer.
 *
 * Kept away from Android so that both can be tested as the rules they are.
 */
final class CardStage {
    /** The kinds of thing that can be on the card. */
    enum Content {
        /** Nothing has been shown since the service started. */
        NOTHING,
        /** "正在看…": what is coming next is not ready yet. */
        BUSY,
        /** A question with its choices. */
        QUESTION,
        /** Something said without asking anything. */
        MESSAGE,
        /** What the model hears, with its two buttons. */
        STYLE
    }

    private Content content = Content.NOTHING;
    private long generation;
    private boolean awaitingAnswer;

    /**
     * Put something on the card.
     *
     * @return a token for this showing: a later step that means to act on what
     *         it put up hands this back and finds out whether it is still up.
     */
    long show(Content what) {
        content = what;
        // A question, and the waiting before one, both mean somebody owes an
        // answer; anything else is the app talking and the listener listening.
        awaitingAnswer = what == Content.QUESTION || what == Content.BUSY;
        return ++generation;
    }

    /** Whether what this token showed is still what is on the card. */
    boolean stillShowing(long token) {
        return token == generation;
    }

    /** The listener answered, or the question was given up on. */
    void answered() {
        awaitingAnswer = false;
    }

    /** Whether a question is on the card and nobody has answered it. */
    boolean awaitingAnswer() {
        return awaitingAnswer;
    }

    /**
     * What a tap on the button should do.
     *
     * Yes means "show what the model hears": there is nothing waiting to be
     * answered, so the listener is asking the app a question rather than
     * answering one.
     */
    boolean tapShowsWhatItHears() {
        return !awaitingAnswer;
    }

    Content content() {
        return content;
    }
}
