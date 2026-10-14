package com.fallenangel.listeningai.listening;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;
import java.util.Locale;

/**
 * A beat grid computed offline, read from a .mgmcs.json.
 *
 * The app tracks beats from the audio, and tracking is guesswork: a phase found by
 * letting it settle, a period corrected by how the error grows, a meter assumed and
 * sometimes a half bar out. The Workbench files hold the same thing decided
 * elsewhere - with more of the song to look at, and with a person able to correct it
 * - so when one exists for the song being played, it is used instead.
 *
 * The shape of these files is not one shape, and pretending otherwise is how the app
 * ended up using the worst reading in the file:
 *
 *   beats                拍点, exact times. The best case.
 *   downbeats / barLines 小节线 only. madmom reports these and so does the
 *                        Workbench, and the beats between them are a division of the
 *                        bar, which is right for a bar whose tempo is steady. Bar
 *                        lines may be plain numbers or objects carrying a `time`.
 *   neither              只有 bpm 和 audioTime 的条目（human_snapped 的 tap、只有
 *                        sixteenthLock 的 workbench）：那是锚点，不是网格，不用。
 *
 * And `audioTime` is checked rather than believed:
 *
 *   audioTime = 该读法第一条网格线的音频秒（网格原点）
 *
 * measured 2026-10-14 over all 220 files in mgmcs_validator/static/docs: every
 * reading whose list is in song time has audioTime == list[0] exactly (librosa 220,
 * BeatThis 158, madmom 220, workbench 47, mapperatorinator-filled/erased 54), and
 * the one reading that breaks it is mapperatorinator, where audioTime ==
 * validRanges[0].start instead (156 of 156) while its `beats` start somewhere else
 * entirely.
 *
 * A reading may write both lists, and `audioTime` can only be the first line of one
 * of them - so which one it is the first line *of* is what says which list that entry
 * is written in. Anchored to the beats, the beats are the grid and the bar lines only
 * place the bar. Anchored to the bar lines, they are the grid and the beats are a
 * division of the bar. Anchored to neither, the entry is in a coordinate of its own
 * and is refused: mapperatorinator's `beats` is refused every time, its `barLines`
 * (which are its measures) is what may be used.
 *
 * What else is read, and what is deliberately not:
 *
 *   beatsPerBar      the meter, which the app otherwise assumes
 *   reading          which reading this is. Missing -> calibrationId is used, and
 *                    said so in the notes: the drums batch writes only the latter.
 *   health           a gate. ok is used as-is, suspect is dropped, and anything else
 *                    (caution, derived, missing) is used but marked: see `caution`.
 *   suspectReasons   the validator's own words, carried into the notes
 *   locallyDowngraded  whether the validator threw this reading out over part of the
 *                    song, with `downgraded: [{start, end, reason}]` for where. A
 *                    reading marked that way is looked at after the ones that are not,
 *                    and where it is used anyway, the stretch it was doubted over says
 *                    so on screen: being doubted in one place is not being wrong.
 *   audioFileName    which audio a reading was made from. `drums.wav` on an entry
 *                    whose song is the mix means the reading is from the stem, which
 *                    is marked rather than rejected - same time axis, other material.
 *   validRanges      a local observer's windows: outside them it says nothing
 *   title, duration  matching, and only matching
 *
 * Not read: the events. A grid is a coordinate system and an event hangs off it;
 * importing both at once would make a mistake in either look like a mistake in both.
 *
 * Kept away from Android: it is a parser and a choice of readings.
 */
public final class WorkbenchGrid {
    /** How far the first grid line may sit from `audioTime` and still be the same coordinate. */
    static final double ANCHOR_TOLERANCE_SECONDS = 0.002;

    /**
     * Readings from best to worst, and closed on purpose.
     *
     * A name that is not here is refused with a note instead of being used on a
     * guess: a new reading from the Workbench is a thing to agree on, not something
     * to be quietly picked up as if it were one of these.
     */
    private static final String[] RANK = {
            "workbench",
            "human_snapped",
            "human_unquantized",
            "BeatThis",
            "madmom",
            "mapperatorinator-filled",
            "mapperatorinator-erased",
            "mapperatorinator",
            "librosa",
    };

