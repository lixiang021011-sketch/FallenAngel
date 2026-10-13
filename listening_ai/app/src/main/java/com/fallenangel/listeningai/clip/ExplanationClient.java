package com.fallenangel.listeningai.clip;

import com.fallenangel.listeningai.listening.PauseQuestion;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Base64;
import java.util.List;

/**
 * Asking a reasoning model why the listener stopped.
 *
 * The local analysis can say what changed; it cannot say what it means. "The
 * drum figure changed" is a measurement, and the listener's answer to it was
 * twice "都不是" - so the explanations are asked for from something that knows
 * more, and the audio goes with the question rather than a description of it.
 *
 * Everything that talks to the network is behind {@link Transport}: the request
 * that is built, and the answer that is parsed, are testable without one, and
 * the live call is the smallest possible part.
 *
 * The model is asked for a json object of candidates and nothing else. It is
 * never asked to be right: a candidate list the listener rejects is data too,
 * and that is the whole point of asking.
 */
public final class ExplanationClient {
    /** The seam. One method, so a test can answer without a network. */
    public interface Transport {
        String post(String url, java.util.Map<String, String> headers, String body)
                throws IOException;
    }

    public static final String DEFAULT_MODEL = "gpt-4o-mini";
    private static final String NONE = PauseQuestion.NONE_LABEL;

    private final String endpoint, apiKey, model;
    private final Transport transport;

    public ExplanationClient(String endpoint, String apiKey, String model, Transport transport) {
        this.endpoint = endpoint;
        this.apiKey = apiKey;
        this.model = model == null || model.isEmpty() ? DEFAULT_MODEL : model;
        this.transport = transport;
    }

    /** Whether this is configured well enough to be worth calling. */
    public static boolean configured(String endpoint, String apiKey) {
        return endpoint != null && !endpoint.isEmpty() && apiKey != null && !apiKey.isEmpty();
    }

    /**
     * One POST, with nothing between the request and the socket.
     *
     * The whole of the live network path is here so that everything else can be
     * tested without it: what is sent, what comes back, and what happens when
     * the answer is useless.
     */
    public static String post(String url, java.util.Map<String, String> headers, String body)
            throws IOException {
        java.net.HttpURLConnection connection =
                (java.net.HttpURLConnection) new java.net.URL(url).openConnection();
        try {
            connection.setRequestMethod("POST");
            // Short, because a model that is not there is not going to be there in a
            // moment either: ten seconds of "正在问模型…" per question, and two
            // questions after one seek, is twenty seconds of a card saying nothing.
            // The read timeout stays long - generation is slow and that is fine.
            connection.setConnectTimeout(2_500);
            // The answer has to be waited out: a hosted model answers in a second
            // or two, and a model on the desk has to encode the clip first, so the
            // same question takes twenty. Thirty seconds was chosen when the only
            // models were somebody else's.
            connection.setReadTimeout(90_000);
            connection.setDoOutput(true);
            for (java.util.Map.Entry<String, String> header : headers.entrySet()) {
                connection.setRequestProperty(header.getKey(), header.getValue());
            }
            byte[] bytes = body.getBytes(StandardCharsets.UTF_8);
            connection.setFixedLengthStreamingMode(bytes.length);
            try (java.io.OutputStream out = connection.getOutputStream()) {
                out.write(bytes);
            }
            int code = connection.getResponseCode();
            java.io.InputStream in = code >= 400
                    ? connection.getErrorStream() : connection.getInputStream();
            String text = in == null ? "" : new String(in.readAllBytes(), StandardCharsets.UTF_8);
            if (code >= 400) {
                throw new IOException("HTTP " + code + ": " + text);
            }
            return text;
        } finally {
            connection.disconnect();
        }
    }

    /**
     * @param pcm      44.1 kHz mono, the seconds before the pause
     * @param facts    what the local analysis already knows, as one block of text
     * @param rate     the sample rate of pcm
     */
    public List<PauseQuestion.Candidate> explain(short[] pcm, int offset, int count, int rate,
                                                 String facts)
            throws IOException, org.json.JSONException {
        return explain(pcm, offset, count, rate, facts, "");
    }

