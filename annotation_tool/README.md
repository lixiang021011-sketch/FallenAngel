# Instrument Attention Annotator — V0.1

A mobile-first web prototype for one job: **while music plays normally, mark
which instrument parts are pulling your attention right now.**

It is a research instrument, not a music editor, and not part of FallenAngel
gameplay. The one-line success test from the spec:

> I can listen to music for an hour, produce annotations naturally, and never
> feel like I am doing data entry.

Everything lives outside `Assets/` — no Unity code, scenes, prefabs or settings
are touched by this folder.

## Run it

```bash
cd annotation_tool
python tools/make_demo_song.py   # once: writes demo/*.wav + demo/manifest.json
npm run serve                    # http://localhost:8099
```

The server prints a second URL for your phone. Open that on the phone (same wifi)
and use **Load demo song** to start immediately. `npm test` runs the data-model
tests.

## Using it

1. **Library** — add a song by picking audio files or a folder. Filenames decide
   the role: `master` / `mix` / `full` (or a single file) is the full mix; the
   others map by keyword to `guitar`, `bass`, `drums`, `piano`, and also
   `vocal`, `strings`, `synth`. Audio is stored on the device, so you only pick
   a song once.
   A `<song>` file plus `<song>_drums` / `<song>_bass` / … is recognised too:
   the file whose name is the shared prefix is the mix.
2. **Import a whole library** — *Add many songs (one file = one song)* takes a
   folder or a pile of tracks from the phone's storage and makes each file its
   own song. Duplicates are skipped by name and size, progress is shown, and if
   the browser's storage quota runs out the import stops and tells you how many
   were saved. Cards are then marked *to listen* / *✓ done*.
3. **Listen** — press play, then tap a part the moment it grabs you and tap
   again when it lets go. Several parts can be active at once. Nothing else is
   required; there is no chart and no waveform.
4. **Review** — finished sessions open straight into review: the attended
   spans, replay of a span on the full mix, solo of a single stem, and one free
   text note per span. **Export** writes the session as JSON; past sessions stay
   in the library under *Recent sessions*. **Next unannotated →** jumps straight
   into the next song without a finished session, which is how a long listening
   run works: end session → review → next.

### A bulk listening run

The loop is built for volume, not for one careful song:

- The library header shows **N / M songs**, how much music that covers, how many
  are still to listen and how many sessions have been recorded. Repeat listens
  count as sessions but never as extra coverage.
- When a session ends, the review screen offers the next song and **starts it
  automatically after 8 seconds**, playing — no taps per song.
- Any sign that you are still working in the review (picking a span, tapping
  into the note field, opening the export) **cancels the countdown**. A note is
  worth more than throughput, and nothing should move out from under your
  keyboard.
- **End → Next song** on the listening screen skips the review entirely when a
  song is clearly not worth a second look. The session is still recorded and
  still reviewable later.
- Songs are queued in import order, so a run works through the library the way
  it was added.

Autoplay after a countdown relies on the page having seen a gesture, which a
listening run always has. If a browser refuses anyway, the tool says so and
waits for play rather than silently sitting on a paused song.

### Mix-only songs

Most of a personal library has no stems. Those songs are annotated exactly the
same way — the four parts are attention labels, not audio channels — but Review
cannot solo a stem it does not have, so it says `(no stem)` and the header says
`full mix only`. The research question "does stem solo change the judgement"
therefore cannot be answered from mix-only data; run that comparison on a small
curated set that does have stems.

### What a phone browser cannot do

