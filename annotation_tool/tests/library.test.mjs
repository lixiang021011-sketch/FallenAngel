// Filename mapping is the only thing standing between "my music folder" and a
// usable library, so the rules that matter are pinned down here.

import test from "node:test";
import assert from "node:assert/strict";

import { classifyFiles, isAudioFile, partsForSong, titleFromFilename } from "../js/library.js";

function audio(name, { path = "", type = "audio/ogg" } = {}) {
  const file = new File([new Uint8Array([1, 2, 3])], name, { type });
  if (path) Object.defineProperty(file, "webkitRelativePath", { value: path });
  return file;
}

test("a single unstructured file becomes the mix, and is not also reported as ignored", () => {
  const file = audio("mecha_tribe.ogg");
  const out = classifyFiles([file]);
  assert.equal(out.masterFile, file);
  assert.deepEqual(out.stems, {});
  assert.deepEqual(out.unmatched, []);
});

test("explicit master naming still wins", () => {
  const out = classifyFiles([
    audio("Master.wav", { type: "audio/wav" }),
    audio("Guitar.wav", { type: "audio/wav" }),
    audio("Bass.wav", { type: "audio/wav" }),
  ]);
  assert.equal(out.masterFile.name, "Master.wav");
  assert.deepEqual(Object.keys(out.stems).sort(), ["Bass", "Guitar"]);
  assert.deepEqual(out.unmatched, []);
});

test("<song> + <song>_<part> naming maps the prefixed file to the mix", () => {
  const out = classifyFiles([
    audio("hitchcock.ogg"),
    audio("hitchcock_drums.ogg"),
    audio("hitchcock_bass.ogg"),
    audio("hitchcock_vocals.ogg"),
    audio("hitchcock_instruments.ogg"),
  ]);
  assert.equal(out.masterFile.name, "hitchcock.ogg");
  assert.deepEqual(Object.keys(out.stems).sort(), ["Bass", "Drums", "Instruments", "Vocal"]);
  assert.deepEqual(out.unmatched, []);
});

test("an unmatched stem suffix lands in a generic part instead of being dropped", () => {
  const out = classifyFiles([audio("song.ogg"), audio("song_whatever.ogg")]);
  assert.equal(out.masterFile.name, "song.ogg");
  assert.deepEqual(Object.keys(out.stems), ["Other"]);
});

test("a folder of loose tracks does not get mistaken for one multitrack", () => {
  const out = classifyFiles([
    audio("01 rain.ogg", { path: "music/01 rain.ogg" }),
    audio("02 harbour.ogg", { path: "music/02 harbour.ogg" }),
  ]);
  assert.ok(out.masterFile, "one file is taken as the mix");
  assert.equal(out.unmatched.length, 1, "the other is reported rather than guessed at");
});

test("two files claiming the same part keep one and report the other", () => {
  const out = classifyFiles([
    audio("mix.ogg"),
    audio("guitar_a.ogg"),
    audio("guitar_b.ogg"),
  ]);
  assert.deepEqual(Object.keys(out.stems), ["Guitar"]);
  assert.equal(out.unmatched.length, 1);
});

test("no mix anywhere leaves masterFile empty so the caller can refuse", () => {
  const out = classifyFiles([audio("song_drums.ogg"), audio("song_bass.ogg")]);
  assert.equal(out.masterFile, null);
  assert.deepEqual(Object.keys(out.stems).sort(), ["Bass", "Drums"]);
});

test("a mix-only song still offers all four parts to annotate", () => {
  const parts = partsForSong([]);
  assert.deepEqual(parts.map((p) => p.id), ["Guitar", "Bass", "Drums", "Piano"]);
  assert.equal(parts.every((p) => p.hasStem === false), true);
});

test("parts available in the song are marked as soloable, extras included", () => {
  const parts = partsForSong(["Bass", "Instruments"]);
  assert.deepEqual(
    parts.filter((p) => p.hasStem).map((p) => p.id),
    ["Bass", "Instruments"],
  );
});

test("audio detection accepts the formats a phone actually hands over", () => {
  assert.equal(isAudioFile(audio("a.mp3")), true);
  assert.equal(isAudioFile(audio("a.M4A")), true);
  assert.equal(isAudioFile(audio("a.flac")), true);
  assert.equal(isAudioFile(audio("notes.txt", { type: "text/plain" })), false);
  assert.equal(isAudioFile(audio("cover.jpg", { type: "image/jpeg" })), false);
});

test("titles come from filenames without the extension or underscore noise", () => {
  assert.equal(titleFromFilename("local_light_drums.ogg"), "local light drums");
  assert.equal(titleFromFilename("01 - Rain Song.mp3"), "01 - Rain Song");
});
