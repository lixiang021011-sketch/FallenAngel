package com.fallenangel.listeningai.listening;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

/**
 * Chords and their degree in the key, checked against chords this file wrote.
 *
 * The same four triads were given to the model first, and it answered "F major"
 * to three of them and one of the four correctly - clean sines, no drums, no
 * vocals, nothing to excuse it. So a chord is not something to ask a language
 * model about; it is something to measure, and a measurement can be checked
 * against a known answer, which is what this does.
 */
public class HarmonyTest {
    private static final int RATE = 44100;

    /** A triad as a bank of sine partials, which is what a chord is, minus timbre. */
    private static short[] triad(double rootHz, double third, int seconds) {
        return triad(rootHz, third, 1.498307, seconds);   // perfect fifth
    }

    private static short[] triad(double rootHz, double third, double fifth, int seconds) {
        int n = RATE * seconds;
        short[] pcm = new short[n];
        double[] ratios = {1.0, third, fifth};      // root, third, fifth
        for (int i = 0; i < n; i++) {
            double value = 0;
            for (double ratio : ratios) {
                value += Math.sin(2 * Math.PI * rootHz * ratio * i / RATE) * 0.25;
            }
            pcm[i] = (short) Math.max(-32000, Math.min(32000, value * 32000));
        }
        return pcm;
    }

    private static final double MAJOR_THIRD = 1.259921;
    private static final double MINOR_THIRD = 1.189207;

    @Test
    public void aCMajorTriadIsTheFirstDegreeOfTheKeyItSuggests() {
        short[] pcm = triad(261.63, MAJOR_THIRD, 2);
        double[] chroma = Harmony.chroma(pcm, 0, pcm.length, RATE);
        Harmony.Reading heard = Harmony.of(pcm, 0, pcm.length, RATE);
        assertEquals("C", heard.chord);
        // Full-width numerals and the quality spelled out, which is how a Chinese
        // theory text prints a degree - not "I" next to "Am".
        assertEquals("\u2160", Harmony.degreeOf(heard, Harmony.keyOf(chroma)));
        assertEquals("C 大三和弦", heard.name());
    }

    @Test
    public void aGMajorTriadIsTheFifthDegreeOfC() {
        // In the key this file writes around, the second chord of I-V-vi-IV is
        // G major, and its degree is V. The key is estimated from the passage,
        // not from the chord, because one chord on its own is just as much a
        // key as it is a degree.
        short[] passage = progression();
        double[] chroma = Harmony.chroma(passage, 0, passage.length, RATE);
        Harmony.Key key = Harmony.keyOf(chroma);
        assertEquals("C", key.tonic);
        assertEquals("C 调", key.name());

        short[] g = triad(392.00, MAJOR_THIRD, 2);
        assertEquals("\u2164",
                Harmony.degreeOf(Harmony.chordOf(Harmony.chroma(g, 0, g.length, RATE)), key));
    }

    @Test
    public void anAMinorTriadIsTheSixthDegreeOfC() {
        short[] passage = progression();
        Harmony.Key key = Harmony.keyOf(Harmony.chroma(passage, 0, passage.length, RATE));
        short[] am = triad(220.00, MINOR_THIRD, 2);
        Harmony.Reading heard = Harmony.chordOf(Harmony.chroma(am, 0, am.length, RATE));
        // Lower-case letter for a minor triad, lower-case numeral for its degree.
        assertEquals("a", heard.chord);
        assertEquals("a 小三和弦", heard.name());
        assertEquals("\u2175", Harmony.degreeOf(heard, key));
    }

    @Test
    public void anFMajorTriadIsTheFourthDegreeOfC() {
        short[] passage = progression();
        Harmony.Key key = Harmony.keyOf(Harmony.chroma(passage, 0, passage.length, RATE));
        short[] f = triad(349.23, MAJOR_THIRD, 2);
        Harmony.Reading heard = Harmony.chordOf(Harmony.chroma(f, 0, f.length, RATE));
        assertEquals("F", heard.chord);
        assertEquals("\u2163", Harmony.degreeOf(heard, key));
    }

    @Test
    public void theWholeProgressionReadsAsOneCThenFiveSixFour() {
        short[] passage = progression();
        Harmony.Key key = Harmony.keyOf(Harmony.chroma(passage, 0, passage.length, RATE));
        String[] expected = {"\u2160", "\u2164", "\u2175", "\u2163"};
        int bar = RATE * 2;
        for (int i = 0; i < 4; i++) {
            double[] chroma = Harmony.chroma(passage, i * bar, bar, RATE);
            assertEquals("chord " + i, expected[i], Harmony.degreeOf(Harmony.chordOf(chroma), key));
        }
    }

