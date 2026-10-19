package com.fallenangel.overlaybeat;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

/**
 * Reads a FallenAngel Chart v2 file and reduces it to what this game can play.
 *
 * The chart side of FallenAngel is already finished - a score becomes a chart
 * with lanes, taps, holds, drags, flicks and slides. This game is deliberately
 * not that: one lane, taps only. So the reader's job is an explicit, countable
 * reduction rather than a faithful decode, and it reports what it dropped so the
 * reduction is never invisible.
 *
 * Chart v2 shape:
 *   { "metadata": { songName, songArtist, difficulty, bpm, offset, ... },
 *     "notes": [ { type, lane, time, duration, direction, wide, path } ],
 *     "events": [] }
 */
public final class ChartFile {
    /** Notes closer together than this on the single lane become one tap. */
    private static final double MERGE_SECONDS = 0.03;

    public final String songName;
    public final String songArtist;
    public final String difficulty;
    public final double bpm;
    public final double offset;
    /** Already reduced to the one lane this game plays. */
    public final List<ChartNote> notes;
    /** What the reduction cost, so the loss is visible rather than silent. */
    public final int droppedByLane;
    public final int mergedSimultaneous;
    public final int reducedToTap;

    private ChartFile(String songName, String songArtist, String difficulty, double bpm,
                      double offset, List<ChartNote> notes,
                      int droppedByLane, int mergedSimultaneous, int reducedToTap) {
        this.songName = songName;
        this.songArtist = songArtist;
        this.difficulty = difficulty;
        this.bpm = bpm;
        this.offset = offset;
        this.notes = notes;
        this.droppedByLane = droppedByLane;
        this.mergedSimultaneous = mergedSimultaneous;
        this.reducedToTap = reducedToTap;
    }

    /**
     * @param laneFilter lane to keep, or -1 to fold every lane onto the one lane.
     */
    public static ChartFile parse(String json, int laneFilter) throws JSONException {
        JSONObject root = new JSONObject(json);
        JSONObject meta = root.optJSONObject("metadata");
        String songName = meta == null ? "" : meta.optString("songName", "");
        String songArtist = meta == null ? "" : meta.optString("songArtist", "");
        String difficulty = meta == null ? "" : meta.optString("difficulty", "");
        double bpm = meta == null ? 0 : meta.optDouble("bpm", 0);
        double offset = meta == null ? 0 : meta.optDouble("offset", 0);

        JSONArray source = root.optJSONArray("notes");
        List<ChartNote> out = new ArrayList<>();
        int droppedByLane = 0;
        int merged = 0;
        int reduced = 0;

        if (source != null) {
            for (int i = 0; i < source.length(); i++) {
                JSONObject note = source.optJSONObject(i);
                if (note == null) continue;
                int lane = note.optInt("lane", 0);
                if (laneFilter >= 0 && lane != laneFilter) {
                    droppedByLane++;
                    continue;
                }
                double time = note.optDouble("time", 0) + offset;
                String type = note.optString("type", "tap");
                double duration = note.optDouble("duration", 0);

                ChartNote.Kind kind = ChartNote.Kind.TAP;
                if ("hold".equals(type) && duration > 0) {
                    kind = ChartNote.Kind.HOLD;
                } else if (!"tap".equals(type)) {
                    // drag / flick / slide all need a gesture this game does not
                    // have. They become taps, and are counted so the loss is visible.
                    reduced++;
                }

                ChartNote previous = out.isEmpty() ? null : out.get(out.size() - 1);
                if (previous != null && Math.abs(time - previous.time) < MERGE_SECONDS) {
                    // Two lanes a few milliseconds apart are one moment on one
                    // lane - a single finger cannot hit them separately.
                    merged++;
                    continue;
                }
                out.add(new ChartNote(time, 0, kind, false, duration));
            }
        }
        out.sort((a, b) -> Double.compare(a.time, b.time));
        return new ChartFile(songName, songArtist, difficulty, bpm, offset, out,
                droppedByLane, merged, reduced);
    }

    public double lastTime() {
        return notes.isEmpty() ? 0 : notes.get(notes.size() - 1).time;
    }
}