    public final String title;
    public final double durationSeconds;
    /** Which reading was used, for the record. */
    public final String reading;
    public final String generatedBy;
    /**
     * Which audio that reading was made from: "mix", "drums", or whatever the file
     * said. Empty when nothing in the file says.
     */
    public final String audioSource;
    /** The shape the grid was written in: beats, beats+downbeats, downbeats, barLines. */
    public final String shape;
    public final int beatsPerBar;
    /** Usable, but not as a clean reading: why is in {@link #notes}. */
    public final boolean caution;
    /** What was refused on the way to this reading, and anything odd about it. */
    public final List<String> notes;
    public final double bpm;

    /** Beat times in seconds, ascending, one grid stitched from its pieces. */
    private final double[] beats;
    /** Which beat is the first of the bar: 0 unless a bar line says otherwise. */
    private final int barOffset;
    /** What a local reading calls its windows, or null for the whole song. */
    private final double[][] validRanges;
    /** Where the reading says its coverage ends, or -1 when it does not say. */
    private final double coverageEnd;
    /** Stretches the validator doubted this reading over, or null when it doubted none. */
    private final double[][] shakyRanges;

    private WorkbenchGrid(String title, double durationSeconds, String reading, String generatedBy,
                          String audioSource, String shape, int beatsPerBar, boolean caution,
                          List<String> notes, double bpm, double[] beats, int barOffset,
                          double[][] validRanges, double coverageEnd, double[][] shakyRanges) {
        this.title = title == null ? "" : title;
        this.durationSeconds = durationSeconds;
        this.reading = reading == null ? "" : reading;
        this.generatedBy = generatedBy == null ? "" : generatedBy;
        this.audioSource = audioSource == null ? "" : audioSource;
        this.shape = shape == null ? "" : shape;
        this.beatsPerBar = beatsPerBar;
        this.caution = caution;
        this.notes = notes == null
                ? Collections.<String>emptyList() : Collections.unmodifiableList(new ArrayList<>(notes));
        this.bpm = bpm;
        this.beats = beats;
        this.barOffset = barOffset;
        this.validRanges = validRanges;
        this.coverageEnd = coverageEnd;
        this.shakyRanges = shakyRanges;
    }

    public int beatCount() {
        return beats.length;
    }

    public double beat(int index) {
        return beats[Math.max(0, Math.min(beats.length - 1, index))];
    }

    /** The first beat, which is what a downbeat lock needs. */
    public double firstBeat() {
        return beats.length == 0 ? 0 : beats[0];
    }

    /** The last beat, which is where this grid stops saying anything. */
    public double lastBeat() {
        return beats.length == 0 ? 0 : beats[beats.length - 1];
    }

    /** Same song, by title alone: the player's own duration is wrong on this phone. */
    public boolean matches(String appLabel, double appDurationSeconds) {
        return sameSong(title, appLabel);
    }

    /**
     * Whether a file is about the song that is playing.
     *
     * Static because the file has to be judged before it is known to hold a usable
     * grid: a file that is about this song and has nothing usable in it is worth a
     * line saying why, and one about another song is worth silence.
     */
    public static boolean sameSong(String fileTitle, String appLabel) {
        return words(normalise(fileTitle)).size() >= 2 && overlap(fileTitle, appLabel) >= 0.8;
    }

    /** The title inside a file, without reading the rest of it. */
    public static String titleOf(JSONObject root) {
        JSONObject song = root == null ? null : root.optJSONObject("song");
        return song == null ? "" : song.optString("title", "");
    }

    /**
     * How much of this file's title is in the app's label for what is playing.
     *
     * Not equality, and the reason is in the two systems' own files: the Workbench
     * writes "ACIDMAN - 赤橙 (Remastered 2019)" and the app, reading the player's
     * session, writes "ACIDMAN (アシッドマン) — 赤橙 (Remastered 2019)". The same song,
     * with the artist's own spelling added in the middle - so an equality test
     * rejects exactly the file that is right.
     */
    public double titleOverlap(String appLabel) {
        return overlap(title, appLabel);
    }

    private static double overlap(String fileTitle, String appLabel) {
        if (fileTitle.isEmpty() || appLabel == null) return 0;
        java.util.Set<String> mine = words(normalise(fileTitle));
        java.util.Set<String> theirs = words(normalise(appLabel));
        if (mine.isEmpty()) return 0;
        int found = 0;
        for (String word : mine) if (theirs.contains(word)) found++;
        return found / (double) mine.size();
    }

