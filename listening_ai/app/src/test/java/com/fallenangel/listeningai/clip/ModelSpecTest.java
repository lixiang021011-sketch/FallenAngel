package com.fallenangel.listeningai.clip;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertTrue;
import static org.junit.Assert.fail;

import com.fallenangel.listeningai.localai.MusicObservation;

import org.json.JSONObject;
import org.junit.Test;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

/**
 * The description a model travels with, and the manifest that ships today.
 *
 * The point of the manifest is that the next model is a file and a description.
 * That only holds if the description is read rather than assumed, and if a
 * description that does not match its own model is refused rather than believed.
 */
public class ModelSpecTest {
    private static final File MANIFEST =
            new File("src/main/assets/models/models.json");

    @Test
    public void theManifestShipsAndDescribesAModelThatIsThere() throws Exception {
        assertTrue("the manifest should be shipped with the app: " + MANIFEST,
                MANIFEST.exists());
        String text = new String(Files.readAllBytes(MANIFEST.toPath()), StandardCharsets.UTF_8);
        ModelSpec spec = ModelSpec.fromJson(new JSONObject(text));

        assertTrue("the frontend and the model agree on the band count",
                spec.features.mels == spec.mels);
        assertTrue("nothing is described that is not on disk",
                new File("src/main/assets/" + spec.path).exists());
        if (spec.features.frontend != null) {
            File frontend = new File("src/main/assets/" + spec.features.frontend);
            assertTrue("a named frontend has to ship with the model: " + frontend,
                    frontend.exists());
            JSONObject described = new JSONObject(
                    new String(Files.readAllBytes(frontend.toPath()), StandardCharsets.UTF_8));
            Frontend read = Frontend.fromJson(described);
            read.check(spec.features);
            assertEquals("the file and the manifest agree",
                    spec.features.mels, read.filters.length);
        }
        // The placeholder is still here to point at: it is the smoke test.
        assertTrue(new File("src/main/assets/models/placeholder-events.onnx").exists());
    }

    /** The four classes and the shipped contract, still describable. */
    @Test
    public void thePlaceholderIsStillAFullDescription() {
        ModelSpec spec = ModelSpec.placeholder();
        assertEquals("models/placeholder-events.onnx", spec.path);
        assertEquals(4, spec.classes.size());
        assertEquals(MusicObservation.Type.KICK, spec.classes.get(1));
    }

    @Test
    public void aDescriptionRoundTrips() {
        ModelSpec original = ModelSpec.placeholder();
        ModelSpec read = ModelSpec.fromJson(original.toJson());
        assertEquals(original.path, read.path);
        assertEquals(original.classes, read.classes);
        assertEquals(original.threshold, read.threshold, 1e-9);
        assertEquals(original.features.sampleRate, read.features.sampleRate);
        assertEquals(original.features.hop, read.features.hop);
        assertEquals(original.layout, read.layout);
        assertEquals(original.strideMillis, read.strideMillis);
        assertEquals(original.features.log, read.features.log);
        assertEquals(original.features.normaliseLevel, read.features.normaliseLevel);
        assertEquals(original.features.frontend, read.features.frontend);
    }

    /**
     * A model whose frontend is a file is not one we are allowed to guess at.
     *
     * Deriving our own filters for it is the failure this layer exists to stop,
     * so the loader has to notice rather than quietly do it.
     */
    @Test
    public void aModelWithItsOwnFrontendIsNotDerivedFor() throws Exception {
        JSONObject json = ModelSpec.placeholder().toJson();
        json.getJSONObject("features").put("frontend", "models/musicnn_msd.frontend.json");
        ModelSpec spec = ModelSpec.fromJson(json);
        try {
            new LogMel(spec.features);
            fail("deriving an approximation of somebody else's frontend is not a fallback");
        } catch (IllegalArgumentException expected) {
            assertTrue(expected.getMessage().contains("musicnn_msd.frontend.json"));
        }
    }

    /** Two more things that would be read as zero rather than refused. */
    @Test
    public void anUnknownLogOrLayoutIsRefused() throws Exception {
        JSONObject json = ModelSpec.placeholder().toJson();
        json.getJSONObject("features").put("logKind", "FIFTH_ROOT");
        try {
            ModelSpec.fromJson(json);
            fail("a log transform the frontend cannot do must be refused");
        } catch (IllegalArgumentException expected) {
            assertTrue(expected.getMessage().contains("FIFTH_ROOT"));
        }
        JSONObject other = ModelSpec.placeholder().toJson();
        other.put("layout", "SIDEWAYS");
        try {
            ModelSpec.fromJson(other);
            fail("an input layout the loader cannot arrange must be refused");
        } catch (IllegalArgumentException expected) {
            assertTrue(expected.getMessage().contains("SIDEWAYS"));
        }
    }

    /** The stride is a time, and the stream counts hops. */
    @Test
    public void theStrideIsConvertedToHops() throws Exception {
        JSONObject json = ModelSpec.placeholder().toJson();
        json.put("strideMillis", 500);         // hop is 160 / 16000 = 10ms
        assertEquals(50, ModelSpec.fromJson(json).strideHops());
        json.put("strideMillis", 0);
        assertEquals("zero means every hop", 1, ModelSpec.fromJson(json).strideHops());
    }

    /** A model for 44.1 kHz and 128 mel is a description, not a code change. */
    @Test
    public void aDifferentModelIsJustADifferentDescription() {
        JSONObject json = ModelSpec.placeholder().toJson();
        ModelSpec changed;
        try {
            json.put("mels", 128);
            json.put("frames", 128);
            json.put("threshold", 0.35);
            JSONObject features = json.getJSONObject("features");
            features.put("sampleRate", 44100);
            features.put("mels", 128);
            features.put("fft", 2048);
            features.put("frame", 2048);
            features.put("hop", 512);
            features.put("maxHz", 11025);
            changed = ModelSpec.fromJson(json);
        } catch (Exception e) {
            throw new AssertionError(e);
        }
        assertEquals(128, changed.features.mels);
        assertEquals(44100, changed.features.sampleRate);
        assertEquals(0.35, changed.threshold, 1e-9);
        assertNotNull(new LogMel(changed.features));
    }

    /** Believing a description that does not fit its model is silent nonsense. */
    @Test
    public void aDescriptionThatContradictsItselfIsRefused() throws Exception {
        JSONObject json = ModelSpec.placeholder().toJson();
        try {
            json.put("mels", 128);          // model wants 128…
            ModelSpec.fromJson(json);        // …but the frontend still makes 64
            fail("a model and a frontend that disagree must be refused");
        } catch (IllegalArgumentException expected) {
            assertTrue(expected.getMessage().contains("mel"));
        }
    }

    @Test
    public void anUnknownClassIsRefused() throws Exception {
        JSONObject json = ModelSpec.placeholder().toJson();
        try {
            json.put("classes", new org.json.JSONArray().put("ONSET").put("COWBELL"));
            ModelSpec.fromJson(json);
            fail("a class the app cannot report must be refused");
        } catch (IllegalArgumentException expected) {
            assertTrue(expected.getMessage().contains("COWBELL"));
        }
    }
}
