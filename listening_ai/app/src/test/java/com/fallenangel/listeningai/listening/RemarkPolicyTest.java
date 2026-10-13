package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

/**
 * When the app may talk without being asked.
 *
 * The questions have a budget already - a gate, a gap, a bank that cannot pick
 * twice in four bars. Remarks had none, because until now they did not exist.
 * Whether a listener wants the app talking during music is not something this
 * class can answer; what it can do is make the amount of talking a number
 * instead of a matter of taste, so the answer can be measured.
 */
public class RemarkPolicyTest {
    private static final long GAP = 45_000;

    @Test
    public void silentWhileTheMusicIsStopped() {
        RemarkPolicy policy = new RemarkPolicy(GAP);
        assertFalse(policy.due(0, false, false));
    }

    @Test
    public void silentWhileSomethingIsWaitingToBeAnswered() {
        RemarkPolicy policy = new RemarkPolicy(GAP);
        policy.spoke(0);
        assertFalse(policy.due(GAP * 2, true, true));
    }

    @Test
    public void speaksWhenTheMusicIsPlayingAndNobodyIsWaiting() {
        RemarkPolicy policy = new RemarkPolicy(GAP);
        assertTrue(policy.due(0, true, false));
    }

    @Test
    public void notAgainBeforeTheGapIsUp() {
        RemarkPolicy policy = new RemarkPolicy(GAP);
        policy.spoke(1_000);
        assertFalse(policy.due(1_000 + GAP - 1, true, false));
        assertTrue(policy.due(1_000 + GAP, true, false));
    }

    @Test
    public void theGapIsWhatChangesTheAmountOfTalking() {
        RemarkPolicy chatty = new RemarkPolicy(10_000);
        RemarkPolicy quiet = new RemarkPolicy(180_000);
        chatty.spoke(0);
        quiet.spoke(0);
        // One knob, and the difference between "companion" and "nag" is whether
        // this is ten seconds or three minutes.
        assertTrue(chatty.due(60_000, true, false));
        assertFalse(quiet.due(60_000, true, false));
    }
}