    private static java.util.Set<String> words(String normalised) {
        java.util.Set<String> out = new java.util.HashSet<>();
        for (String word : normalised.split(" ")) {
            // One and two character words are punctuation-sized ("feat", "2019" aside)
            // and carry almost no identity; every word counts, so this only drops the
            // empty ones the split leaves behind.
            if (!word.isEmpty()) out.add(word);
        }
        return out;
    }

    /** Titles differ in punctuation and spacing between the two systems. */
    static String normalise(String text) {
        String clean = text.toLowerCase(Locale.US).trim();
        clean = clean.replaceAll("[\\p{Punct}\\s]+", " ").replaceAll("\\s+", " ").trim();
        return clean;
    }

    /**
     * Whether the grid still says anything about this point of the song.
     *
     * False past the last beat: a grid that has run out is not a grid, and pretending
     * otherwise is how a file that stops half way becomes silence that looks like
     * knowledge. False before the first line for the same reason - and inside a local
     * reading's windows only, because outside them that reading measured nothing.
     */
    public boolean covers(double seconds) {
        if (beats.length == 0) return false;
        if (seconds < beats[0] - ANCHOR_TOLERANCE_SECONDS) return false;
        if (seconds > beats[beats.length - 1]) return false;
        // And inside what the reading says it measured, when it says: a list can reach
        // further than the part somebody stood behind.
        if (coverageEnd >= 0 && seconds > coverageEnd) return false;
        return insideValidRanges(seconds);
    }

    /**
     * Whether this moment is one the grid is not fully behind.
     *
     * True when the reading itself is only a caution, and true inside the stretches the
     * validator threw this reading out over but nothing better turned up to replace it:
     * a file that says "here it is doubtful" is worth more than one that says nothing,
     * as long as the screen repeats it.
     */
    public boolean shakyAt(double seconds) {
        if (caution) return true;
        if (shakyRanges == null) return false;
        for (double[] range : shakyRanges) {
            if (seconds >= range[0] && seconds <= range[1]) return true;
        }
        return false;
    }

    private boolean insideValidRanges(double seconds) {
        if (validRanges == null || validRanges.length == 0) return true;
        for (double[] range : validRanges) {
            if (seconds >= range[0] && seconds <= range[1]) return true;
        }
        return false;
    }

    /** The index of the last beat at or before this point, or -1 where the grid is silent. */
    public int positionAt(double seconds) {
        if (!covers(seconds)) return -1;
        int low = 0;
        int high = beats.length - 1;
        int found = 0;
        while (low <= high) {
            int middle = (low + high) >>> 1;
            if (beats[middle] <= seconds) {
                found = middle;
                low = middle + 1;
            } else {
                high = middle - 1;
            }
        }
        return found;
    }

    /** Which bar this point is in, or -1 when the grid does not know. */
    public long barAt(double seconds) {
        int position = positionAt(seconds);
        if (position < 0 || beatsPerBar <= 0 || position < barOffset) return -1;
        return (position - barOffset) / beatsPerBar;
    }

    /** Where this point falls in the bar, 0-based, or -1 when the grid does not know. */
    public int beatInBar(double seconds) {
        int position = positionAt(seconds);
        if (position < 0 || beatsPerBar <= 0 || position < barOffset) return -1;
        return (position - barOffset) % beatsPerBar;
    }

    /**
     * The tempo around this point, from the spacing of the beats themselves.
     *
     * Measured locally rather than taken from the file's `bpm` field, because a song
     * that changes tempo is written down as pieces with one bpm at the top: the number
     * that belongs on screen is the one for where the music is, which is what the
     * times of the nearby beats say.
     */
    public double localBpm(double seconds) {
        int at = positionAt(seconds);
        if (at < 0) at = beats.length - 1;
        if (at < 1) {
            return beats.length >= 2 && beats[1] > beats[0]
                    ? 60.0 / (beats[1] - beats[0]) : bpm;
        }
        // Medians of the few beats around it, so one odd gap cannot move the number.
        int first = Math.max(0, at - 3);
        int last = Math.min(beats.length - 1, at + 3);
        List<Double> gaps = new ArrayList<>();
        for (int i = first; i < last; i++) {
            double gap = beats[i + 1] - beats[i];
            if (gap > 0) gaps.add(gap);
        }
        if (gaps.isEmpty()) return bpm;
        Collections.sort(gaps);
        double middle = gaps.get(gaps.size() / 2);
        return middle > 0 ? 60.0 / middle : bpm;
    }

