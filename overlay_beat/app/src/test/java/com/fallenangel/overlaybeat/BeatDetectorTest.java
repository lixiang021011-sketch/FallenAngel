package com.fallenangel.overlaybeat;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import java.util.ArrayList;
import java.util.List;

/**
 * The generator, against music whose answer is known.
 *
 * Everything else in this project is measured against recordings, where "the
 * right chart" is an opinion. A drum pattern is not: the hits are where they
 * were written, and which drum it is is a fact. So this is the one place where
 * the generator can be held to an exact answer - it has to lock to the tempo,
 * put its notes on the hits, and say whether each one was a kick or a snare.
 */
public class BeatDetectorTest {
    private static final int SR = BeatDetector.SAMPLE_RATE;
    private static final double BPM = 120;
    private static final double BEAT = 60.0 / BPM;
    private static final int BARS = 15;
    /** Positions per beat the chart is written on. */
    private static final int SUBDIVISIONS = 4;

    /** A plain four-on-the-floor with backbeats: kick on 1 and 3, snare on 2 and 4. */
    private static short[] pattern() {
        double seconds = BARS * 4 * BEAT + 1.0;
        short[] out = new short[(int) (seconds * SR)];
        java.util.Random random = new java.util.Random(7);
        for (int bar = 0; bar < BARS; bar++) {
            for (int beat = 0; beat < 4; beat++) {
                double at = startOf(bar, beat);
                if (beat % 2 == 0) {
                    kick(out, at);
                } else {
                    snare(out, at, random);
                }
                // Eighth-note hats, so the grid has something to be tempted by
                // between the beats.
                hat(out, at + BEAT / 2, random);
            }
        }
        return out;
    }

    private static double startOf(int bar, int beat) {
        return (bar * 4 + beat) * BEAT + 0.25;
    }

    private static void kick(short[] out, double at) {
        int start = (int) (at * SR);
        for (int i = 0; i < (int) (0.12 * SR) && start + i < out.length; i++) {
            double t = i / (double) SR;
            double value = Math.sin(2 * Math.PI * 55 * t) * Math.exp(-t * 30) * 0.85;
            add(out, start + i, value);
        }
    }

    private static void snare(short[] out, double at, java.util.Random random) {
        int start = (int) (at * SR);
        for (int i = 0; i < (int) (0.12 * SR) && start + i < out.length; i++) {
            double t = i / (double) SR;
            double value = (random.nextDouble() * 2 - 1) * Math.exp(-t * 28) * 0.5;
            add(out, start + i, value);
        }
    }

    private static void hat(short[] out, double at, java.util.Random random) {
        int start = (int) (at * SR);
        for (int i = 0; i < (int) (0.03 * SR) && start + i < out.length; i++) {
            double t = i / (double) SR;
            double value = (random.nextDouble() * 2 - 1) * Math.exp(-t * 120) * 0.3;
            add(out, start + i, value);
        }
    }

    private static void add(short[] out, int index, double value) {
        double mixed = out[index] / 32768.0 + value;
        out[index] = (short) Math.max(-32768, Math.min(32767, Math.round(mixed * 32768)));
    }

    @Test
    public void theChartLocksToTheTempoAndLandsOnTheDrums() {
        Result result = generate(pattern());

        assertEquals("the tempo is the one the pattern was written at",
                BPM, result.bpm, 3.0);
        assertTrue("nothing was generated at all", result.notes.size() > 40);

        int onHit = 0;
        for (double note : result.notes) {
            if (nearest(hitTimes(), note) <= 0.06) onHit++;
        }
        double precision = onHit / (double) result.notes.size();
        assertTrue("only " + Math.round(precision * 100) + "% of the notes landed on a drum",
                precision >= 0.85);
    }

    @Test
    public void aKickIsCalledAKickAndASnareASnare() {
        Result result = generate(pattern());
        assertTrue("nothing was generated at all", result.notes.size() > 40);

        int kicksRight = 0;
        int kicksSeen = 0;
        int snaresRight = 0;
        int snaresSeen = 0;
        for (int i = 0; i < result.notes.size(); i++) {
            double at = result.notes.get(i);
            if (nearest(kickTimes(), at) <= 0.06) {
                kicksSeen++;
                if (result.kick.get(i)) kicksRight++;
            } else if (nearest(snareTimes(), at) <= 0.06) {
                snaresSeen++;
                if (!result.kick.get(i)) snaresRight++;
            }
        }
        assertTrue("no notes landed on the kicks at all", kicksSeen > 10);
        assertTrue("no notes landed on the snares at all", snaresSeen > 5);
        assertTrue("the low drum is being called a snare: " + kicksRight + "/" + kicksSeen,
                kicksRight >= kicksSeen * 0.8);
        assertTrue("the high drum is being called a kick: " + snaresRight + "/" + snaresSeen,
                snaresRight >= snaresSeen * 0.7);
    }