    /**
     * The same question, with one more rule for the model to follow.
     *
     * The extra text is the closed vocabulary when there is one: the labels it may
     * use, verbatim. Passed in rather than held here, because the list is an asset
     * of the app and can be replaced without this class knowing.
     */
    public List<PauseQuestion.Candidate> explain(short[] pcm, int offset, int count, int rate,
                                                 String facts, String extraRule)
            throws IOException, org.json.JSONException {
        byte[] wav = Wav.of(Wav.resample(pcm, offset, count, rate, 16000), 16000);
        String audio = Base64.getEncoder().encodeToString(wav);

        JSONArray content = new JSONArray();
        content.put(new JSONObject().put("type", "text").put("text", facts));
        content.put(new JSONObject()
                .put("type", "input_audio")
                .put("input_audio", new JSONObject()
                        .put("data", audio)
                        .put("format", "wav")));
        JSONArray messages = new JSONArray();
        String system = SYSTEM + (extraRule == null || extraRule.isEmpty() ? "" : extraRule);
        messages.put(new JSONObject().put("role", "system").put("content", system));
        messages.put(new JSONObject().put("role", "user").put("content", content));
        JSONObject body = new JSONObject()
                .put("model", model)
                .put("messages", messages)
                .put("response_format", new JSONObject().put("type", "json_object"));

        String answer = transport.post(endpoint,
                java.util.Map.of("Authorization", "Bearer " + apiKey,
                        "Content-Type", "application/json"),
                new String(body.toString().getBytes(StandardCharsets.UTF_8), StandardCharsets.UTF_8));
        // Read the words out of the envelope first, so that a cut-off answer is
        // salvaged as the text the model wrote rather than as the envelope it
        // arrived in - the envelope is valid JSON and salvaging it would find
        // nothing, silently turning this back into the failure it exists to fix.
        String text;
        try {
            text = contentOf(answer);
        } catch (org.json.JSONException notChatShaped) {
            text = answer;
        }
        List<PauseQuestion.Candidate> out;
        try {
            out = parseText(text);
        } catch (org.json.JSONException broken) {
            // A model that answers at length runs past the server's token ceiling,
            // and what arrives is valid up to where it stopped. Throwing all of it
            // away because the last candidate is unfinished turns a usable answer
            // into no answer - which is exactly what happened on 2026-10-13: four
            // candidates, cut off mid-string, card fell back to the rules, and from
            // the listener's side the model looked like it had gone quiet.
            out = salvage(text);
            if (out.isEmpty()) throw broken;
        }
        if (out.isEmpty()) {
            throw new IOException("the answer had no usable candidates");
        }
        // Added here, not asked for: a model that is never allowed to be wrong
        // has to be given a way to say it is.
        out.add(new PauseQuestion.Candidate("none", NONE, 0, ""));
        return out;
    }

    /**
     * Read the candidates out of a chat-style answer.
     *
     * "都不是" is appended here rather than asked for, because a model that is
     * never allowed to be wrong has to offer a way to say it is.
     */
    static List<PauseQuestion.Candidate> parse(String answer) throws org.json.JSONException {
        return parseText(contentOf(answer));
    }

    /** The same, for the text the model wrote, with the envelope already off. */
    static List<PauseQuestion.Candidate> parseText(String text) throws org.json.JSONException {
        JSONObject parsed = new JSONObject(text);
        JSONArray array = parsed.optJSONArray("candidates");
        List<PauseQuestion.Candidate> out = new ArrayList<>();
        if (array != null) {
            for (int i = 0; i < array.length() && out.size() < 4; i++) {
                JSONObject item = array.optJSONObject(i);
                if (item == null) continue;
                String label = item.optString("label", "").trim();
                if (label.isEmpty()) continue;
                out.add(new PauseQuestion.Candidate(
                        item.optString("id", "m" + i), label,
                        item.optDouble("confidence", 0.5),
                        item.optString("evidence", "")));
            }
        }
        return out;
    }

    /** The assistant's own words out of a chat-shaped answer. */
    static String contentOf(String answer) throws org.json.JSONException {
        JSONObject root = new JSONObject(answer);
        JSONArray choices = root.optJSONArray("choices");
        if (choices != null && choices.length() > 0) {
            JSONObject message = choices.getJSONObject(0).optJSONObject("message");
            if (message != null) return message.optString("content", "");
        }
        return root.optString("content", "");
    }

    /**
     * The candidates out of an answer that was cut off part way through.
     *
     * Reads the array by hand, one object at a time, keeping every object whose
     * closing brace arrived. The last one is almost always the casualty - a label
     * with no end, or a quote still open - and it is dropped rather than repaired,
     * because a repaired candidate would be a candidate the model did not write.
     */
    static List<PauseQuestion.Candidate> salvage(String text) {
        List<PauseQuestion.Candidate> out = new ArrayList<>();
        if (text == null) return out;
        int array = text.indexOf('[');
        if (array < 0) return out;
        int at = array + 1;
        while (at < text.length() && out.size() < 4) {
            int open = text.indexOf('{', at);
            if (open < 0) break;
            int close = matchingBrace(text, open);
            if (close < 0) break;          // this is the one that was cut off
            try {
                JSONObject item = new JSONObject(text.substring(open, close + 1));
                String label = item.optString("label", "").trim();
                if (!label.isEmpty()) {
                    out.add(new PauseQuestion.Candidate(
                            item.optString("id", "m" + out.size()), label,
                            item.optDouble("confidence", 0.5),
                            item.optString("evidence", "")));
                }
            } catch (org.json.JSONException halfWritten) {
                // Skip it and keep going: one unusable object is not a reason to
                // drop the ones around it.
            }
            at = close + 1;
        }
        return out;
    }