    /** Reads a file, or null when it holds no grid that may be used. */
    public static WorkbenchGrid from(JSONObject root) {
        return from(root, null);
    }

    /**
     * Reads a file, or null when it holds no grid that may be used.
     *
     * @param journal when given, everything refused or worth saying is written into
     *                it, one line per reading. A refusal that nobody can read is the
     *                same as a file that was never there, so the caller is expected to
     *                pass one wherever the answer is logged.
     */
    public static WorkbenchGrid from(JSONObject root, List<String> journal) {
        if (root == null) return null;
        JSONObject song = root.optJSONObject("song");
        JSONArray calibrations = root.optJSONArray("calibrations");
        if (song == null || calibrations == null) {
            say(journal, "文件里没有 song 或 calibrations");
            return null;
        }
        String title = song.optString("title", "");
        double duration = song.optDouble("durationSeconds", -1);
        String songAudio = stem(song.optString("audioFileName", ""));

        // Readings the file itself says were downgraded over part of the song. `locally
        // Downgraded` is the field - and the older files only said it in prose ("降级来源
        // BeatThis" inside the reasons), which is still read so that a file written
        // before the field existed is not quietly taken as clean. Such a reading is
        // looked at last rather than dropped: it is still better than tracking the beat.
        java.util.Set<String> downgraded = new java.util.HashSet<>();
        for (int i = 0; i < calibrations.length(); i++) {
            JSONObject item = calibrations.optJSONObject(i);
            if (item == null) continue;
            String reading = readingOf(item);
            if (item.optBoolean("locallyDowngraded", false) || saysDowngraded(item, reading)) {
                downgraded.add(reading);
            }
        }

        for (int pass = 0; pass < 2; pass++) {
        for (String rank : RANK) {
            if (pass == 0 && downgraded.contains(rank)) {
                say(journal, rank + "：这份文件说它被降级过，先看别的读法");
                continue;
            }
            List<double[]> pieces = new ArrayList<>();
            List<String> notes = new ArrayList<>();
            List<double[]> ranges = new ArrayList<>();
            int beatsPerBar = 0;
            int barOffset = 0;
            double bpm = 0;
            String audioSource = "";
            String shape = "";
            boolean caution = false;
            double coverageEnd = -1;
            List<double[]> shaky = new ArrayList<>();
            if (pass == 1 && downgraded.contains(rank)) {
                notes.add(rank + " 只剩它还能用，但它自己的记录说它被降级过");
                // Whether that makes the whole reading a caution is decided below, once
                // the stretches are known: a file that says where it doubted a reading
                // is saying the rest of it is all right.
            }

            for (int i = 0; i < calibrations.length(); i++) {
                JSONObject item = calibrations.optJSONObject(i);
                if (item == null) continue;
                String named = item.optString("reading", "");
                String reading = readingOf(item);
                if (!rank.equals(reading)) continue;

                String health = item.optString("health", "");
                if ("suspect".equals(health) || "ineligible".equals(health)) {
                    notes.add(reading + " health=suspect（" + reasons(item) + "），这一条不用");
                    continue;
                }
                if (!"ok".equals(health)) {
                    caution = true;
                    notes.add(reading + " health=" + (health.isEmpty() ? "没写" : health)
                            + "，当谨慎用（" + reasons(item) + "）");
                }
                if (named.isEmpty()) {
                    notes.add(reading + " 没写 reading，按 calibrationId 认的");
                }
                if (item.optBoolean("needsEars", false)) {
                    // Their own words for it: a bar whose length came from a rule rather
                    // than from a measurement, waiting for a person. Usable - it is the
                    // best reading there is - and not to be shown as a clean one.
                    caution = true;
                    notes.add(reading + " needsEars：" + item.optString("needsEarsNote", "有规则推出来的小节，等人听"));
                }
                if (item.optBoolean("locallyDowngraded", false)) {
                    List<double[]> where = shakyRangesOf(item);
                    shaky.addAll(where);
                    notes.add(reading + " locallyDowngraded" + describe(where));
                }
                int perBar = item.optInt("beatsPerBar", 0);
                if (perBar < 1 || perBar > 12) {
                    notes.add(reading + " beatsPerBar=" + perBar + "，这一条不用");
                    continue;
                }

                double[] beatTimes = times(item, "beats");
                double[] bars = times(item, "downbeats");
                String barField = "downbeats";
                if (bars == null) {
                    bars = times(item, "barLines");
                    barField = "barLines";
                }
                // Which list this entry is written in: the one whose first line is where
                // the reading says its origin is. A reading may carry both, and only one
                // of them can be anchored - the other is then either the bar placement
                // (when the beats are anchored) or a division of the bar (when the bar
                // lines are).
                double anchor = item.optDouble("audioTime", Double.NaN);
                double[] line;
                String shapeHere;
                int offset = 0;
                boolean listIsBarLines = false;
                if (beatTimes != null && anchored(beatTimes, anchor)) {
                    line = beatTimes;
                    shapeHere = bars != null ? "beats+" + barField : "beats";
                    if (bars != null) offset = barOffsetIn(beatTimes, bars);
                } else if (bars != null && anchored(bars, anchor)) {
                    // Bar lines alone: the beats between them are a division of the bar,
                    // and nothing past the last line is invented.
                    line = expandBars(bars, perBar);
                    shapeHere = barField;
                    listIsBarLines = true;
                    if (beatTimes != null) {
                        // Said out loud, because it is the kind of thing that looks like a
                        // bug later: this entry's beats start somewhere other than the
                        // first bar line, and they are not being used.
                        notes.add(reading + " 的 audioTime " + format(bars[0])
                                + " 是第一根" + barField + "，beats 从 " + format(beatTimes[0])
                                + " 开始 → 按" + barField + "铺拍点");
                    }
                } else if (beatTimes == null && bars == null) {
                    notes.add(reading + " 里没有拍点也没有小节线（只有 bpm/audioTime 那种锚点），"
                            + "当不了网格");
                    continue;
                } else {
                    notes.add(reading + " 的列表与 audioTime " + format(anchor)
                            + " 不是一个坐标（beats "
                            + (beatTimes == null ? "无" : format(beatTimes[0])) + "，"
                            + barField + " " + (bars == null ? "无" : format(bars[0])) + "），不用");
                    continue;
                }

                String problem = check(line, item, duration, perBar,
                        listIsBarLines && bars != null ? bars.length : 0);
                if (problem != null) {
                    notes.add(reading + " 的 " + shapeHere + " 不能用：" + problem);
                    continue;
                }
                if (pieces.isEmpty()) {
                    barOffset = offset;
                    shape = shapeHere;
                    if (audioSource.isEmpty()) audioSource = sourceOf(item, songAudio);
                    // How far this reading says it measured, when it says: a list may
                    // reach further than the part anybody stands behind.
                    double coverage = item.optDouble("coverageSeconds", Double.NaN);
                    if (!Double.isNaN(coverage) && coverage > 0) {
                        coverageEnd = line[0] + coverage;
                    }
                }
                pieces.add(line);
                if (beatsPerBar == 0) beatsPerBar = perBar;
                if (bpm == 0) bpm = item.optDouble("bpm", 0);
                readRanges(item, ranges);
            }

            double[] stitched = pieces.isEmpty() ? null : stitch(pieces, notes);
            for (String note : notes) say(journal, note);
            if (stitched == null || stitched.length == 0) continue;
            if (pass == 1 && downgraded.contains(rank) && shaky.isEmpty()) caution = true;
            say(journal, String.format(Locale.US,
                    "用 %s（%s，%d 个拍点，首条 %.3f，%d/4%s%s）",
                    rank, shape, stitched.length, stitched[0], beatsPerBar,
                    audioSource.isEmpty() ? "" : "，素材 " + audioSource,
                    caution ? "，谨慎" : ""));
            return new WorkbenchGrid(title, duration, rank, root.optString("generatedBy", ""),
                    audioSource, shape, beatsPerBar, caution, notes, bpm,
                    stitched, barOffset, ranges.isEmpty() ? null : ranges.toArray(new double[0][]),
                    coverageEnd, shaky.isEmpty() ? null : shaky.toArray(new double[0][]));
        }
        }
        say(journal, "文件里没有能用的读法");
        return null;
    }