    @Test
    public void aDiminishedTriadIsNotReadAsTheMajorChordItNearlyIs() {
        // B-D-F is the leading-note triad of C major. B major is B-D#-F#, which
        // shares only the root, and G major is G-B-D, which shares two - so a
        // detector without a diminished template reports one of those and the
        // degree comes out plausibly wrong. This is the test that keeps the
        // template in.
        short[] passage = progression();
        Harmony.Key key = Harmony.keyOf(Harmony.chroma(passage, 0, passage.length, RATE));
        // B, D, F: a minor third and a diminished fifth, which is what makes it
        // the leading-note triad rather than B minor.
        short[] bdf = triad(246.94, MINOR_THIRD, 1.414214, 2);
        double[] chroma = Harmony.chroma(bdf, 0, bdf.length, RATE);
        // The key is what makes this readable at all: the same three pitch classes
        // are also A# major, and without the key both fit equally well.
        Harmony.Reading heard = Harmony.chordOf(chroma, key);
        assertEquals("read as " + heard.chord, 11, heard.root);
        assertEquals(Harmony.Quality.DIMINISHED, heard.quality);
        assertEquals("b 减三和弦", heard.name());
        assertEquals("\u2176\u00B0", Harmony.degreeOf(heard, key));
    }

    @Test
    public void theKeyIsATonicAndNothingElse() {
        // No major or minor: the two are the same seven notes, so choosing between
        // them on a chroma is a coin toss - and because relative keys have
        // different tonics, that coin toss moved every degree in the line, which
        // is what made a song without modulations look like it had several.
        assertEquals("C 调", new Harmony.Key(0).name());
        assertEquals("#C 调", new Harmony.Key(1).name());
    }

    @Test
    public void theKeyStaysPutUnlessTheEvidenceClearlyMoves() {
        short[] passage = progression();          // I-V-vi-IV in C
        double[] chroma = Harmony.chroma(passage, 0, passage.length, RATE);
        Harmony.Key c = new Harmony.Key(0);
        Harmony.Key g = new Harmony.Key(7);

        // A margin nobody can clear: the previous key stands, which is what stops
        // a song with no modulation from being described as changing key.
        assertEquals("G 调", Harmony.keyOf(chroma, g, 2.0).name());
        // No margin at all: the best fit wins, which is the old behaviour and the
        // one a real modulation has to be able to reach.
        assertEquals("C 调", Harmony.keyOf(chroma, g, 0.0).name());
        // And a previous key that is already the answer is simply kept.
        assertEquals("C 调", Harmony.keyOf(chroma, c, 2.0).name());
    }

    @Test
    public void theLineTheCardShowsSpellsTheQualityOut() {
        // "（F）" leaves the third to the case of one letter; the card says what
        // the chord is, and the degree numeral says the same thing in the other
        // notation - so the two halves check each other.
        Harmony.Key c = new Harmony.Key(0);
        Harmony.Reading f = new Harmony.Reading("F", 5, Harmony.Quality.MAJOR, 0.42, 0.30);
        assertEquals("C 调 \u2163 级（F 大三和弦）", Harmony.sentence(f, c));

        Harmony.Key a = new Harmony.Key(9);
        Harmony.Reading d = new Harmony.Reading("d", 2, Harmony.Quality.MINOR, 0.40, 0.20);
        // Lower-case numeral for a minor triad, which is the half of the notation
        // that repeats what the chord name already says.
        assertEquals("A 调 \u2173 级（d 小三和弦）", Harmony.sentence(d, a));

        Harmony.Key zero = new Harmony.Key(0);
        Harmony.Reading b = new Harmony.Reading("b", 11, Harmony.Quality.DIMINISHED, 0.50, 0.10);
        assertEquals("C 调 \u2176\u00B0 级（b 减三和弦）", Harmony.sentence(b, zero));
    }

    /** I - V - vi - IV in C major: the four chords the model was asked about. */
    private static short[] progression() {
        short[] a = triad(261.63, MAJOR_THIRD, 2);
        short[] b = triad(392.00, MAJOR_THIRD, 2);
        short[] c = triad(220.00, MINOR_THIRD, 2);
        short[] d = triad(349.23, MAJOR_THIRD, 2);
        short[] out = new short[a.length * 4];
        System.arraycopy(a, 0, out, 0, a.length);
        System.arraycopy(b, 0, out, a.length, b.length);
        System.arraycopy(c, 0, out, a.length * 2, c.length);
        System.arraycopy(d, 0, out, a.length * 3, d.length);
        return out;
    }
}