    /**
     * Where the brace that closes the one at {@code open} is, or -1 if it never
     * arrives.
     *
     * Strings are tracked, because a label may contain a brace and a quote may be
     * escaped - and getting that wrong would silently cut a candidate in half.
     */
    private static int matchingBrace(String text, int open) {
        int depth = 0;
        boolean inString = false;
        for (int i = open; i < text.length(); i++) {
            char c = text.charAt(i);
            if (inString) {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}') {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    /**
     * What a remark is asked for: one sentence, not a set of choices.
     *
     * The ban on "感觉/氛围" and on asking anything is not style advice. A remark
     * that asks a question is a question with worse manners, and a remark made of
     * mood words cannot be compared with the last one - the whole use of having
     * the app talk is that what it says about the music can be read back later.
     */
    private static final String REMARK_SYSTEM =
            "用一句不超过 12 个字的中文，说出一件他自己听不出来、或者叫不出名字的事："
                    + "和刚才那几秒相比这一段里最明显的变化，"
                    + "或者这里面用到的一个手法，用行话说它，例如 shuffle、切分重音、半音上行、切音。"
                    + "不要提问，不要给建议，不要复述他自己就听得见的东西（音量、密度、鼓点变快这类），"
                    + "不要用「感觉」「氛围」「情绪」。"
                    + "直接说那句话，不要引号，不要 JSON，不要解释。";

    /**
     * One sentence about the clip, for the app to say on its own.
     *
     * The same request as {@link #explain} with a different instruction: the
     * audio travels the same way, the transport is the same, the failure path is
     * the same - there is no json contract and no candidates, because a remark is
     * not a claim to be agreed with. It is the app talking.
     */
    public String remark(short[] pcm, int offset, int count, int rate)
            throws IOException, org.json.JSONException {
        byte[] wav = Wav.of(Wav.resample(pcm, offset, count, rate, 16000), 16000);
        String audio = Base64.getEncoder().encodeToString(wav);

        JSONArray content = new JSONArray();
        content.put(new JSONObject()
                .put("type", "input_audio")
                .put("input_audio", new JSONObject()
                        .put("data", audio)
                        .put("format", "wav")));
        JSONArray messages = new JSONArray();
        messages.put(new JSONObject().put("role", "system").put("content", REMARK_SYSTEM));
        messages.put(new JSONObject().put("role", "user").put("content", content));
        JSONObject body = new JSONObject().put("model", model).put("messages", messages);

        String answer = transport.post(endpoint,
                java.util.Map.of("Authorization", "Bearer " + apiKey,
                        "Content-Type", "application/json"),
                body.toString());
        String said = tidy(contentOf(answer));
        if (said.isEmpty()) throw new IOException("the remark came back empty");
        // A remark that asks something is a question with worse manners: it
        // arrives without the card, the choices or the music being held, so the
        // listener is asked something they cannot answer properly and the app
        // never learns what they said. Refused here rather than tolerated, so the
        // caller falls back to the local reading, which at least does not ask.
        if (said.endsWith("？") || said.endsWith("?")) {
            throw new IOException("a remark came back as a question: " + said);
        }
        return said;
    }

    /**
     * A remark as it should appear on the card: one line, bare.
     *
     * The model is asked for all of this; this is what happens when it does it
     * anyway, and a card showing 「"鼓点变密了"」 or three lines where one was
     * asked for looks like the app is broken rather than talkative.
     */
    static String tidy(String text) {
        String clean = text == null ? "" : text.trim();
        for (String quote : new String[] {"\"", "「", "“", "『"}) {
            if (clean.startsWith(quote)) clean = clean.substring(quote.length()).trim();
        }
        for (String quote : new String[] {"\"", "」", "”", "』"}) {
            if (clean.endsWith(quote)) clean = clean.substring(0, clean.length() - quote.length()).trim();
        }
        int newline = clean.indexOf('\n');
        if (newline >= 0) clean = clean.substring(0, newline).trim();
        return clean;
    }

    /** What the model is told to do, in the terms this project actually has. */
    private static final String SYSTEM =
            "你在帮一个人研究「为什么某个音乐瞬间会让人停下来听」。"
                    + "我会给你一段几秒的音频，以及本地分析已经测到的事实。"
                    + "请给出 3 到 5 个候选解释：这个人刚才可能注意到的是哪一件事。"
                    + "要求："
                    + "每个解释都要短（不超过 12 个字），是音乐上的具体事情，"
                    + "不要笼统地说「变化」「感觉」；"
                    + "只给 3 条，不要更多；"
                    + "evidence 不超过 12 个字；"
                    + "必须能被这段音频支持，不要编造本地事实里没有提到的乐器或事件；"
                    + "confidence 是 0 到 1 的把握，不要都写同一个数；"
                    + "只输出 JSON，形如 "
                    + "{\"candidates\":[{\"id\":\"c1\",\"label\":\"…\","
                    + "\"confidence\":0.6,\"evidence\":\"一句话说明依据\"}]}";
}