    @Test
    public void aFinerGridPlacesMoreOfTheMusic() {
        short[] audio = pattern();
        int coarse = generate(audio, 1).notes.size();
        int fine = generate(audio, 4).notes.size();
        assertTrue("a sixteenth grid (" + fine + ") should carry more than a beat grid ("
                + coarse + ")", fine > coarse);
    }

    /**
     * The figure is the unit: the chart should have the shape of what the music
     * plays, not every place a drum was ever heard.
     *
     * The pattern puts a drum on the beat and a hat halfway through it, and
     * nothing on the second or fourth sixteenth of the beat. A chart built one
     * position at a time fills all four of them in; the same chart built from
     * the beat's figure has two notes in it and two holes.
     */
    @Test
    public void theChartPlaysTheFigureOfTheBeatRatherThanEveryPosition() {
        Result result = generate(pattern());
        assertTrue("nothing was generated at all", result.notes.size() > 40);

        for (double note : result.notes) {
            double intoBeat = ((note - 0.25) % BEAT + BEAT) % BEAT / BEAT;
            int subdivision = (int) Math.round(intoBeat * SUBDIVISIONS) % SUBDIVISIONS;
            assertTrue(String.format(java.util.Locale.US,
                            "a note at %.3f lands on subdivision %d, which the music "
                                    + "does not play", note, subdivision),
                    subdivision == 0 || subdivision == 2);
        }
    }

    private static List<Double> hitTimes() {
        List<Double> hits = new ArrayList<>(kickTimes());
        hits.addAll(snareTimes());
        for (int bar = 0; bar < BARS; bar++) {
            for (int beat = 0; beat < 4; beat++) {
                hits.add(startOf(bar, beat) + BEAT / 2);
            }
        }
        return hits;
    }

    private static List<Double> kickTimes() {
        List<Double> times = new ArrayList<>();
        for (int bar = 0; bar < BARS; bar++) {
            times.add(startOf(bar, 0));
            times.add(startOf(bar, 2));
        }
        return times;
    }

    private static List<Double> snareTimes() {
        List<Double> times = new ArrayList<>();
        for (int bar = 0; bar < BARS; bar++) {
            times.add(startOf(bar, 1));
            times.add(startOf(bar, 3));
        }
        return times;
    }

    private static double nearest(List<Double> times, double at) {
        double best = Double.MAX_VALUE;
        for (double time : times) best = Math.min(best, Math.abs(time - at));
        return best;
    }

    private static final class Result {
        final List<Double> notes = new ArrayList<>();
        final List<Boolean> kick = new ArrayList<>();
        final List<Double> bpms = new ArrayList<>();
        double bpm;
    }

    private static Result generate(short[] audio) {
        return generate(audio, 4);
    }

    /** Push the audio through the analysis exactly as the capture service does. */
    private static Result generate(short[] audio, int subdivisions) {
        // On a phone the media session is always there, which is what stops the
        // tempo loop from re-locking on its own evidence. The test stands in for
        // the same thing so it is measuring the generator, not the fallback.
        Playback.available = true;
        BeatDetector detector = new BeatDetector();
        detector.setSubdivisions(subdivisions);
        ChartBuilder builder = new ChartBuilder();
        Result result = new Result();

        int block = 2048;
        for (int offset = 0; offset < audio.length; offset += block) {
            int count = Math.min(block, audio.length - offset);
            short[] chunk = new short[count];
            System.arraycopy(audio, offset, chunk, 0, count);
            detector.push(chunk, count);
            for (Beat beat : detector.drainBeats()) {
                ChartNote note = builder.accept(beat);
                if (note == null) continue;
                result.notes.add(note.time);
                result.kick.add(beat.kickDominant());
                result.bpms.add(detector.bpm());
            }
        }
        // The tempo while the chart was being written. Reading it at the very
        // end would read the silence that follows the song, which is not a tempo.
        List<Double> sorted = new ArrayList<>(result.bpms);
        java.util.Collections.sort(sorted);
        result.bpm = sorted.isEmpty() ? 0 : sorted.get(sorted.size() / 2);
        return result;
    }
}