    /**
     * One list of times, in the order they are written.
     *
     * Workbench bar lines are objects carrying a `time` (they hold more than a time),
     * madmom's and BeatThis's are plain numbers, and both are the same thing here.
     */
    private static double[] times(JSONObject item, String field) {
        JSONArray array = item.optJSONArray(field);
        if (array == null || array.length() == 0) return null;
        double[] out = new double[array.length()];
        for (int i = 0; i < array.length(); i++) {
            Object value = array.opt(i);
            if (value instanceof Number) {
                out[i] = ((Number) value).doubleValue();
            } else if (value instanceof JSONObject) {
                out[i] = ((JSONObject) value).optDouble("time", Double.NaN);
            } else {
                return null;
            }
        }
        return out;
    }

    /** Beats between bar lines, the last bar line kept and nothing invented after it. */
    private static double[] expandBars(double[] bars, int perBar) {
        List<Double> out = new ArrayList<>();
        for (int i = 0; i < bars.length; i++) {
            if (i < bars.length - 1) {
                double step = (bars[i + 1] - bars[i]) / perBar;
                for (int k = 0; k < perBar; k++) out.add(bars[i] + k * step);
            } else {
                out.add(bars[i]);
            }
        }
        double[] times = new double[out.size()];
        for (int i = 0; i < times.length; i++) times[i] = out.get(i);
        return times;
    }

