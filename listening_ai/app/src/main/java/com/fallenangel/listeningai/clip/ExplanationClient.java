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
            connection.setConnectTimeout(10_000);
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
        messages.put(new JSONObject().put("role", "system").put("content", SYSTEM));
        messages.put(new JSONObject().put("role", "user").put("content", content));
        JSONObject body = new JSONObject()
                .put("model", model)
                .put("messages", messages)
                .put("response_format", new JSONObject().put("type", "json_object"));

        String answer = transport.post(endpoint,
                java.util.Map.of("Authorization", "Bearer " + apiKey,
                        "Content-Type", "application/json"),
                new String(body.toString().getBytes(StandardCharsets.UTF_8), StandardCharsets.UTF_8));
        List<PauseQuestion.Candidate> out = parse(answer);
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
        String text = contentOf(answer);
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
     * What a remark is asked for: one sentence, not a set of choices.
     *
     * The ban on "感觉/氛围" and on asking anything is not style advice. A remark
     * that asks a question is a question with worse manners, and a remark made of
     * mood words cannot be compared with the last one - the whole use of having
     * the app talk is that what it says about the music can be read back later.
     */
    private static final String REMARK_SYSTEM =
            "用一句不超过 12 个字的中文，说出现在这段音乐里正在发生什么。"
                    + "不要提问，不要给建议，不要用「感觉」「氛围」「情绪」这类词，"
                    + "只说一件你听到的具体的事，例如「鼓点变密了」「贝斯进来了」。"
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
                    + "必须能被这段音频支持，不要编造本地事实里没有提到的乐器或事件；"
                    + "confidence 是 0 到 1 的把握，不要都写同一个数；"
                    + "只输出 JSON，形如 "
                    + "{\"candidates\":[{\"id\":\"c1\",\"label\":\"…\","
                    + "\"confidence\":0.6,\"evidence\":\"一句话说明依据\"}]}";
}
