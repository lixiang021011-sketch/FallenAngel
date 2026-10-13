package com.fallenangel.listeningai.localai;

import org.junit.Test;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import static org.junit.Assert.*;

public class MusicSectionTest {
    private static final int SR = 44100;

    private static SectionFeatureExtractor.Feature feature(int index, int voice) {
        double[] vector = new double[SectionFeatureExtractor.DIMENSIONS];
        vector[voice] = 1;
        return new SectionFeatureExtractor.Feature(index * 22050L, (index + 1) * 22050L, 0.1, vector);
    }

    private static short[] tones(int secondsPerSection, double... hz) {
        short[] pcm = new short[secondsPerSection * hz.length * SR];
        for (int i = 0; i < pcm.length; i++) {
            double frequency = hz[i / (secondsPerSection * SR)];
            pcm[i] = (short) (12000 * Math.sin(2 * Math.PI * frequency * i / SR));
        }
        return pcm;
    }

    private static List<SectionObservation> analyse(short[] pcm, int chunk) {
        try (MusicSectionEngine engine = new MusicSectionEngine()) {
            List<SectionObservation> out = new ArrayList<>();
            for (int i = 0; i < pcm.length; i += chunk) {
                engine.pushPcm(pcm, i, Math.min(chunk, pcm.length - i));
                out.addAll(engine.drainSections());
            }
            assertEquals(0, engine.droppedSections());
            return out;
        }
    }

    @Test public void boundariesNeedSecondsOfContextAndRecognizeReturnToA() {
        SectionTracker tracker = new SectionTracker();
        tracker.reset();
        for (int i = 0; i < 72; i++) tracker.accept(feature(i, i < 24 || i >= 48 ? 0 : 1));
        List<SectionObservation> out = tracker.drain();
        assertEquals(3, out.size());
        assertEquals(1, out.get(0).sectionIndex);
        assertEquals(3, out.get(2).sectionIndex);
        assertEquals("A", out.get(0).label);
        assertEquals("B", out.get(1).label);
        assertEquals("A", out.get(2).label);
        assertEquals(12, out.get(1).boundarySeconds(), 0);
        assertEquals(24, out.get(2).boundarySeconds(), 0);
        assertEquals(17, out.get(1).detectedAtSeconds(), 0);
        assertEquals(SectionObservation.Kind.RETURN, out.get(2).kind);
    }

    @Test public void halfSecondAccentDoesNotBecomeASection() {
        SectionTracker tracker = new SectionTracker();
        tracker.reset();
        for (int i = 0; i < 80; i++) tracker.accept(feature(i, i == 30 ? 1 : 0));
        assertEquals(1, tracker.drain().size());
    }

    @Test public void repeatedShortHarmonicCycleDoesNotSplitEveryChord() {
        SectionTracker tracker = new SectionTracker();
        tracker.reset();
        for (int i = 0; i < 120; i++) tracker.accept(feature(i, (i / 2) % 4));
        assertEquals(1, tracker.drain().size());
    }

    @Test public void pcmFrontendFindsContrastingSectionsAndIsChunkInvariant() {
        short[] pcm = tones(12, 220, 1800, 220);
        short[] copy = pcm.clone();
        List<SectionObservation> a = analyse(pcm, 2048), b = analyse(pcm, 137);
        assertArrayEquals(copy, pcm);
        assertEquals(3, a.size());
        assertEquals("A", a.get(0).label);
        assertEquals("B", a.get(1).label);
        assertEquals("A", a.get(2).label);
        assertEquals(12, a.get(1).boundarySeconds(), 0.5);
        assertEquals(24, a.get(2).boundarySeconds(), 0.5);
        assertEquals(a.size(), b.size());
        for (int i = 0; i < a.size(); i++) {
            assertEquals(a.get(i).boundarySample, b.get(i).boundarySample);
            assertEquals(a.get(i).detectedAtSample, b.get(i).detectedAtSample);
            assertEquals(a.get(i).label, b.get(i).label);
            assertEquals(a.get(i).novelty, b.get(i).novelty, 0);
        }
    }

    @Test public void silenceAndLowNoiseDoNotInventSections() {
        assertTrue(analyse(new short[12 * SR], 2048).isEmpty());
        short[] noise = new short[12 * SR];
        java.util.Random random = new java.util.Random(19);
        for (int i = 0; i < noise.length; i++) noise[i] = (short) (random.nextInt(31) - 15);
        assertTrue(analyse(noise, 997).isEmpty());
    }

    @Test public void simplePlaybackGainChangeDoesNotInventASection() {
        short[] pcm = tones(24, 440);
        for (int i = 12 * SR; i < pcm.length; i++) pcm[i] /= 2;
        assertEquals(1, analyse(pcm, 2048).size());
    }

