package com.fallenangel.listeningai.listening;

import com.fallenangel.listeningai.listening.PauseQuestion.Candidate;

import java.util.ArrayList;
import java.util.List;

/**
 * What the listener is allowed to be offered, after the model has answered.
 *
 * Two things go wrong with a small model's answers, and both were measured on this
 * app's own log before this existed:
 *
 *   - the same fact three times. "鼓型在5拍时变化", "鼓型的相似度低" and "鼓型在小节2
 *     结束时发生变化" arrived as three buttons for one phenomenon, and a listener
 *     choosing between them was choosing a wording. The same happened as "强低频
 *     提前进入" and "强低频起得较早";
 *   - words the vocabulary does not have, which is the other half of the same
 *     problem: a label that is anything cannot be counted with the rest.
 *
 * Neither is a reason to throw the answer away. Both are reasons to clean it before
 * it reaches a person.
 *
 * Kept away from Android so the rules can be tried against the actual answers that
 * caused them, which is what the tests do.
 */
public final class CandidateFilter {
    /**
     * The subjects a music event can be about.
     *
     * Two labels naming the same subject are the same phenomenon for the purpose of
     * a question: nobody is being asked to choose between "the drums did something"
     * said twice. Deliberately short - a longer list would start merging things that
     * are genuinely different, and over-merging is the worse failure here because
     * it silently removes an option the listener wanted.
     */
    private static final String[] SUBJECTS = {
            "鼓型", "鼓点", "底鼓", "军鼓", "过门", "fill", "填充",
            "贝斯", "低音", "低频", "吉他", "人声", "和声", "旋律", "力度", "音量",
            "密度", "编排", "编曲", "段落", "停顿", "节奏"
    };

    /**
     * @param allowed the vocabulary, or {@link CandidateVocabulary#OPEN} for none
     */
    public static List<Candidate> clean(List<Candidate> raw, CandidateVocabulary allowed) {
        return clean(raw, allowed, null);
    }

    /**
     * The same, with the record of what has already been offered and taken.
     *
     * A label that has been put in front of this listener several times and never
     * once chosen is not offered again. That is the whole of what the memory does
     * to a card: no reweighting, no ranking, nothing that would need more than the
     * handful of answers that exist so far.
     */
    public static List<Candidate> clean(List<Candidate> raw, CandidateVocabulary allowed,
                                       AnswerMemory memory) {
        List<Candidate> out = new ArrayList<>();
        if (raw == null) return out;
        for (Candidate candidate : raw) {
            // "都不是" is not a claim about the music; it is the way out, and it is
            // never filtered or merged - a listener with no good option must always
            // have one.
            if ("none".equals(candidate.id)) {
                out.add(candidate);
                continue;
            }
            if (allowed != null && !allowed.allows(candidate.label)) continue;
            // The way out is exempt above; everything else has to have either been
            // taken at least once or not yet worn out its welcome.
            if (memory != null && memory.tiredOf(candidate.label)) continue;
            int same = indexOfSameSubject(out, candidate);
            if (same < 0) {
                out.add(candidate);
            } else if (candidate.confidence > out.get(same).confidence) {
                // Keep the more confident of two phrasings. Which one survives does
                // not matter much; that only one survives does.
                out.set(same, candidate);
            }
        }
        return out;
    }

    private static int indexOfSameSubject(List<Candidate> seen, Candidate candidate) {
        String subject = subjectOf(candidate.label);
        if (subject.isEmpty()) return -1;
        for (int i = 0; i < seen.size(); i++) {
            if (subject.equals(subjectOf(seen.get(i).label))) return i;
        }
        return -1;
    }

    private static String subjectOf(String label) {
        String text = label == null ? "" : label;
        for (String subject : SUBJECTS) {
            if (text.contains(subject)) {
                // "音量上升" and "力度变大" are one phenomenon said two ways, and the
                // subjects list is flat, so the aliases are folded onto one name
                // here rather than by listing them twice.
                if ("音量".equals(subject)) return "力度";
                return subject;
            }
        }
        return "";
    }

    private CandidateFilter() {
    }
}
