package com.fallenangel.listeningai.clip;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.Test;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

/**
 * The frontend the app ships, against the one the model was trained with.
 *
 * Everything else here can be right and this can still be wrong, and when it is
 * wrong there is no error to notice: the mel is simply a different shape, and a
 * model answers whatever it is asked. It has happened once already - the filters
 * this project derived by hand were eighty times too heavy, per band - so the
 * shipped numbers are checked against librosa's own output for a clip that
 * travels with the answer.
 */
public class FrontendGoldenTest {
    private static final File GOLDEN =
            new File("src/test/resources/musicnn_frontend_golden.json");
    private static final File MANIFEST =
            new File("src/main/assets/models/models.json");

    /** What the app's frontend says about a clip librosa already answered. */
    @Test
    public void theShippedFrontendReproducesTheLibraryItCameFrom() throws Exception {
        assertTrue("the golden clip ships with the test: " + GOLDEN, GOLDEN.exists());
        JSONObject golden = new JSONObject(
                new String(Files.readAllBytes(GOLDEN.toPath()), StandardCharsets.UTF_8));
        ModelSpec spec = ModelSpec.fromJson(new JSONObject(
                new String(Files.readAllBytes(MANIFEST.toPath()), StandardCharsets.UTF_8)));
        FeatureSpec features = spec.features;

        JSONArray samples = golden.getJSONArray("samples");
        short[] pcm = new short[samples.length()];
        for (int i = 0; i < pcm.length; i++) pcm[i] = (short) samples.getInt(i);

        LogMel frontend = new LogMel(features, Frontend.fromJson(new JSONObject(
                new String(Files.readAllBytes(
                        new File("src/main/assets/" + features.frontend).toPath()),
                        StandardCharsets.UTF_8))));

        float[][] mel = frontend.of(pcm, 0, pcm.length, golden.getInt("sampleRate"));
        JSONArray expected = golden.getJSONArray("expectedMel");
        assertEquals("one row per frame", golden.getInt("frameCount"), mel.length);
        assertEquals("a row per mel band", features.mels, mel[0].length);

        double worst = 0;
        int worstFrame = 0, worstBand = 0;
        for (int frame = 0; frame < mel.length; frame++) {
            for (int band = 0; band < mel[frame].length; band++) {
                double delta = Math.abs(mel[frame][band] - expected.getJSONArray(frame).getDouble(band));
                if (delta > worst) {
                    worst = delta;
                    worstFrame = frame;
                    worstBand = band;
                }
            }
        }
        // The file is written with six significant digits and the app reads
        // seven, so the honest bound is what that rounding costs, not zero.
        assertTrue("worst difference from librosa: " + worst
                        + " at frame " + worstFrame + " band " + worstBand,
                worst < 1e-4);
    }

    /**
     * The same clip at a quarter of the level is the same clip.
     *
     * This is the volume slider: without it the model hears a different song
     * when the listener turns the music up, which was worth 0.31 of the novelty
     * it reports for real musical change.
     */
    @Test
    public void turningTheVolumeDownChangesNothing() throws Exception {
        ModelSpec spec = ModelSpec.fromJson(new JSONObject(
                new String(Files.readAllBytes(MANIFEST.toPath()), StandardCharsets.UTF_8)));
        short[] loud = tone(0.4);
        short[] quiet = new short[loud.length];
        for (int i = 0; i < loud.length; i++) quiet[i] = (short) (loud[i] / 4);

        LogMel frontend = new LogMel(spec.features, Frontend.fromJson(new JSONObject(
                new String(Files.readAllBytes(
                        new File("src/main/assets/" + spec.features.frontend).toPath()),
                        StandardCharsets.UTF_8))));
        float[][] a = frontend.of(loud, 0, loud.length, 16000);
        float[][] b = frontend.of(quiet, 0, quiet.length, 16000);

        double worst = 0;
        for (int frame = 0; frame < a.length; frame++) {
            for (int band = 0; band < a[frame].length; band++) {
                worst = Math.max(worst, Math.abs(a[frame][band] - b[frame][band]));
            }
        }
        assertTrue("four times quieter should be the same mel: " + worst, worst < 0.02);
    }

    private static short[] tone(double amplitude) {
        short[] pcm = new short[16000];
        for (int i = 0; i < pcm.length; i++) {
            double value = amplitude * (Math.sin(2 * Math.PI * 220 * i / 16000.0)
                    + 0.5 * Math.sin(2 * Math.PI * 4400 * i / 16000.0));
            pcm[i] = (short) Math.max(-32768, Math.min(32767, value * 32767 / 1.5));
        }
        return pcm;
    }
}
