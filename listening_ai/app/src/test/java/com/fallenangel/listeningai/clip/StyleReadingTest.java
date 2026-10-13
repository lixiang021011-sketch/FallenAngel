package com.fallenangel.listeningai.clip;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import java.util.Arrays;
import java.util.Collections;

/**
 * A column of probabilities becoming something a person can answer.
 *
 * Two things matter here and neither is the arithmetic. The words have to stay
 * honest about how sure the model is - it answers at 0.2 to 0.6 for the tag it
 * likes best, so "听起来像爵士" and "大概偏爵士" are different claims - and a tag
 * nobody translated must not turn into a missing word in the middle of Chinese.
 */
public class StyleReadingTest {
    private static final java.util.List<String> NAMES =
            Arrays.asList("rock", "pop", "jazz", "female vocalists", "Shibuya-kei");

    private static float[] tags(float... values) {
        return values;
    }

    @Test
    public void theStrongestTagBecomesAChineseSentence() {
        StyleReading reading = StyleReading.of(tags(0.1f, 0.2f, 0.62f, 0f, 0f), NAMES, 0.03);
        assertEquals("爵士", reading.style);
        assertEquals("听起来像爵士，和刚才比很稳定", reading.sentence());
        assertTrue(reading.said());
        assertEquals("像爵士", reading.shortPhrase());
    }

    /** How sure it is decides which claim it is allowed to make. */
    @Test
    public void aWeakTagIsNotClaimedAsAFact() {
        assertEquals("有点像爵士，和刚才比很稳定",
                StyleReading.of(tags(0f, 0f, 0.35f, 0f, 0f), NAMES, 0.03).sentence());
        assertEquals("大概偏爵士，和刚才比很稳定",
                StyleReading.of(tags(0f, 0f, 0.18f, 0f, 0f), NAMES, 0.03).sentence());
        assertEquals("说不上像什么风格，和刚才比很稳定",
                StyleReading.of(tags(0f, 0f, 0.05f, 0f, 0f), NAMES, 0.03).sentence());
        assertFalse("nothing worth showing on the button",
                StyleReading.of(tags(0f, 0f, 0.05f, 0f, 0f), NAMES, 0.03).said());
        assertEquals("偏爵士", StyleReading.of(tags(0f, 0f, 0.2f, 0f, 0f), NAMES, 0.03)
                .shortPhrase());
    }

    /** The bands are the ones the first real model actually produced. */
    @Test
    public void howMuchItChangedIsSaidInWords() {
        assertEquals("和刚才比很稳定", change(0.02));
        assertEquals("和刚才比略有变化", change(0.10));
        assertEquals("和刚才比明显变了", change(0.25));
        assertEquals("和刚才比明显变了", change(1.0));
    }

    private static String change(double novelty) {
        return StyleReading.of(tags(0f, 0.5f, 0f, 0f, 0f), NAMES, novelty).change();
    }

    /** A model with no tags, or a tag with no translation. */
    @Test
    public void missingWordsAreHandledRatherThanDropped() {
        assertEquals("", StyleReading.of(null, NAMES, 0.1).style);
        assertEquals("", StyleReading.of(tags(), NAMES, 0.1).style);
        assertEquals("", StyleReading.of(tags(0.5f), Collections.emptyList(), 0.1).style);
        assertEquals("Shibuya-kei",
                StyleReading.of(tags(0f, 0f, 0f, 0f, 0.9f), NAMES, 0.03).style);
        StyleReading loud = StyleReading.of(tags(0.9f, 0f, 0f, 0f, 0f), NAMES, 0.03);
        assertEquals("听起来像摇滚，和刚才比很稳定", loud.sentence());
    }

    /** Confidence and novelty arrive as whatever the model made of them. */
    @Test
    public void theNumbersAreClampedToProbabilities() {
        StyleReading reading = new StyleReading("爵士", 4, -1);
        assertEquals(1.0, reading.confidence, 0);
        assertEquals(0.0, reading.novelty, 0);
    }
}