    /** Which beat of the list the first bar line is, which is what a bar offset needs. */
    private static int barOffsetIn(double[] beats, double[] bars) {
        double gap = medianGap(beats);
        if (gap <= 0) return 0;
        for (int i = 0; i < beats.length; i++) {
            if (Math.abs(beats[i] - bars[0]) <= gap / 2) return i;
        }
        // Not on a beat: the last beat before it is the one the bar starts from.
        int found = 0;
        for (int i = 0; i < beats.length && beats[i] <= bars[0]; i++) found = i;
        return found;
    }

    private static double medianGap(double[] times) {
        if (times.length < 2) return 0;
        double[] gaps = new double[times.length - 1];
        for (int i = 1; i < times.length; i++) gaps[i - 1] = times[i] - times[i - 1];
        Arrays.sort(gaps);
        return gaps[gaps.length / 2];
    }

    /**
     * Whether a list of times may be believed, and if not, why not.
     *
     * The anchor has already done its work by here - it is what decided which list the
     * entry is written in. What is left is whether the times themselves hang together.
     *
     * @param barLines how many bar lines the entry reported, or 0 when the list is beats
     */
    private static String check(double[] times, JSONObject item, double duration, int perBar,
                                int barLines) {
        if (times.length == 0) return "空的";
        if (times[0] < -0.001) return String.format(Locale.US, "首元素为负（%.3f）", times[0]);
        double previous = times[0];
        for (int i = 1; i < times.length; i++) {
            if (Double.isNaN(times[i])) return "有读不出的时间";
            double gap = times[i] - previous;
            if (gap <= 0.0005) {
                return String.format(Locale.US, "时间不递增（第 %d 个 %.3f 紧跟着 %.3f）",
                        i, times[i], previous);
            }
            previous = times[i];
        }
        double median = medianGap(times);
        if (median > 0) {
            int holes = 0;
            double previousTime = times[0];
            for (int i = 1; i < times.length; i++) {
                if (times[i] - previousTime > 5 * median) holes++;
                previousTime = times[i];
            }
            if (holes >= 2 && holes > (times.length - 1) * 0.2) {
                return "空档太多（" + holes + " 处，中位间隔 "
                        + String.format(Locale.US, "%.3f", median) + " 秒）";
            }
        }
        if (barLines > 0 && duration > 0) {
            double bpm = item.optDouble("bpm", 0);
            if (bpm > 0) {
                double barSeconds = 60.0 / bpm * perBar;
                double expected = duration / barSeconds;
                if (barLines < expected * 0.4) {
                    return String.format(Locale.US,
                            "只报了 %d 根小节线，整曲约 %.0f 根（只报了半首那种）",
                            barLines, expected);
                }
            }
        }
        return null;
    }

    /** The validator's own words for why a reading is not clean. */
    private static String reasons(JSONObject item) {
        // A list of lines in the newer files, one line in some of the older ones.
        String single = item.optString("suspectReasons", "");
        if (!single.isEmpty()) return single;
        JSONArray list = item.optJSONArray("suspectReasons");
        if (list == null || list.length() == 0) return "没写原因";
        StringBuilder out = new StringBuilder();
        for (int i = 0; i < list.length(); i++) {
            if (out.length() > 0) out.append("；");
            out.append(list.opt(i));
        }
        return out.toString();
    }

