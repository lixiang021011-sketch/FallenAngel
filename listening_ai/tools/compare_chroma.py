"""Is the chord reader wrong in its ear, or in its reasoning?

`Harmony` does two things that can each be wrong on their own: it turns audio into
twelve numbers (chroma), and it turns those twelve numbers into a chord and a
degree. When it reports the same chord for thirty seconds of music that changes
every bar, there are two very different explanations and only one of them is worth
rewriting.

This separates them by swapping out the first half and keeping the second:

    my chroma      + these templates   -> what the app says
    librosa chroma + these templates   -> what it would say with a known-good ear

If the second one moves and the first one does not, the fault is in the chroma. If
both sit still, it is in the templates or the key estimate - or the music really
is one chord, which happens and is not a bug.

Run it with the environment that has librosa (the musicnn export one):

    D:\\musicnn_export\\Scripts\\python.exe tools\\compare_chroma.py \\
        ..\\overlay_beat\\tools\\raw\\local_chiharu.raw 44 76 4
"""

import math
import re
import subprocess
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent / "qwen"))

RATE = 44100
JAVA = r"D:\unity\jdk-17\bin\java.exe"
CLASSES = Path(__file__).resolve().parent / "out"

PITCH = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
DEGREE = ["I", "bII", "II", "bIII", "III", "IV", "#IV", "V", "bVI", "VI", "bVII", "VII"]
MAJOR_PROFILE = np.array([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88])
MINOR_PROFILE = np.array([6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17])


def read_raw(path):
    """The fixtures are headerless 44.1 kHz mono s16le."""
    data = np.fromfile(path, dtype="<i2").astype(np.float32) / 32768.0
    return data


def my_chroma(path, start, seconds):
    """The app's own chroma, taken from the tool that runs the app's own code."""
    done = subprocess.run(
        [JAVA, "-Dfile.encoding=UTF-8", "-cp", str(CLASSES), "HarmonyDump",
         str(path), str(start), str(start + seconds), str(seconds), "verbose"],
        capture_output=True, text=True, encoding="utf-8", errors="replace")
    for line in done.stdout.splitlines():
        match = re.search(r"\[(.*?)\]", line)
        if not match:
            continue
        values = {}
        for pair in match.group(1).split():
            pitch = re.match(r"([A-G]#?)([\d.]+)", pair)
            if pitch:
                values[pitch.group(1)] = float(pitch.group(2))
        if len(values) == 12:
            return np.array([values[p] for p in PITCH])
    raise SystemExit("could not read a chroma out of HarmonyDump:\n" + done.stdout[-400:])


def librosa_chroma(pcm, start, seconds):
    """The reference ear: a constant-Q chroma, which is what the app's FFT version
    is trying to approximate with much less machinery."""
    import librosa

    first = int(start * RATE)
    window = pcm[first:first + int(seconds * RATE)]
    chroma = librosa.feature.chroma_cqt(y=window, sr=RATE, hop_length=2048)
    value = chroma.mean(axis=1)
    return value / max(value.max(), 1e-9)


def key_of(chroma):
    """Krumhansl, the same way Harmony does it."""
    best = None
    for tonic in range(12):
        for profile, major in ((MAJOR_PROFILE, True), (MINOR_PROFILE, False)):
            rotated = np.roll(chroma, -tonic)
            score = np.corrcoef(rotated, profile)[0, 1]
            if best is None or score > best[0]:
                best = (score, tonic, major)
    return best[1], best[2]


def chord_of(chroma, tonic, major, bonus=0.05):
    """The same templates and the same in-key bonus as Harmony."""
    total = chroma.sum()
    if total <= 0:
        return "-", 0.0
    best = None
    for root in range(12):
        for third, fifth, quality in ((4, 7, "M"), (3, 7, "m"), (3, 6, "d")):
            score = sum(chroma[(root + i) % 12] for i in (0, third, fifth)) / total
            step = (root - tonic) % 12
            if major:
                in_key = ((step in (0, 5, 7) and quality == "M")
                          or (step in (2, 4, 9) and quality == "m")
                          or (step == 11 and quality == "d"))
            else:
                in_key = ((step in (0, 5, 7) and quality == "m")
                          or (step in (3, 8, 10) and quality == "M")
                          or (step == 2 and quality == "d"))
            if in_key:
                score += bonus
            if best is None or score > best[0]:
                best = (score, root, quality)
    score, root, quality = best
    numeral = DEGREE[(root - tonic) % 12]
    if quality != "M":
        numeral = numeral.lower()
    if quality == "d":
        numeral += "deg"
    return numeral, score


def main():
    if len(sys.argv) < 4:
        print(__doc__)
        return
    path = Path(sys.argv[1])
    start = float(sys.argv[2])
    stop = float(sys.argv[3])
    window = float(sys.argv[4]) if len(sys.argv) > 4 else 4.0

    pcm = read_raw(path)
    print(f"{path.name}  {start:.0f}-{stop:.0f}s  window {window:.0f}s")
    print("   window | mine (key, degree)          | librosa (key, degree)")

    mine_all, ref_all = [], []
    for at in np.arange(start, stop - window + 1e-9, window):
        mine = my_chroma(path, at, window)
        ref = librosa_chroma(pcm, at, window)
        mine_all.append(mine)
        ref_all.append(ref)

    # The key from the whole range, as the app does it over a whole song.
    mine_key = key_of(np.sum(mine_all, axis=0))
    ref_key = key_of(np.sum(ref_all, axis=0))
    print(f"   key over the range: mine {PITCH[mine_key[0]]}"
          f"{'maj' if mine_key[1] else 'min'}   librosa {PITCH[ref_key[0]]}"
          f"{'maj' if ref_key[1] else 'min'}")

    for at, mine, ref in zip(np.arange(start, stop - window + 1e-9, window), mine_all, ref_all):
        mc, ms = chord_of(mine, *mine_key)
        rc, rs = chord_of(ref, *ref_key)
        shape = float(np.dot(mine, ref) / (np.linalg.norm(mine) * np.linalg.norm(ref) + 1e-9))
        print(f"   {at:6.1f}s | {mc:5s} {ms:.2f}  (agreement {shape:.2f}) | {rc:5s} {rs:.2f}")


if __name__ == "__main__":
    main()
