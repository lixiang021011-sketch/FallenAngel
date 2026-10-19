// Song library. Audio blobs live in IndexedDB so a song is picked once and then
// usable offline, forever — no re-uploading stems before every listen.

import { nowIso, uuid, baseName, stripExt } from "./util.js";
import { parseAnalysis } from "./analysis.js";

const DB_NAME = "fa_annot_audio";
const DB_VERSION = 1;
const STORE = "songs";

export const DEFAULT_PARTS = [
  { id: "Guitar", label: "Guitar" },
  { id: "Bass", label: "Bass" },
  { id: "Drums", label: "Drums" },
  { id: "Piano", label: "Piano" },
];

export const EXTRA_PARTS = [
  { id: "Vocal", label: "Vocal" },
  { id: "Strings", label: "Strings" },
  { id: "Synth", label: "Synth" },
  { id: "Instruments", label: "Instruments" },
  { id: "Other", label: "Other" },
];

const keyword = (words) => new RegExp(`(^|[^a-z])(${words.join("|")})([^a-z]|$)`, "i");

const MASTER_RX = keyword(["master", "mix", "full", "stereo"]);

// Ordered: the first rule that matches a name wins, so `lead_guitar` is a guitar
// and not a synth. `Instruments` is the generic bucket a 4-way split
// (vocals / drums / bass / instruments) puts everything else into.
const PART_RULES = [
  { id: "Guitar", words: ["guitar", "gtr"] },
  { id: "Bass", words: ["bass"] },
  { id: "Drums", words: ["drums", "drum", "drm", "percussion", "perc", "kit"] },
  { id: "Piano", words: ["piano", "keys", "keyboard", "pno"] },
  { id: "Vocal", words: ["vocals", "vocal", "vox", "voice"] },
  { id: "Strings", words: ["strings", "string", "violin", "cello", "orchestra"] },
  { id: "Synth", words: ["synth", "pad", "arp"] },
  { id: "Instruments", words: ["instruments", "instrumental", "band", "other"] },
].map((rule) => ({ id: rule.id, rx: keyword(rule.words) }));

const AUDIO_RX = /\.(wav|mp3|m4a|aac|ogg|oga|opus|flac|aiff?)$/i;

export function isAudioFile(file) {
  if (!file) return false;
  if (file.type && file.type.startsWith("audio/")) return true;
  return AUDIO_RX.test(file.name || "");
}

/** Path-less display name without its extension. */
function stemNameOf(file) {
  return stripExt(baseName(file.webkitRelativePath || file.name));
}

/**
 * Sort browser-picked files into master + stems by filename.
 * Anything unrecognised that could be a stem lands in `unmatched` so the UI can
 * say so instead of silently dropping it.
 */
export function classifyFiles(files) {
  const list = [...files];
  const entries = list.map((file) => {
    const name = stemNameOf(file);
    return { file, name, lower: name.toLowerCase() };
  });

  const master = [];
  const stems = {};
  const unmatched = [];
  const used = new Set();

  // 1. Anything explicitly named as the mix.
  for (const entry of entries) {
    if (MASTER_RX.test(entry.lower)) { master.push(entry.file); used.add(entry); }
  }

  // 2. Recognised part keywords.
  for (const entry of entries) {
    if (used.has(entry)) continue;
    const hit = PART_RULES.find((rule) => rule.rx.test(entry.lower));
    if (hit && !stems[hit.id]) { stems[hit.id] = entry.file; used.add(entry); }
  }

  // 3. `<song>_<part>` naming: the file whose name is exactly the shared prefix
  //    is the full mix. This is how `hitchcock.ogg` + `hitchcock_bass.ogg` reads.
  for (const entry of entries) {
    if (used.has(entry)) continue;
    let prefix = null;
    for (const other of entries) {
      if (other === entry || used.has(other)) continue;
      const rest = entry.lower.slice(other.lower.length);
      if (!entry.lower.startsWith(other.lower) || !/^[_\-\s]/.test(rest)) continue;
      if (!prefix || other.lower.length > prefix.lower.length) prefix = other;
    }
    if (!prefix) continue;

    if (!master.includes(prefix.file)) master.push(prefix.file);
    used.add(prefix);
    const suffix = entry.lower.slice(prefix.lower.length).replace(/^[_\-\s]+/, "");
    const hit = PART_RULES.find((rule) => rule.rx.test(suffix));
    const id = hit ? hit.id : "Other";
    if (!stems[id]) { stems[id] = entry.file; used.add(entry); }
  }

  // 4. Whatever is left. One stray file is a valid mix-only song; more than that
  //    is genuinely ambiguous, so say so rather than guessing.
  const leftovers = entries.filter((entry) => !used.has(entry));
  for (const entry of leftovers) used.add(entry);
  if (!master.length && leftovers.length) {
    master.push(leftovers[0].file);
    leftovers.shift();
  }
  for (const entry of leftovers) unmatched.push(entry.file);

  return { masterFile: master[0] || null, extraMasters: master.slice(1), stems, unmatched };
}