    /** The reading whose own reasons say it was downgraded. */
    private static boolean saysDowngraded(JSONObject item, String reading) {
        if (reading.isEmpty()) return false;
        return reasons(item).contains("降级来源 " + reading);
    }

    /** The stretches a local downgrade covers, from `downgraded: [{start, end, reason}]`. */
    private static List<double[]> shakyRangesOf(JSONObject item) {
        List<double[]> out = new ArrayList<>();
        JSONArray list = item.optJSONArray("downgraded");
        if (list == null) return out;
        for (int i = 0; i < list.length(); i++) {
            JSONObject range = list.optJSONObject(i);
            if (range == null) continue;
            out.add(new double[] {range.optDouble("start", Double.NaN),
                    range.optDouble("end", Double.NaN)});
        }
        return out;
    }

    /** Those stretches, in the words of a log line. */
    private static String describe(List<double[]> ranges) {
        if (ranges.isEmpty()) return "（没写区间）";
        StringBuilder out = new StringBuilder("（");
        for (int i = 0; i < ranges.size(); i++) {
            if (i > 0) out.append("、");
            out.append(format(ranges.get(i)[0])).append("–").append(format(ranges.get(i)[1]));
        }
        return out.append(" 秒）").toString();
    }

    /** Which reading an entry is, whether it says so under `reading` or only `calibrationId`. */
    private static String readingOf(JSONObject item) {
        String named = item.optString("reading", "");
        return named.isEmpty() ? item.optString("calibrationId", "") : named;
    }

    /** Which audio a reading was made from, when the file says so without a word for it. */
    private static String sourceOf(JSONObject item, String songAudio) {
        String declared = item.optString("audioSource", "");
        if (!declared.isEmpty()) return declared;
        String own = stem(item.optString("audioFileName", ""));
        if (own.isEmpty()) return "";
        if (own.equals(songAudio)) return "mix";
        return own;
    }

    /**
     * Whether a list's first element is where the reading says its origin is.
     *
     * With no `audioTime` there is nothing to check, and a list is better than nothing.
     */
    private static boolean anchored(double[] times, double anchor) {
        return times.length > 0
                && (Double.isNaN(anchor) || Math.abs(times[0] - anchor) <= ANCHOR_TOLERANCE_SECONDS);
    }

    private static String format(double seconds) {
        return Double.isNaN(seconds) ? "没写" : String.format(Locale.US, "%.3f", seconds);
    }

    /** ".../drums.wav" -> "drums". */
    private static String stem(String fileName) {
        if (fileName == null || fileName.isEmpty()) return "";
        String name = fileName.replace('\\', '/');
        int slash = name.lastIndexOf('/');
        if (slash >= 0) name = name.substring(slash + 1);
        int dot = name.lastIndexOf('.');
        if (dot > 0) name = name.substring(0, dot);
        return name;
    }

    private static void readRanges(JSONObject item, List<double[]> into) {
        JSONArray ranges = item.optJSONArray("validRanges");
        if (ranges == null) return;
        for (int i = 0; i < ranges.length(); i++) {
            JSONObject range = ranges.optJSONObject(i);
            if (range == null) continue;
            into.add(new double[] {range.optDouble("start", 0), range.optDouble("end", 0)});
        }
    }

    /**
     * Several entries of one reading as one grid.
     *
     * That is how a song that changes tempo is written down: pieces in order, each
     * starting where the last stopped. A piece that starts inside the piece before it
     * is a contradiction rather than a continuation, so it is dropped and said so.
     */
    private static double[] stitch(List<double[]> pieces, List<String> notes) {
        Collections.sort(pieces, (a, b) -> Double.compare(a[0], b[0]));
        List<Double> out = new ArrayList<>();
        for (double[] piece : pieces) {
            if (!out.isEmpty() && piece[0] <= out.get(out.size() - 1)) {
                notes.add(String.format(Locale.US,
                        "有一段从 %.3f 开始，落在上一段里面，弃用这一段", piece[0]));
                continue;
            }
            for (double time : piece) out.add(time);
        }
        double[] times = new double[out.size()];
        for (int i = 0; i < times.length; i++) times[i] = out.get(i);
        return times;
    }

    private static void say(List<String> journal, String line) {
        if (journal != null) journal.add(line);
    }
}
