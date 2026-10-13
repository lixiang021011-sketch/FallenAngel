package com.fallenangel.listeningai.listening.query;

import com.fallenangel.listeningai.listening.PauseQuestion;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * The decision, in words.
 *
 * The policy speaks in types and confidences because that is what can be argued
 * with; a person needs a sentence and three buttons. Keeping the two apart is
 * what makes a cloud model a drop-in replacement for this class later - the
 * decision does not change when the phrasing does.
 *
 * Names for the candidate types live here rather than in the policy so that
 * everything the app says out loud is in one place, and so that a type nobody
 * has a word for is shown as it came instead of being dropped.
 */
public final class TemplateQuestionRenderer {
    private static final Map<String, String> NAMES = new HashMap<>();

    static {
        String[][] pairs = {
                // The local rules' own vocabulary, kept identical to what the
                // pause-triggered questions already say.
                {"density_up", "编曲密度增加"}, {"density_down", "编曲变薄、留出空间"},
                {"anticipation", "低音提前进入（抢在拍前）"}, {"fill", "出现 fill / 密集过门"},
                {"pattern", "鼓型发生变化"}, {"timbre", "音色变化 / 新乐器进入"},
                {"harmony", "和声发生变化"}, {"level_up", "整体音量上升（段落推进）"},
                {"level_down", "整体音量下降（收束或留白）"}, {"section", "进入了新的段落"},
                // The names the query layer's spec uses.
                {"guitar_entry", "吉他进入"}, {"section_change", "整体段落变化"},
                {"drum_density_up", "鼓变密"}, {"drum_fill", "鼓的变化 / 过门"},
                {"bass_entry", "贝斯进入"}, {"vocal_entry", "人声进入"},
                // The answers that are not candidates.
                {"none", PauseQuestion.NONE_LABEL},
        };
        for (String[] pair : pairs) NAMES.put(pair[0], pair[1]);
    }

    public PauseQuestion render(QueryDecision decision, long targetSample, int bar, int beat,
                                int subdivision, String sectionLabel) {
        if (decision == null || !decision.shouldAsk) {
            throw new IllegalArgumentException("nothing was decided, so there is nothing to say: "
                    + (decision == null ? "no decision" : decision.reasonCode));
        }
        List<PauseQuestion.Candidate> candidates = new ArrayList<>();
        for (QueryOption option : decision.options) {
            candidates.add(new PauseQuestion.Candidate(option.id,
                    name(option, decision.queryType), 0, ""));
        }
        if (candidates.isEmpty()) {
            candidates.add(new PauseQuestion.Candidate("none", PauseQuestion.NONE_LABEL, 0, ""));
        } else if (!candidates.get(candidates.size() - 1).id.equals("none")) {
            // Every question offers the way out, whatever the policy listed.
            candidates.add(new PauseQuestion.Candidate("none", PauseQuestion.NONE_LABEL, 0, ""));
        }
        return new PauseQuestion(question(decision, candidates), targetSample, sectionLabel,
                bar, beat, subdivision, candidates);
    }

    /** The sentence, which for a confirmation is about the claim it names. */
    private String question(QueryDecision decision, List<PauseQuestion.Candidate> candidates) {
        switch (decision.queryType) {
            case ATTENTION_SOURCE:
                return "刚才你更注意到哪一种变化？";
            case SALIENCE:
                return "刚才这里有值得注意的变化吗？";
            case EVENT_CONFIRMATION:
                return "刚才这里主要是「" + candidates.get(0).label + "」吗？";
            default:
                return "刚才这里是什么变化？";
        }
    }

    /**
     * What to call an option.
     *
     * The yes and no of a yes-or-no question read differently depending on what
     * was asked: "有 / 没有" for whether something mattered, "是 / 不是" for
     * whether it was the thing named. One word cannot do both, and getting it
     * wrong is the kind of thing a person notices immediately.
     */
    private static String name(QueryOption option, QueryType asked) {
        boolean confirmation = asked == QueryType.EVENT_CONFIRMATION;
        switch (option.id) {
            case "yes": return confirmation ? "是" : "有";
            case "no": return confirmation ? "不是" : "没有";
            case "unsure": return "不确定";
            case "none": return PauseQuestion.NONE_LABEL;
            default: break;
        }
        String known = NAMES.get(option.id);
        return known != null ? known : option.label;
    }
}