A web page cannot read or tap **what another app is playing**. There is no API
for the system now-playing state (`navigator.mediaSession` only publishes *this
page's* metadata to the lock screen) and no way to capture another app's audio
on mobile (`getDisplayMedia` audio is desktop-Chromium only). Reading the Music
app's own library is blocked for the same reason — those tracks are not exposed
as files. Songs have to arrive through the file picker.

On Android the picker reaches internal storage and the SD card. On iOS Safari it
reaches the Files app (iCloud Drive / On My iPhone), not Apple Music. Folder
selection needs Chromium; iOS Safari only offers multi-file selection.

## What the data means

- **Raw events are the truth.** Every tap is stored as it happened, with the
  song position at the moment of the gesture. Nothing is snapped to notes,
  beats or chart events, and no reaction-time correction is applied.
- **Regions are derived, never stored in place of events.** A span is built by
  pairing `attention_in` with `attention_out`, then splitting where the *set* of
  attended parts changes — so "Guitar" then "Guitar + Bass" then "Guitar" reads
  as three spans instead of one merged blob.
- **Moving the playhead closes what is open.** `seek`, `restart` and the end of
  a song write an `attention_out` at the pre-jump position with
  `autoClosed: true` and a `closeReason`, *before* the transport event.
- **Repeat listens are separate sessions.** Re-listening never overwrites; it is
  the data needed to ask whether attention is stable across listens.
- **Annotation is positive evidence only.** No annotation is not a negative
  label, and nothing in the schema implies one.

Event schema:

```json
{
  "eventId": "uuid",
  "sessionId": "uuid",
  "songId": "Song001",
  "audioTime": 42.813,
  "eventType": "attention_in",
  "partId": "Bass",
  "clientTime": "2026-10-05T14:30:42.123Z"
}
```

`eventType` is one of `attention_in`, `attention_out`, `play`, `pause`, `seek`,
`restart`, `song_end`, `exit`. Non-part events carry `partId: null`; `seek` also
carries `seekTo`. Export JSON is
`{ schema, exportedAt, session, rawEvents, derivedIntervals, derivedRegions, reviews }`.

## Local first

Writes go to local storage first and playback never waits on the network. Each
event is queued and uploaded in the background with exponential backoff, and the
queue survives a reload. The server de-duplicates by `eventId`, so a retry after
a timeout cannot create a second row.

The default backend is a **mock** that lives in the browser (nothing leaves the
device). Settings can point at a real API (`POST /api/v1/annotation/sessions`,
`/events`, `/sessions/{id}/end`, `/reviews`) and can force uploads to fail, which
is the fastest way to see the offline behaviour: the queue badge climbs, the
listening flow is unaffected, and it drains once uploads succeed again.

Audio is never uploaded. The annotation backend only ever sees song metadata,
sessions, raw events and review notes.

## Deliberately not built

Per the spec: no chart, no waveform, no STRUM integration, no beat or note
snapping, no AI analysis, no riff/fill/groove detection, no fixed annotation
taxonomy, no rating, no scoring, no dashboard, no accounts, no audio upload.

Not frozen either: point-versus-interval (V0.1 tests a plain toggle, and the
schema does not assume "attention is always an interval"), the meaning of
simultaneous parts (two lit buttons cannot distinguish two interesting parts
from an interesting interaction — that is what the free-text note is for), and
the part list (`partId` is a free string; the UI shows the four playable parts
plus any stem the song actually has).

## Layout

| Path | Role |
| --- | --- |
| `index.html`, `styles.css` | The three screens: library, listen, review |
| `js/app.js` | Wiring and screen state |
| `js/audio.js` | Master + stems on one clock, tap timing, solo/mix |
| `js/session.js` | Gestures to raw events, including auto-close on playhead moves |
| `js/derive.js` | Pure derivation: raw events to intervals and regions |
| `js/store.js` | Local-first session/event/note store and export |
| `js/api.js` | Backend interface, mock and HTTP implementations, upload queue |
| `js/library.js` | Song library (IndexedDB) and filename-to-stem mapping |
| `js/views.js` | DOM rendering |
| `tests/` | Node tests for the data semantics and the upload queue |
| `tools/make_demo_song.py` | Synthesized 4-stem demo song with known entry times |
| `tools/serve.mjs` | Zero-dependency static server for phone testing |

## Verification

- `npm test` — 25 tests covering region derivation, auto-close on
  seek/restart/end, timestamp fidelity, the raw-event schema, idempotent
  uploads and queue drain.
- Taps are timed from the pointer-down moment against a requestAnimationFrame
  clock, then converted to a song position; a tap is only committed on a clean
  release, so scrolling across the grid cannot create a phantom annotation.
- One seek gesture produces exactly one `seek` event: the live drag is a silent
  preview, and the commit records the position the gesture started from and the
  position it landed on.