export function partsForSong(stemIds) {
  const have = new Set(stemIds);
  const out = DEFAULT_PARTS.map((p) => ({ ...p, hasStem: have.has(p.id) }));
  for (const p of EXTRA_PARTS) {
    if (have.has(p.id)) out.push({ ...p, hasStem: true });
  }
  return out;
}

export function suggestTitle(files) {
  const list = [...files];
  if (!list.length) return "";
  const rel = list[0].webkitRelativePath;
  if (rel && rel.includes("/")) return rel.split("/")[0];
  const name = stripExt(baseName(list[0].name));
  return name.replace(/[-_ ]*(master|mix|full|guitar|bass|drums|piano)$/i, "") || name;
}

/** Turn a filename into something readable as a song title. */
export function titleFromFilename(name) {
  const base = stripExt(baseName(name));
  const cleaned = base
    .replace(/[_]+/g, " ")
    .replace(/\s*-\s*/g, " - ")
    .replace(/\s{2,}/g, " ")
    .trim();
  return cleaned || base;
}

export function dedupeKey(name, size) {
  return `${String(name).toLowerCase()}|${size}`;
}

function openDb() {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, DB_VERSION);
    req.onupgradeneeded = () => {
      const db = req.result;
      if (!db.objectStoreNames.contains(STORE)) db.createObjectStore(STORE, { keyPath: "id" });
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

function tx(db, mode, fn) {
  return new Promise((resolve, reject) => {
    const t = db.transaction(STORE, mode);
    const store = t.objectStore(STORE);
    let result;
    try { result = fn(store); } catch (err) { reject(err); return; }
    t.oncomplete = () => resolve(result && result.result !== undefined ? result.result : result);
    t.onerror = () => reject(t.error);
    t.onabort = () => reject(t.error);
  });
}

function reqDone(req) {
  return new Promise((resolve, reject) => {
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

export class AudioLibrary {
  constructor() {
    this._db = null;
    this._memory = [];
  }

  async db() {
    if (!this._db) this._db = await openDb();
    return this._db;
  }

  async list() {
    try {
      const db = await this.db();
      const t = db.transaction(STORE, "readonly");
      const all = await reqDone(t.objectStore(STORE).getAll());
      this._memory = all;
      return all.sort((a, b) => (a.addedAt < b.addedAt ? 1 : -1));
    } catch (err) {
      console.warn("[library] IndexedDB unavailable, songs will not persist", err);
      return [...this._memory];
    }
  }

  async get(id) {
    try {
      const db = await this.db();
      const t = db.transaction(STORE, "readonly");
      return await reqDone(t.objectStore(STORE).get(id));
    } catch {
      return this._memory.find((s) => s.id === id) || null;
    }
  }

  /**
   * `strict` rethrows instead of silently falling back to memory. Bulk import
   * needs that: quietly keeping 300 songs in RAM and reporting success would
   * lose the library on the next reload.
   */
  async put(song, { strict = false } = {}) {
    try {
      const db = await this.db();
      await tx(db, "readwrite", (store) => store.put(song));
      return song;
    } catch (err) {
      console.warn("[library] could not persist song", err);
      if (strict) throw err;
      this._memory = this._memory.filter((s) => s.id !== song.id).concat([song]);
      return song;
    }
  }

  async remove(id) {
    try {
      const db = await this.db();
      await tx(db, "readwrite", (store) => store.delete(id));
    } catch { /* ignore */ }
    this._memory = this._memory.filter((s) => s.id !== id);
  }

  /** Build a storable song record from picked files. */
  async addFromFiles({ title, files, duration }) {
    const { masterFile, stems, unmatched } = classifyFiles(files);
    if (!masterFile) throw new Error("Pick at least one audio file to use as the master mix.");

    const stemFiles = {};
    for (const [partId, file] of Object.entries(stems)) stemFiles[partId] = file;

    const song = {
      id: uuid(),
      title: title.trim() || suggestTitle(files) || "Untitled",
      addedAt: nowIso(),
      source: "local-files",
      duration: Number(duration) || 0,
      parts: partsForSong(Object.keys(stemFiles)),
      files: {
        master: await toBlob(masterFile),
        masterName: masterFile.name,
        stems: await mapBlobs(stemFiles),
        stemNames: Object.fromEntries(Object.entries(stemFiles).map(([k, v]) => [k, v.name])),
      },
      notes: unmatched.length
        ? `Unmatched files ignored: ${unmatched.map((f) => f.name).join(", ")}`
        : null,
    };
    await this.put(song);
    return song;
  }

  /**
   * Demo songs are plain URLs; fetch and store them like anything else.
   *
   * An entry may name an `analysis` file. It is read here and handed to
   * `onAnalysis` rather than stored on the song: machine observations belong in
   * the store's analysis layer, not mixed into a song record. A missing or
   * broken analysis file is not an error - the song simply arrives without
   * structure.
   */
  async addFromManifest(url, { onAnalysis = null } = {}) {
    const res = await fetch(url, { cache: "no-store" });
    if (!res.ok) throw new Error(`manifest not found (${res.status}). Run the generator first.`);
    const manifest = await res.json();
    const base = url.replace(/[^/]*$/, "");
    const added = [];
    for (const entry of manifest.songs || []) {
      const masterBlob = await fetchBlob(base + entry.master);
      const stems = {};
      const stemNames = {};
      for (const [partId, path] of Object.entries(entry.stems || {})) {
        stems[partId] = await fetchBlob(base + path);
        stemNames[partId] = path;
      }
      const song = {
        id: entry.id || uuid(),
        title: entry.title || "Demo song",
        addedAt: nowIso(),
        source: "demo",
        duration: Number(entry.duration) || 0,
        parts: partsForSong(Object.keys(stems)),
        files: { master: masterBlob, masterName: `${entry.id}-master.wav`, stems, stemNames },
        notes: entry.notes || null,
      };
      await this.put(song);
      if (entry.analysis && typeof onAnalysis === "function") {
        const analysis = await fetchAnalysis(base + entry.analysis);
        if (analysis) onAnalysis(song.id, analysis);
      }
      added.push(song);
    }
    return added;
  }

  /**
   * Bulk import: every picked file becomes its own song, full mix only.
   *
   * This is the "listen to a lot of music" path — no stems, no guessing which
   * file is a mix. Titles come from filenames and duration is left at 0, because
   * measuring 300 files up front costs more than it is worth; the real duration
   * is captured when the song is actually loaded for listening.
   */
  async addManyFromFiles(files, { onProgress = () => {}, skipKeys = null } = {}) {
    const list = [...files].filter(isAudioFile);
    const existing = await this.list();
    const seen = new Set(
      skipKeys || existing.map((s) => dedupeKey(s.files?.masterName || "", s.files?.master?.size ?? -1)),
    );

    let added = 0;
    let skipped = 0;
    let quotaHit = false;
    const failures = [];

    for (let i = 0; i < list.length; i++) {
      const file = list[i];
      onProgress({ done: i, total: list.length, name: file.name, added, skipped });

      const key = dedupeKey(file.name, file.size);
      if (seen.has(key)) { skipped++; continue; }

      try {
        await this.put({
          id: uuid(),
          title: titleFromFilename(file.name),
          addedAt: nowIso(),
          source: "bulk",
          duration: 0,
          parts: partsForSong([]),
          files: { master: await toBlob(file), masterName: file.name, stems: {}, stemNames: {} },
          notes: null,
        }, { strict: true });
        seen.add(key);
        added++;
      } catch (err) {
        failures.push({ name: file.name, error: err.message || String(err) });
        if (/quota|storage/i.test(err.name + " " + err.message)) { quotaHit = true; break; }
      }
    }

    onProgress({ done: list.length, total: list.length, name: "", added, skipped, quotaHit });
    return { added, skipped, failures, quotaHit, considered: list.length };
  }

  async usage() {
    try {
      const est = await navigator.storage?.estimate?.();
      if (!est) return null;
      return { usage: est.usage || 0, quota: est.quota || 0 };
    } catch {
      return null;
    }
  }

  /** Ask the browser not to evict the library under storage pressure. */
  async requestPersistence() {
    try { return await navigator.storage?.persist?.() ?? false; } catch { return false; }
  }
}

async function toBlob(file) {
  if (file instanceof Blob) return file;
  return new Blob([file]);
}

async function mapBlobs(obj) {
  const out = {};
  for (const [k, v] of Object.entries(obj)) out[k] = await toBlob(v);
  return out;
}

async function fetchBlob(url) {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`could not fetch ${url} (${res.status})`);
  return res.blob();
}

/** A demo analysis, or null when it is missing or unreadable. */
async function fetchAnalysis(url) {
  try {
    const res = await fetch(url, { cache: "no-store" });
    if (!res.ok) return null;
    const { analysis, problems } = parseAnalysis(await res.text());
    if (problems.length) console.warn("[library] demo analysis has problems", problems);
    return analysis;
  } catch (err) {
    console.warn("[library] could not read the demo analysis", err);
    return null;
  }
}

/** Header-safe label for a song card. */
export function describeSong(song) {
  const stemCount = Object.keys(song.files?.stems || {}).length;
  const bits = [];
  if (stemCount) bits.push(`${stemCount} stems`);
  else bits.push("mix only");
  if (song.duration) bits.push(`${Math.round(song.duration)}s`);
  if (song.notes) bits.push(song.notes);
  return bits.join(" \u00b7 ");
}