    @Test public void seekClearsFamilyMemoryAndUsesAbsoluteCaptureTime() {
        try (MusicSectionEngine engine = new MusicSectionEngine()) {
            short[] before = tones(8, 220), after = tones(8, 1800);
            engine.pushPcm(before, 0, before.length);
            SectionObservation old = engine.drainSections().get(0);
            engine.reset(40L * SR);
            engine.pushPcm(after, 0, after.length);
            SectionObservation fresh = engine.drainSections().get(0);
            assertEquals("A", fresh.label);
            assertEquals(40, fresh.boundarySeconds(), 0);
            assertTrue(fresh.streamId > old.streamId);
        }
    }

    @Test public void pausePreservesFamiliesButDoesNotCreateBoundaryAtResume() {
        try (SectionSession session = new SectionSession()) {
            short[] a = tones(12, 220), b = tones(12, 1800), silence = new short[6 * SR];
            assertEquals("A", session.accept(a, 0, a.length, true, "song", 0, true).get(0).label);
            assertTrue(session.accept(silence, 0, silence.length, true, "song", 0, false).isEmpty());
            assertTrue(session.accept(a, 0, a.length, true, "song", 0, true).isEmpty());
            List<SectionObservation> changed = session.accept(b, 0, b.length, true, "song", 0, true);
            assertEquals(1, changed.size());
            assertEquals("B", changed.get(0).label);
            assertEquals(30, changed.get(0).boundarySeconds(), 0.5);
            assertEquals(42L * SR, session.captureSamples());
        }
    }

    @Test public void trackChangeStartsANewStreamWithoutComparingDifferentSongs() {
        try (SectionSession session = new SectionSession()) {
            short[] a = tones(8, 220), b = tones(8, 1800);
            SectionObservation first = session.accept(a, 0, a.length, true, "one", 0, true).get(0);
            SectionObservation next = session.accept(b, 0, b.length, true, "two", 0, true).get(0);
            assertEquals(SectionObservation.Kind.START, next.kind);
            assertEquals("A", next.label);
            assertTrue(next.streamId > first.streamId);
        }
    }

    @Test public void tooShortInputAndUnconfirmedEndingDoNotFabricateBoundaries() {
        assertTrue(analyse(tones(3, 220), 2048).isEmpty());
        short[] a = tones(12, 220), b = tones(3, 1800);
        short[] joined = Arrays.copyOf(a, a.length + b.length);
        System.arraycopy(b, 0, joined, a.length, b.length);
        assertEquals(1, analyse(joined, 2048).size());
    }

    @Test public void featurePoolingUsesExactHalfSecondTimesAndDoesNotRetainInput() {
        SectionFeatureExtractor extractor = new SectionFeatureExtractor();
        extractor.reset(10000);
        List<SectionFeatureExtractor.Feature> frames = new ArrayList<>();
        short[] pcm = new short[44100];
        extractor.push(pcm, 0, pcm.length, frames::add);
        assertEquals(2, frames.size());
        assertEquals(10000, frames.get(0).start);
        assertEquals(32050, frames.get(0).end);
        assertEquals(54100, frames.get(1).end);
        for (double value : frames.get(0).vector) assertTrue(Double.isFinite(value));
    }

    @Test public void resourceAndParameterContractIsExplicit() {
        MusicSectionEngine engine = new MusicSectionEngine();
        assertThrows(IllegalArgumentException.class, () -> engine.reset(-1));
        assertThrows(IllegalArgumentException.class, () -> engine.pushPcm(new short[3], 2, 2));
        engine.close();
        engine.close();
        assertThrows(IllegalStateException.class, () -> engine.pushPcm(new short[3], 0, 3));
    }

    @Test public void pausedStartupSeekAndMetadataFallbackRespectTransport() {
        try (SectionSession session = new SectionSession()) {
            short[] pcm = tones(8, 220);
            assertTrue(session.accept(pcm, 0, pcm.length, true, "song", 0, false).isEmpty());
            SectionObservation start = session.accept(pcm, 0, pcm.length, true, "song", 0, true).get(0);
            assertEquals(8, start.boundarySeconds(), 0);
            SectionObservation seek = session.accept(pcm, 0, pcm.length, true, "song", 1, true).get(0);
            assertTrue(seek.streamId > start.streamId);
            assertEquals(1, seek.sectionIndex);
            SectionObservation fallback = session.accept(pcm, 0, pcm.length, false, "song", 1, false).get(0);
            assertTrue(fallback.streamId > seek.streamId);
            assertEquals(24, fallback.boundarySeconds(), 0);
        }
    }

    @Test public void outputQueueAndFamilyMemoryRemainBounded() {
        SectionTracker tracker = new SectionTracker();
        tracker.reset();
        for (int i = 0; i < 24 * 140; i++) tracker.accept(feature(i, (i / 24) % 2));
        assertEquals(SectionTracker.CAPACITY, tracker.drain().size());
        assertTrue(tracker.dropped() > 0);
        tracker.reset();
        for (int i = 0; i < 24 * 35; i++) tracker.accept(feature(i, i / 24));
        List<SectionObservation> observations = tracker.drain();
        assertTrue(observations.stream().anyMatch(o -> o.label.equals("UNKNOWN")));
        assertTrue(observations.stream().anyMatch(o -> o.boundarySeconds() >= 33 * 12));
    }
}
