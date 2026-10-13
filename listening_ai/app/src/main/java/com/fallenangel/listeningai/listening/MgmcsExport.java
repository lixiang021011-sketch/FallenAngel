package com.fallenangel.listeningai.listening;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * The document the desktop validator reads, written by the phone itself.

 * The phone is the only thing that knows what was tapped and when, so the export
 * is mostly a faithful copy of that: every calibration keeps the raw presses it
 * came from, both clocks on each press, and the two numbers a person settled on.
 * Nothing is inferred here - a grid is what somebody said it was, and the desktop
 * is where it gets checked against the music.

 * The same document can be rebuilt later from `events.jsonl` by
 * `mgmcs_validator/tools/from_listening_ai.py`, for recordings made before this
 * existed. The two must agree on the event names; if you change one, change both.
 */
public final class MgmcsExport {
    public static final String SCHEMA_VERSION = "0.1";
    public static final String GENERATED_BY = "listening_ai";

    private MgmcsExport() {
    }

    /**
     * One grid, as the desktop draws it.

     * `audioTimeSeconds` is in the song's own clock - zero is the first sample of
     * the file - because that is the clock the validator's timeline is in. The
     * raw presses carry both clocks, so a better reading of them remains possible
     * without anybody tapping again.
     */
    public static JSONObject calibration(String calibrationId, double audioTimeSeconds,
                                         double bpm, int beatsPerBar, String note,
                                         List<JSONObject> rawTaps) {
        JSONObject out = new JSONObject();
        try {
            out.put("calibrationId", calibrationId == null ? "" : calibrationId);
            out.put("audioTime", round(audioTimeSeconds, 3));
            out.put("bpm", round(bpm, 3));
            out.put("beatsPerBar", Math.max(1, beatsPerBar));
            out.put("beatUnit", 4);
            out.put("source", "human");
            out.put("note", note == null ? "" : note);
            JSONArray taps = new JSONArray();
            if (rawTaps != null) {
                for (JSONObject tap : rawTaps) taps.put(rawTap(tap));
            }
            out.put("rawTaps", taps);
        } catch (Exception ignored) {
            // JSONObject is a map; the only way this throws is out of memory.
        }
        return out;
    }

    /** One press of the two buttons, with both clocks it was recorded against. */
    private static JSONObject rawTap(JSONObject line) {
        JSONObject out = new JSONObject();
        try {
            out.put("tapIndex", line.optInt("tapIndex", -1));
            out.put("button", line.optString("button"));
            out.put("songTime", round(line.optDouble("songTime", Double.NaN), 3));
            out.put("captureTime", round(line.optDouble("audioTimeMs", Double.NaN) / 1000.0, 3));
            out.put("clientTime", line.optLong("clientTime", -1));
            out.put("sessionId", line.optInt("sessionId", -1));
        } catch (Exception ignored) {
        }
        return out;
    }

