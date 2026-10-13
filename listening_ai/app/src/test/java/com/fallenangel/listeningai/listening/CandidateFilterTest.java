package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import com.fallenangel.listeningai.listening.PauseQuestion.Candidate;

import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.Test;

import java.util.Arrays;
import java.util.List;

/**
 * The answers that were actually bad, kept as tests.
 *
 * Every label in here came out of this app's own log on 2026-10-13. A rule written
 * against invented examples would have been written against the author's idea of
 * what a model says; these are what it said.
 */
public class CandidateFilterTest {
    private static final Candidate NONE = new Candidate("none", "都不是", 0, "");

    private static Candidate candidate(String label, double confidence) {
        return new Candidate("c" + label.hashCode(), label, confidence, "");
    }

    @Test
    public void threePhrasingsOfOneFactBecomeOneButton() {
        List<Candidate> raw = Arrays.asList(
                candidate("鼓型在5拍时变化", 0.6),
                candidate("鼓型的相似度低", 0.5),
                candidate("鼓型在小节2结束时发生变化", 0.4),
                NONE);

        List<Candidate> clean = CandidateFilter.clean(raw, CandidateVocabulary.OPEN);

        assertEquals(2, clean.size());
        assertEquals("鼓型在5拍时变化", clean.get(0).label);
        assertEquals("都不是", clean.get(1).label);
    }

    @Test
    public void theMoreConfidentOfTwoPhrasingsIsTheOneKept() {
        List<Candidate> raw = Arrays.asList(
                candidate("强低频提前进入", 0.4),
                candidate("强低频起得较早", 0.7),
                NONE);
        List<Candidate> clean = CandidateFilter.clean(raw, CandidateVocabulary.OPEN);
        assertEquals(2, clean.size());
        assertEquals("强低频起得较早", clean.get(0).label);
    }

    @Test
    public void differentFactsAreNotMerged() {
        // Measured together in one answer, and they are three different things: a
        // filter that swallowed any of them would be hiding an option the listener
        // might have wanted.
        List<Candidate> raw = Arrays.asList(
                candidate("编曲密度增加", 0.8),
                candidate("鼓型变化", 0.7),
                candidate("音量上升", 0.6),
                NONE);
        assertEquals(4, CandidateFilter.clean(raw, CandidateVocabulary.OPEN).size());
    }

    @Test
    public void twoWordsForTheSameThingAreOneButton() {
        // "音量上升" and "力度变大" are the same observation in two vocabularies, and
        // a card offering both makes the listener choose a wording.
        List<Candidate> raw = Arrays.asList(
                candidate("音量上升", 0.6),
                candidate("力度变大", 0.5),
                NONE);
        List<Candidate> clean = CandidateFilter.clean(raw, CandidateVocabulary.OPEN);
        assertEquals(2, clean.size());
        assertEquals("音量上升", clean.get(0).label);
    }

    @Test
    public void aLabelOutsideTheVocabularyIsNotOffered() throws Exception {
        CandidateVocabulary vocabulary = CandidateVocabulary.fromJson(vocabulary(
                "鼓点加密", "贝斯切分", "其他", "都不是"));
        List<Candidate> raw = Arrays.asList(
                candidate("鼓点加密", 0.8),
                candidate("编曲密度增加", 0.7),      // not in the list
                candidate("贝斯切分", 0.6),
                NONE);
        List<Candidate> clean = CandidateFilter.clean(raw, vocabulary);
        assertEquals(Arrays.asList("鼓点加密", "贝斯切分", "都不是"), labels(clean));
    }

    @Test
    public void theWayOutIsNeverFilteredOrMerged() throws Exception {
        CandidateVocabulary vocabulary = CandidateVocabulary.fromJson(vocabulary("鼓点加密"));
        List<Candidate> raw = Arrays.asList(candidate("贝斯切分", 0.9), NONE);
        List<Candidate> clean = CandidateFilter.clean(raw, vocabulary);
        assertEquals(1, clean.size());
        assertEquals("都不是", clean.get(0).label);
    }

    @Test
    public void aLabelTheListenerHasAlreadyRejectedIsNotOfferedAgain() {
        AnswerMemory memory = new AnswerMemory();
        for (int i = 0; i < 3; i++) {
            memory.learn(Arrays.asList("编曲密度增加", "都不是"), "都不是");
        }
        List<Candidate> raw = Arrays.asList(
                candidate("编曲密度增加", 0.8),
                candidate("鼓点加密", 0.7),
                NONE);
        List<Candidate> clean = CandidateFilter.clean(raw, CandidateVocabulary.OPEN, memory);
        assertEquals(Arrays.asList("鼓点加密", "都不是"), labels(clean));
    }

    @Test
    public void aLabelChosenOnceSurvivesBeingRejectedAfterwards() {
        AnswerMemory memory = new AnswerMemory();
        memory.learn(Arrays.asList("鼓点加密", "都不是"), "鼓点加密");
        for (int i = 0; i < 4; i++) memory.learn(Arrays.asList("鼓点加密", "都不是"), "都不是");
        List<Candidate> raw = Arrays.asList(candidate("鼓点加密", 0.7), NONE);
        assertEquals(2, CandidateFilter.clean(raw, CandidateVocabulary.OPEN, memory).size());
    }

    @Test
    public void anEmptyVocabularyAllowsEverything() throws Exception {
        CandidateVocabulary none = CandidateVocabulary.fromJson(vocabulary());
        assertTrue(none.isEmpty());
        assertTrue(none.allows("随便什么说法"));
        assertEquals("", none.instruction());
    }

    @Test
    public void theInstructionHandsOverTheListAsWritten() throws Exception {
        CandidateVocabulary vocabulary = CandidateVocabulary.fromJson(vocabulary("鼓点加密", "贝斯切分"));
        String instruction = vocabulary.instruction();
        assertTrue(instruction, instruction.contains("[鼓点加密 贝斯切分]"));
        assertTrue(instruction, instruction.contains("逐字"));
    }

    private static JSONObject vocabulary(String... labels) throws org.json.JSONException {
        JSONArray array = new JSONArray();
        for (String label : labels) array.put(label);
        return new JSONObject().put("version", "test").put("labels", array);
    }

    private static List<String> labels(List<Candidate> candidates) {
        List<String> out = new java.util.ArrayList<>();
        for (Candidate candidate : candidates) out.add(candidate.label);
        return out;
    }
}
