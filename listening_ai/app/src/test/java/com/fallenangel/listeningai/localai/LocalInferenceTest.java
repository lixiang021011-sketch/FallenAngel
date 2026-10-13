package com.fallenangel.listeningai.localai;

import org.junit.Test;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;
import static org.junit.Assert.*;
import static com.fallenangel.listeningai.localai.MusicObservation.Type.*;

public class LocalInferenceTest {
    private static final int SR = LocalInferenceEngine.SAMPLE_RATE;

    private static short[] drums() {
        short[] pcm = new short[3 * SR];
        for (int hit = 0; hit < 4; hit++) {
            double hz = hit % 2 == 0 ? 65 : 3500;
            int start = (int) ((0.3 + 0.6 * hit) * SR);
            for (int i = 0; i < SR / 8; i++) {
                double t = i / (double) SR;
                pcm[start + i] = (short) (24000 * Math.sin(2 * Math.PI * hz * t) * Math.exp(-t * 35));
            }
        }
        return pcm;
    }

    private static List<MusicObservation> run(short[] pcm, int chunk) {
        try (LocalInferenceEngine engine = new StreamingLocalInferenceEngine()) {
            List<MusicObservation> out = new ArrayList<>();
            for (int offset = 0; offset < pcm.length; offset += chunk) {
                engine.pushPcm(pcm, offset, Math.min(chunk, pcm.length - offset));
                out.addAll(engine.drainObservations());
            }
            assertEquals(0, engine.droppedObservations());
            return out;
        }
    }

    private static final class EchoModel implements AudioFeatureModel {
        int resets, closes;
        public String id() { return "test-adapter"; }
        public List<Prediction> infer(AudioFeatures f) {
            return Collections.singletonList(new Prediction(ONSET, 0.75));
        }
        public void reset() { resets++; }
        public void close() { closes++; }
    }

    @Test public void detectsKnownDrumTimesAndBands() {
        List<MusicObservation> out = run(drums(), 2048);
        for (int i = 0; i < 4; i++) {
            double target = 0.3 + 0.6 * i;
            MusicObservation.Type expected = i % 2 == 0 ? KICK : SNARE;
            assertTrue("missed " + expected + " at " + target, out.stream().anyMatch(o ->
                    o.type == expected && o.audioTimeSeconds >= target
                            && o.audioTimeSeconds <= target + 0.07));
        }
        assertEquals(4, out.stream().filter(o -> o.type == ONSET).count());
    }

    @Test public void arbitraryChunkSizesProduceIdenticalEventsAndFeatures() {
        List<MusicObservation> a = run(drums(), 2048), b = run(drums(), 137);
        assertFalse(a.isEmpty());
        assertEquals(a.size(), b.size());
        for (int i = 0; i < a.size(); i++) {
            assertEquals(a.get(i).features.endSample, b.get(i).features.endSample);
            assertEquals(a.get(i).type, b.get(i).type);
            assertEquals(a.get(i).confidence, b.get(i).confidence, 0);
            assertArrayEquals(a.get(i).features.toTensor(), b.get(i).features.toTensor(), 0);
        }
    }

    @Test public void silenceAndSubFloorNoiseProduceNothing() {
        assertTrue(run(new short[2 * SR], 997).isEmpty());
        short[] noise = new short[2 * SR];
        java.util.Random random = new java.util.Random(17);
        for (int i = 0; i < noise.length; i++) noise[i] = (short) (random.nextInt(31) - 15);
        assertTrue(run(noise, 2048).isEmpty());
    }

    @Test public void sustainedToneDoesNotBecomeRepeatedOnsets() {
        short[] tone = new short[2 * SR];
        for (int i = 0; i < tone.length; i++) tone[i] = (short) (12000 * Math.sin(2 * Math.PI * 100 * i / SR));
        assertEquals(0, run(tone, 777).stream().filter(o -> o.type == ONSET).count());
    }

    @Test public void noveltyRecognizesASustainedTimbreChange() {
        short[] pcm = new short[4 * SR];
        for (int i = 0; i < pcm.length; i++) {
            double hz = i < 2 * SR ? 65 : 3500;
            pcm[i] = (short) (12000 * Math.sin(2 * Math.PI * hz * i / SR));
        }
        assertTrue(run(pcm, 2048).stream().anyMatch(o -> o.type == NOVELTY
                && o.audioTimeSeconds >= 2 && o.audioTimeSeconds < 2.15));
    }

    @Test public void timestampsUseSamplesAndHandlePartialFramesWithoutPadding() {
        try (LocalInferenceEngine engine = new StreamingLocalInferenceEngine(new EchoModel(), 8)) {
            engine.reset(2 * SR);
            engine.pushPcm(new short[2047], 0, 2047);
            assertTrue(engine.drainObservations().isEmpty());
            engine.pushPcm(new short[1], 0, 1);
            MusicObservation o = engine.drainObservations().get(0);
            assertEquals(2 * SR + 2048, o.features.endSample);
            assertEquals((2 * SR + 2048) / (double) SR, o.audioTimeSeconds, 0);
            engine.pushPcm(new short[220], 0, 220);
            assertEquals(2 * SR + 2268, engine.drainObservations().get(0).features.endSample);
        }
    }

    @Test public void resetDropsPendingEventsAndOldPartialWindows() {
        EchoModel model = new EchoModel();
        try (LocalInferenceEngine engine = new StreamingLocalInferenceEngine(model, 8)) {
            engine.pushPcm(new short[2048], 0, 2048);
            engine.reset(10000);
            assertTrue(engine.drainObservations().isEmpty());
            engine.pushPcm(new short[220], 0, 220);
            assertTrue(engine.drainObservations().isEmpty());
            engine.pushPcm(new short[1828], 0, 1828);
            MusicObservation o = engine.drainObservations().get(0);
            assertEquals(12048, o.features.endSample);
            assertEquals(1, o.segmentId);
            assertEquals(2, model.resets);
        }
    }