    /**
     * The moments worth drawing on the desktop's timeline.

     * Only the records that happened at a place in the music become events;
     * presses and bookkeeping stay where they are, inside the calibration they
     * belong to. A question and its answer are two lines in the log and one event
     * here, because on a timeline they are one moment.
     */
    public static List<JSONObject> events(List<JSONObject> records) {
        List<JSONObject> out = new ArrayList<>();
        Map<String, JSONObject> open = new HashMap<>();
        int counter = 0;
        for (JSONObject line : records) {
            if (line == null) continue;
            String kind = line.optString("kind");
            if ("ANCHOR_TAP".equals(kind) || "PRESS_SNAP".equals(kind)) continue;

            if ("BAR_ANCHOR".equals(kind)) {
                JSONObject payload = new JSONObject();
                put(payload, "choice", line.optString("choice"));
                put(payload, "meter", line.optInt("meter", 0));
                put(payload, "bpm", line.optDouble("bpm", 0));
                out.add(point("evt_" + String.format(java.util.Locale.US, "%03d", ++counter),
                        "bar_answer", timeOf(line, "songTime"), payload));
                continue;
            }

            // What the listener did to the music: left this song, came back to a
            // song they had already played, jumped somewhere inside one. A grid
            // cannot show any of that, and without it a switch looks like the
            // recording simply stopping.
            if ("SONG_CHANGE".equals(kind)) {
                JSONObject payload = new JSONObject();
                put(payload, "to", line.optString("to"));
                out.add(point("evt_" + String.format(java.util.Locale.US, "%03d", ++counter),
                        "song_change", timeOf(line, "songTime"), payload));
                continue;
            }
            if ("SONG_ENTER".equals(kind)) {
                JSONObject payload = new JSONObject();
                put(payload, "from", line.optString("from"));
                put(payload, "reentry", line.optBoolean("reentry"));
                put(payload, "playsSoFar", line.optInt("playsSoFar", 1));
                out.add(point("evt_" + String.format(java.util.Locale.US, "%03d", ++counter),
                        "song_enter", timeOf(line, "songTime"), payload));
                continue;
            }
            if ("SEEK".equals(kind)) {
                JSONObject payload = new JSONObject();
                put(payload, "from", line.optDouble("fromSeconds", -1));
                put(payload, "to", line.optDouble("toSeconds", -1));
                put(payload, "backwards", line.optBoolean("backwards"));
                out.add(point("evt_" + String.format(java.util.Locale.US, "%03d", ++counter),
                        "seek", timeOf(line, "songTime"), payload));
                continue;
            }

            // A question from the bank: ASKED, then ANSWERED or IGNORED.
            if (!line.has("kind") && "QUERY".equals(line.optString("type"))
                    && line.has("templateId")) {
                String key = "bank:" + line.optString("templateId") + ":" + line.optInt("songBar");
                if ("ASKED".equals(line.optString("state"))) {
                    JSONObject payload = new JSONObject();
                    put(payload, "templateId", line.optString("templateId"));
                    put(payload, "targetSignal", line.optString("targetSignal"));
                    put(payload, "songBar", line.optInt("songBar"));
                    put(payload, "text", line.optString("text"));
                    put(payload, "state", "asked");
                    put(payload, "answer", JSONObject.NULL);
                    JSONObject event = point(
                            "evt_" + String.format(java.util.Locale.US, "%03d", ++counter),
                            "attention_query", timeOf(line, "songSeconds"), payload);
                    open.put(key, event);
                    out.add(event);
                } else {
                    JSONObject event = open.get(key);
                    if (event == null) continue;
                    JSONObject payload = event.optJSONObject("payload");
                    boolean answered = "ANSWERED".equals(line.optString("state"));
                    put(payload, "state", answered ? "answered" : "ignored");
                    // An ignored question keeps a null answer: nobody answered it,
                    // and an empty string would read as an answer that was blank.
                    if (answered) put(payload, "answer", line.optString("answer", ""));
                }
                continue;
            }

            if ("QUERY".equals(kind)) {
                String key = "policy:" + line.optString("id");
                if ("AI_PROPOSED".equals(line.optString("state"))) {
                    JSONObject payload = new JSONObject();
                    put(payload, "queryType", line.optString("queryType"));
                    put(payload, "reasonCode", line.optString("reasonCode"));
                    put(payload, "targetUncertainty", line.optDouble("targetUncertainty", 0));
                    put(payload, "state", "asked");
                    put(payload, "answer", JSONObject.NULL);
                    JSONObject event = point(
                            "evt_" + String.format(java.util.Locale.US, "%03d", ++counter),
                            "query_policy", timeOf(line, "audioTime"), payload);
                    open.put(key, event);
                    out.add(event);
                } else {
                    JSONObject event = open.get(key);
                    if (event == null) continue;
                    put(event.optJSONObject("payload"), "state", "answered");
                    put(event.optJSONObject("payload"), "answer", line.optString("selectedOption"));
                }
                continue;
            }

            if ("STYLE".equals(kind)) {
                String key = "style:" + line.optString("id");
                if ("AI_PROPOSED".equals(line.optString("state"))) {
                    JSONObject payload = new JSONObject();
                    put(payload, "style", line.optString("style"));
                    put(payload, "confidence", line.optDouble("styleConfidence", 0));
                    put(payload, "phrase", line.optString("phrase"));
                    put(payload, "accepted", JSONObject.NULL);
                    JSONObject event = point(
                            "evt_" + String.format(java.util.Locale.US, "%03d", ++counter),
                            "style_reading", timeOf(line, "audioTime"), payload);
                    open.put(key, event);
                    out.add(event);
                } else {
                    JSONObject event = open.get(key);
                    if (event == null) continue;
                    put(event.optJSONObject("payload"), "accepted", line.optBoolean("accepted"));
                }
                continue;
            }

            // A ListeningEvent: the moment the listener stopped on, or the app did.
            if (line.has("targetMusicTime") && line.has("aiVersion")) {
                JSONObject payload = new JSONObject();
                put(payload, "state", line.optString("state"));
                put(payload, "chosen", line.optString("chosenLabel"));
                put(payload, "aiVersion", line.optString("aiVersion"));
                put(payload, "bar", line.optInt("bar", -1));
                put(payload, "beat", line.optInt("beat", -1));
                out.add(point("evt_" + String.format(java.util.Locale.US, "%03d", ++counter),
                        "pause_moment", timeOf(line, "songTime"), payload));
            }
        }
        return out;
    }

