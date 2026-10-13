package com.fallenangel.listeningai;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

/**
 * What the floating window is showing, and what a tap on it should do.
 *
 * Both rules here were bugs. The window used to answer "there is a question" for
 * as long as the session lasted, because answering a question only hid it - so
 * tapping the button reopened the question just answered instead of showing what
 * the model hears, sometimes, depending on which kind of thing had been shown in
 * between. And a slow answer from the cloud, or a note's own timed tidying,
 * would act on whatever happened to be on the window by then.
 */
public class CardStageTest {
    @Test
    public void aQuestionOnScreenIsWaitingToBeAnswered() {
        CardStage stage = new CardStage();
        stage.show(CardStage.Content.QUESTION);
        assertTrue(stage.awaitingAnswer());
        assertFalse("a tap must not replace a question nobody has answered",
                stage.tapShowsWhatItHears());
    }

    @Test
    public void onceItIsAnsweredATapAsksTheAppSomething() {
        CardStage stage = new CardStage();
        stage.show(CardStage.Content.QUESTION);
        stage.answered();
        assertFalse(stage.awaitingAnswer());
        assertTrue("answered: the button shows what the model hears again",
                stage.tapShowsWhatItHears());
    }

    /** The wait before a question is still somebody owing an answer. */
    @Test
    public void theWaitingBeforeAQuestionIsNotAnAnswer() {
        CardStage stage = new CardStage();
        stage.show(CardStage.Content.BUSY);
        assertTrue(stage.awaitingAnswer());
        assertFalse(stage.tapShowsWhatItHears());
    }

    @Test
    public void aNoteIsNotSomethingToAnswer() {
        CardStage stage = new CardStage();
        stage.show(CardStage.Content.MESSAGE);
        assertFalse(stage.awaitingAnswer());
        assertTrue(stage.tapShowsWhatItHears());
    }

    @Test
    public void aLateAnswerDoesNotActOnSomethingNewer() {
        CardStage stage = new CardStage();
        long asked = stage.show(CardStage.Content.BUSY);
        stage.show(CardStage.Content.STYLE);
        assertFalse("the cloud cannot put its question on top of this",
                stage.stillShowing(asked));
    }

    @Test
    public void whatIsStillThereIsStillCurrent() {
        CardStage stage = new CardStage();
        long asked = stage.show(CardStage.Content.BUSY);
        assertTrue(stage.stillShowing(asked));
        stage.answered();
        assertTrue("answering changes who owes an answer, not what is shown",
                stage.stillShowing(asked));
    }
}
