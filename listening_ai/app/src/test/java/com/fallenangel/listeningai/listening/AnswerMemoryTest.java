package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import com.fallenangel.listeningai.listening.PauseQuestion.Candidate;

import java.util.Arrays;
import java.util.List;

/**
 * The record read back: what has been offered, and what has ever been taken.
 *
 * The rule it exists for is the smallest useful one - a label offered several times
 * and never once chosen stops being offered - and most of these tests are about the
 * edge that rule would get wrong if it were written carelessly.
 */
public class AnswerMemoryTest {
    private static final Candidate NONE = new Candidate("none", "都不是", 0, "");

    @Test
    public void aFreshMemoryHasNoOpinions() {
        AnswerMemory memory = new AnswerMemory();
        assertFalse(memory.tiredOf("鼓点加密"));
        assertEquals(0, memory.size());
    }

    @Test
    public void twoRejectionsAreNotYetAPattern() {
        AnswerMemory memory = new AnswerMemory();
        memory.learn(Arrays.asList("鼓点加密", "都不是"), "都不是");
        memory.learn(Arrays.asList("鼓点加密", "都不是"), "都不是");
        assertFalse(memory.tiredOf("鼓点加密"));
        assertEquals(2, memory.offeredCount("鼓点加密"));
    }

    @Test
    public void offeredThreeTimesAndNeverTakenIsTired() {
        AnswerMemory memory = new AnswerMemory();
        for (int i = 0; i < 3; i++) {
            memory.learn(Arrays.asList("编曲密度增加", "都不是"), "都不是");
        }
        assertTrue(memory.tiredOf("编曲密度增加"));
        assertEquals(Arrays.asList("编曲密度增加"), memory.tiredLabels());
    }

    @Test
    public void oneAcceptanceKeepsALabelForever() {
        AnswerMemory memory = new AnswerMemory();
        memory.learn(Arrays.asList("鼓点加密", "都不是"), "鼓点加密");
        for (int i = 0; i < 9; i++) {
            memory.learn(Arrays.asList("鼓点加密", "都不是"), "都不是");
        }
        // Offered ten times, taken once: still a candidate. This app is short of
        // evidence, not of opinions.
        assertEquals(10, memory.offeredCount("鼓点加密"));
        assertEquals(1, memory.chosenCount("鼓点加密"));
        assertFalse(memory.tiredOf("鼓点加密"));
    }

    @Test
    public void theChoiceAlsoCountsAsAnOffer() {
        AnswerMemory memory = new AnswerMemory();
        memory.learn(Arrays.asList("贝斯切分"), "贝斯切分");
        assertEquals(1, memory.offeredCount("贝斯切分"));
        assertEquals(1, memory.chosenCount("贝斯切分"));
    }

    @Test
    public void theWayOutSurvivesTheMemoryBeingTiredOfIt() {
        // Suppose a listener never once picks "都不是": the count says it was
        // offered five times and never taken, which is exactly the shape of a label
        // that has failed. It still has to stay, because it is not a candidate - it
        // is the way out of the question, and the filter exempts it by id rather
        // than by whether it has been useful.
        AnswerMemory memory = new AnswerMemory();
        for (int i = 0; i < 5; i++) memory.learn(Arrays.asList("都不是", "鼓点加密"), "鼓点加密");
        assertTrue(memory.tiredOf("都不是"));

        List<Candidate> clean = CandidateFilter.clean(
                Arrays.asList(NONE), CandidateVocabulary.OPEN, memory);
        assertEquals(1, clean.size());
        assertEquals("都不是", clean.get(0).label);
    }
}