    /** The whole file. */
    public static JSONObject document(String songId, String title, String audioFileName,
                                      double durationSeconds, List<JSONObject> calibrations,
                                      List<JSONObject> records) {
        JSONObject out = new JSONObject();
        try {
            out.put("schemaVersion", SCHEMA_VERSION);
            out.put("generatedBy", GENERATED_BY);
            JSONObject song = new JSONObject();
            song.put("songId", songId == null ? "" : songId);
            song.put("title", title == null ? "" : title);
            song.put("audioFileName", audioFileName == null ? "" : audioFileName);
            if (durationSeconds > 0 && !Double.isInfinite(durationSeconds)) {
                song.put("durationSeconds", round(durationSeconds, 3));
            }
            out.put("song", song);
            JSONArray grids = new JSONArray();
            if (calibrations != null) {
                for (JSONObject calibration : calibrations) grids.put(calibration);
            }
            out.put("calibrations", grids);
            JSONArray events = new JSONArray();
            for (JSONObject event : events(records)) events.put(event);
            out.put("events", events);
        } catch (Exception ignored) {
        }
        return out;
    }

    /** One line for the readable log: what the export came to. */
    public static String summarise(JSONObject document) {
        if (document == null) return "导出失败：没有内容";
        JSONArray grids = document.optJSONArray("calibrations");
        JSONArray events = document.optJSONArray("events");
        return String.format(java.util.Locale.US, "%d 条网格，%d 个事件",
                grids == null ? 0 : grids.length(), events == null ? 0 : events.length());
    }

    private static JSONObject point(String eventId, String type, double audioTime,
                                    JSONObject payload) {
        JSONObject out = new JSONObject();
        try {
            out.put("eventId", eventId);
            out.put("audioTime", round(audioTime, 3));
            out.put("type", type);
            out.put("source", "mobile");
            out.put("payload", payload == null ? new JSONObject() : payload);
        } catch (Exception ignored) {
        }
        return out;
    }

    /** The song's clock for a line, preferring the field named. */
    private static double timeOf(JSONObject line, String field) {
        double value = line.optDouble(field, Double.NaN);
        if (Double.isNaN(value)) value = line.optDouble("songTime", Double.NaN);
        if (Double.isNaN(value)) value = line.optDouble("audioTime", 0);
        return value;
    }

    private static void put(JSONObject target, String key, Object value) {
        if (target == null) return;
        try {
            target.put(key, value);
        } catch (Exception ignored) {
        }
    }

    private static double round(double value, int places) {
        if (Double.isNaN(value) || Double.isInfinite(value)) return 0;
        double factor = Math.pow(10, places);
        return Math.round(value * factor) / factor;
    }
}
