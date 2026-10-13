package com.fallenangel.listeningai.clip;

import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * What the model heard, in words.
 *
 * A tag column and a cosine distance are not something a listener can agree or
 * disagree with - and the point of this project is that they *can*, which makes
 * the model's output a claim to be answered rather than a number to be stored.
 * So the numbers are turned into a sentence here: the strongest tag with how
 * much of it there is, and how this moment compares with the last few seconds.
 *
 * The hedge is not decoration. The first real model answers at 0.2 to 0.6 for
 * the tag it likes best, and printing "jazz 0.24" as if it were a certainty is
 * how a guess starts being treated as a fact. The words are allowed to be
 * unsure, because the thing itself is.
 */
public final class StyleReading {
    /** What the model calls what it is hearing, in Chinese. Empty when it has no tags. */
    public final String style;
    /** The strongest tag's probability, 0..1. */
    public final double confidence;
    /** How unlike the recent past this moment is, 0..1. */
    public final double novelty;

    public StyleReading(String style, double confidence, double novelty) {
        this.style = style == null ? "" : style;
        this.confidence = Math.max(0, Math.min(1, confidence));
        this.novelty = Math.max(0, Math.min(1, novelty));
    }

    /** Read a model's answer. */
    public static StyleReading of(float[] tags, List<String> names, double novelty) {
        if (tags == null || tags.length == 0 || names == null || names.isEmpty()) {
            return new StyleReading("", 0, novelty);
        }
        int best = 0;
        for (int i = 1; i < tags.length && i < names.size(); i++) {
            if (tags[i] > tags[best]) best = i;
        }
        if (best >= names.size()) return new StyleReading("", 0, novelty);
        return new StyleReading(chinese(names.get(best)), tags[best], novelty);
    }

    /** Whether the model is saying anything worth showing. */
    public boolean said() {
        return !style.isEmpty() && confidence > 0.1;
    }

    /** For a pill: as short as it can be while still being a claim. */
    public String shortPhrase() {
        if (style.isEmpty()) return "";
        return (confidence >= 0.3 ? "像" : "偏") + style;
    }

    /** The whole thing, as the sentence a person would read. */
    public String sentence() {
        return claim() + "，" + change();
    }

    /** What kind of music this is, with the uncertainty left in. */
    public String claim() {
        if (style.isEmpty()) return "说不上像什么风格";
        if (confidence >= 0.5) return "听起来像" + style;
        if (confidence >= 0.3) return "有点像" + style;
        if (confidence > 0.1) return "大概偏" + style;
        return "说不上像什么风格";
    }

    /**
     * How this moment sits against the seconds before it.
     *
     * The bands are the ones the first real model actually produced: it called
     * a track switch 0.25 and steady music 0.02 to 0.05, so a line drawn at 0.08
     * is "nothing is happening" and one at 0.25 is "that was a change".
     */
    public String change() {
        if (novelty >= 0.25) return "和刚才比明显变了";
        if (novelty >= 0.08) return "和刚才比略有变化";
        return "和刚才比很稳定";
    }

    @Override
    public String toString() {
        return sentence();
    }

    /**
     * The tags in the language the questions are asked in.
     *
     * The model's own vocabulary is English and its own; leaving the common ones
     * untranslated would put "female vocalists" in the middle of a Chinese
     * question. Anything not in here is shown as it came, which is honest rather
     * than wrong.
     */
    private static final Map<String, String> CHINESE = new HashMap<>();

    static String chinese(String tag) {
        String known = CHINESE.get(tag);
        return known == null ? tag : known;
    }

    static {
        String[][] pairs = {
                {"rock", "摇滚"}, {"pop", "流行"}, {"alternative", "另类"},
                {"indie", "独立"}, {"electronic", "电子"}, {"female vocalists", "女声"},
                {"dance", "舞曲"}, {"00s", "00 年代"}, {"alternative rock", "另类摇滚"},
                {"jazz", "爵士"}, {"beautiful", "唯美"}, {"metal", "金属"},
                {"chillout", "氛围放松"}, {"male vocalists", "男声"},
                {"classic rock", "经典摇滚"}, {"soul", "灵魂乐"}, {"indie rock", "独立摇滚"},
                {"Mellow", "柔和"}, {"electronica", "电子乐"}, {"80s", "80 年代"},
                {"folk", "民谣"}, {"90s", "90 年代"}, {"chill", "舒缓"},
                {"instrumental", "器乐"}, {"punk", "朋克"}, {"oldies", "老歌"},
                {"blues", "布鲁斯"}, {"hard rock", "硬摇滚"}, {"ambient", "环境音"},
                {"acoustic", "原声"}, {"experimental", "实验"}, {"female vocalist", "女声"},
                {"guitar", "吉他"}, {"Hip-Hop", "嘻哈"}, {"70s", "70 年代"},
                {"party", "派对"}, {"country", "乡村"}, {"easy listening", "轻音乐"},
                {"sexy", "性感"}, {"catchy", "上口"}, {"funk", "放克"},
                {"electro", "电音"}, {"heavy metal", "重金属"}, {"Progressive rock", "前卫摇滚"},
                {"60s", "60 年代"}, {"rnb", "节奏布鲁斯"}, {"indie pop", "独立流行"},
                {"sad", "忧伤"}, {"House", "浩室"}, {"happy", "欢快"},
        };
        for (String[] pair : pairs) CHINESE.put(pair[0], pair[1]);
    }
}
