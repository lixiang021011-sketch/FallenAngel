package com.fallenangel.overlaybeat;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import java.nio.file.Files;

/**
 * What is worth keeping, and what has to be thrown away.
 *
 * A cache of the song's audio is only an answer if it is the whole song from the
 * beginning. Everything else - a pass that started in the middle, one the player
 * seeked through, one that ended when they got bored - produces a chart that is
 * confidently wrong, which is worse than the live path it would replace.
 */
public class SongCacheTest {
    private static short[] tone(int samples, int value) {
        short[] pcm = new short[samples];
        for (int i = 0; i < samples; i++) pcm[i] = (short) (value + i % 7);
        return pcm;
    }

    private static SongCache cache() throws Exception {
        return new SongCache(Files.createTempDirectory("songcache").toFile());
    }

    @Test
    public void aWholeSongIsKeptAndComesBackUnchanged() throws Exception {
        SongCache cache = cache();
        String key = SongCache.keyFor("spotify:track:abc");
        assertNotNull(key);

        SongCache.Recorder recorder = cache.begin(key, "A Song", 0.1);
        assertTrue(recorder.usable());
        short[] pcm = tone(44100, 100);
        recorder.write(pcm, pcm.length);
        assertTrue("the recording reaches the end of the song",
                recorder.finish(1.0, "A Song"));

        short[] back = cache.read(key);
        assertNotNull(back);
        assertEquals(pcm.length, back.length);
        for (int i = 0; i < pcm.length; i++) assertEquals(pcm[i], back[i]);
    }

    @Test
    public void aPassThatStartedInTheMiddleIsNotTheSong() throws Exception {
        SongCache cache = cache();
        String key = SongCache.keyFor("song b");
        SongCache.Recorder recorder = cache.begin(key, "B", 40.0);
        assertFalse(recorder.usable());
        short[] pcm = tone(44100, 50);
        recorder.write(pcm, pcm.length);
        assertFalse(recorder.finish(1.0, "B"));
        assertNull(cache.read(key));
    }

    @Test
    public void aPassWithASeekInItIsThrownAway() throws Exception {
        SongCache cache = cache();
        String key = SongCache.keyFor("song c");
        SongCache.Recorder recorder = cache.begin(key, "C", 0.0);
        short[] pcm = tone(44100, 20);
        recorder.write(pcm, pcm.length);
        // The player jumped a minute in: the file has a hole in it now.
        recorder.seek(60.0);
        assertFalse(recorder.finish(1.0, "C"));
        assertNull(cache.read(key));
    }

    /**
     * Every track change arrives as a seek to zero - the session resets its
     * position and reports the reset as a jump. Treating that as a seek threw
     * away every recording a second after it started, which is why nothing was
     * ever cached and no song ever grew a waveform.
     */
    @Test
    public void theResetAtTheStartOfASongIsNotASeek() throws Exception {
        SongCache cache = cache();
        String key = SongCache.keyFor("song e");
        SongCache.Recorder recorder = cache.begin(key, "E", 0.0);
        short[] pcm = tone(4410, 4);
        recorder.write(pcm, pcm.length);
        recorder.seek(0.0);
        assertTrue("the song starting at zero is not a jump", recorder.usable());
    }

    @Test
    public void aPassThatStopsEarlyIsThrownAway() throws Exception {
        SongCache cache = cache();
        String key = SongCache.keyFor("song d");
        SongCache.Recorder recorder = cache.begin(key, "D", 0.0);
        short[] pcm = tone(4410, 10);   // a tenth of a second of a three minute song
        recorder.write(pcm, pcm.length);
        assertFalse(recorder.finish(180.0, "D"));
        assertNull(cache.read(key));
        assertEquals(0, cache.size());
    }

    @Test
    public void differentSongsGetDifferentFiles() throws Exception {
        SongCache cache = cache();
        String one = SongCache.keyFor("Artist - Song");
        String two = SongCache.keyFor("Artist - Song (Live)");
        assertNotNull(one);
        assertNotNull(two);
        assertFalse("a live version is a different recording",
                one.equals(two));
        assertEquals(one, SongCache.keyFor("artist  -  song!"));
    }

    @Test
    public void aRecordingNobodyFinishedIsCleanedUp() throws Exception {
        java.io.File dir = Files.createTempDirectory("songcache-orphan").toFile();
        java.io.File orphan = new java.io.File(dir, "deadbeefdeadbeef.raw");
        java.nio.file.Files.write(orphan.toPath(), new byte[2048]);

        // Opening the cache is what sweeps: a file with no entry is a pass that
        // was interrupted, and nothing can say what song it is or how long.
        SongCache cache = new SongCache(dir);
        assertFalse("the fragment should be gone", orphan.exists());
        assertNull(cache.read("deadbeefdeadbeef"));
    }

    @Test
    public void aFinishedRecordingSurvivesTheSweep() throws Exception {
        java.io.File dir = Files.createTempDirectory("songcache-keep").toFile();
        SongCache first = new SongCache(dir);
        String key = SongCache.keyFor("kept song");
        SongCache.Recorder recorder = first.begin(key, "Kept", 0.0);
        short[] pcm = tone(44100, 3);
        recorder.write(pcm, pcm.length);
        assertTrue(recorder.finish(1.0, "Kept"));

        SongCache second = new SongCache(dir);
        assertNotNull("a recorded song is not rubbish", second.read(key));
    }

    @Test
    public void onlyTheMostRecentlyUsedSongsAreKept() throws Exception {
        SongCache cache = cache();
        short[] pcm = tone(44100, 5);
        for (int i = 0; i < 14; i++) {
            String key = SongCache.keyFor("song number " + i);
            SongCache.Recorder recorder = cache.begin(key, "Song " + i, 0.0);
            recorder.write(pcm, pcm.length);
            recorder.finish(1.0, "Song " + i);
        }
        assertTrue("twelve songs is the budget, fourteen were played",
                cache.size() <= 12);
        assertNull("the first one played is the first one dropped",
                cache.read(SongCache.keyFor("song number 0")));
        assertNotNull("the last one played is still there",
                cache.read(SongCache.keyFor("song number 13")));
    }
}