    @Test public void slowConsumerHasBoundedQueueAndDropCounter() {
        try (LocalInferenceEngine engine = new StreamingLocalInferenceEngine(new EchoModel(), 2)) {
            engine.pushPcm(new short[2048 + 220 * 5], 0, 2048 + 220 * 5);
            List<MusicObservation> out = engine.drainObservations();
            assertEquals(2, out.size());
            assertEquals(4, engine.droppedObservations());
            assertEquals(2048 + 220 * 4, out.get(0).features.endSample);
            assertTrue(engine.drainObservations().isEmpty());
        }
    }

    @Test public void pausedStartupAndResumeKeepCaptureTimeWithoutLeakingAudio() {
        try (InferenceSession session = new InferenceSession(
                new StreamingLocalInferenceEngine(new EchoModel(), 32))) {
            short[] block = new short[2048];
            assertTrue(session.accept(block, 0, block.length, true, "a", 0, false).isEmpty());
            MusicObservation first = session.accept(block, 0, block.length, true, "a", 0, true).get(0);
            assertEquals(4096, first.features.endSample);
            assertTrue(session.accept(block, 0, block.length, true, "a", 0, false).isEmpty());
            MusicObservation resumed = session.accept(block, 0, block.length, true, "a", 0, true).get(0);
            assertEquals(8192, resumed.features.endSample);
            assertTrue(resumed.segmentId > first.segmentId);
            assertEquals(8192, session.captureSamples());
        }
    }

    @Test public void seekAndTrackChangesDiscardCrossBoundaryFrames() {
        try (InferenceSession session = new InferenceSession(
                new StreamingLocalInferenceEngine(new EchoModel(), 32))) {
            short[] block = new short[2048];
            MusicObservation first = session.accept(block, 0, 2048, true, "a", 0, true).get(0);
            assertTrue(session.accept(block, 0, 220, true, "a", 1, true).isEmpty());
            MusicObservation seek = session.accept(block, 0, 1828, true, "a", 1, true).get(0);
            assertEquals(4096, seek.features.endSample);
            assertTrue(seek.segmentId > first.segmentId);
            assertTrue(session.accept(block, 0, 220, true, "b", 1, true).isEmpty());
            MusicObservation track = session.accept(block, 0, 1828, true, "b", 1, true).get(0);
            assertEquals(6144, track.features.endSample);
            assertTrue(track.segmentId > seek.segmentId);
        }
    }

    @Test public void unavailableMetadataFallsBackToAudioAndReconnectionResets() {
        try (InferenceSession session = new InferenceSession(
                new StreamingLocalInferenceEngine(new EchoModel(), 32))) {
            short[] block = new short[2048];
            assertFalse(session.accept(block, 0, 2048, false, "", 0, false).isEmpty());
            assertTrue(session.accept(block, 0, 220, true, "a", 0, true).isEmpty());
            assertEquals(4096, session.accept(block, 0, 1828, true, "a", 0, true)
                    .get(0).features.endSample);
        }
    }

    @Test public void inputRemainsUnchangedAndOffsetIsRespected() {
        short[] original = drums(), saved = original.clone();
        run(original, 17);
        assertArrayEquals(saved, original);
        short[] padded = new short[2050];
        Arrays.fill(padded, (short) 32000);
        Arrays.fill(padded, 1, 2049, (short) 0);
        try (LocalInferenceEngine engine = new StreamingLocalInferenceEngine(new EchoModel(), 8)) {
            engine.pushPcm(padded, 1, 2048);
            assertEquals(0, engine.drainObservations().get(0).features.rms, 0);
        }
    }

    @Test public void invalidRangeAndNegativeResetDoNotConsumeAudio() {
        try (LocalInferenceEngine engine = new StreamingLocalInferenceEngine(new EchoModel(), 8)) {
            assertThrows(IllegalArgumentException.class, () -> engine.pushPcm(new short[8], 1, 8));
            assertThrows(IllegalArgumentException.class, () -> engine.pushPcm(null, 0, 0));
            assertThrows(IllegalArgumentException.class, () -> engine.reset(-1));
            engine.pushPcm(new short[2048], 0, 2048);
            assertEquals(2048, engine.drainObservations().get(0).features.endSample);
        }
    }

    @Test public void modelAdapterOwnsResourceLifetimeAndTensorCopies() {
        EchoModel model = new EchoModel();
        LocalInferenceEngine engine = new StreamingLocalInferenceEngine(model, 8);
        engine.pushPcm(new short[2048], 0, 2048);
        MusicObservation o = engine.drainObservations().get(0);
        assertEquals("test-adapter", o.modelId);
        float[] tensor = o.features.toTensor();
        tensor[0] = 100;
        assertEquals(0, o.features.toTensor()[0], 0);
        engine.close();
        engine.close();
        assertEquals(1, model.closes);
        assertThrows(IllegalStateException.class, () -> engine.pushPcm(new short[1], 0, 1));
    }

    @Test public void invalidModelScoresAreRejected() {
        assertThrows(IllegalArgumentException.class, () -> new AudioFeatureModel.Prediction(ONSET, Double.NaN));
        assertThrows(IllegalArgumentException.class, () -> new AudioFeatureModel.Prediction(ONSET, 1.1));
        assertThrows(IllegalArgumentException.class, () -> new AudioFeatureModel.Prediction(ONSET, -0.1));
    }
}
