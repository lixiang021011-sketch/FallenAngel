// Small shared helpers. No dependencies.

export function uuid() {
  const c = globalThis.crypto;
  if (c && typeof c.randomUUID === "function") {
    try { return c.randomUUID(); } catch { /* insecure context */ }
  }
  const b = new Uint8Array(16);
  if (c && typeof c.getRandomValues === "function") {
    c.getRandomValues(b);
  } else {
    for (let i = 0; i < b.length; i++) b[i] = Math.floor(Math.random() * 256);
  }
  b[6] = (b[6] & 0x0f) | 0x40; // version 4
  b[8] = (b[8] & 0x3f) | 0x80; // variant 10
  const h = Array.from(b, (x) => x.toString(16).padStart(2, "0")).join("");
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

export function nowIso() {
  return new Date().toISOString();
}

/** Round to milliseconds — the resolution we promise in the schema. */
export function roundMs(t) {
  return Math.round((Number(t) || 0) * 1000) / 1000;
}

/** mm:ss.d — the format used in the spec (`01:12.4`). */
export function formatClock(sec, tenths = true) {
  const scale = tenths ? 10 : 1;
  let total = Math.round((Number(sec) || 0) * scale) / scale;
  if (!Number.isFinite(total) || total < 0) total = 0;
  const m = Math.floor(total / 60);
  const s = total - m * 60;
  const ss = s.toFixed(tenths ? 1 : 0).padStart(tenths ? 4 : 2, "0");
  return `${String(m).padStart(2, "0")}:${ss}`;
}

export function formatRange(a, b) {
  return `${formatClock(a)} \u2013 ${formatClock(b)}`;
}

/** Coarse human duration for summaries: "1h 12m", "48 min", "32 s". */
export function formatDurationShort(sec) {
  const total = Math.max(0, Math.round(Number(sec) || 0));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  if (hours) return `${hours}h ${minutes}m`;
  if (minutes) return `${minutes} min`;
  return `${total} s`;
}

/**
 * Cross-browser high-resolution timestamp of an input event, in the same time
 * origin as `performance.now()`. Older Safari reports epoch milliseconds.
 */
export function eventPerfTime(ev) {
  const ts = ev && typeof ev.timeStamp === "number" ? ev.timeStamp : 0;
  const now = performance.now();
  if (!ts) return now;
  if (ts > 1e12) {
    const origin = performance.timeOrigin || 0;
    const mapped = ts - origin;
    if (Number.isFinite(mapped) && mapped > 0 && mapped < now + 5000) return mapped;
    return now;
  }
  if (ts > now + 5000) return now; // different origin, don't guess
  return ts;
}

export function clamp(v, lo, hi) {
  return Math.min(hi, Math.max(lo, v));
}

export function el(tag, props = {}, children = []) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(props)) {
    if (v === undefined || v === null || v === false) continue;
    if (k === "class") node.className = v;
    else if (k === "text") node.textContent = v;
    else if (k === "html") node.innerHTML = v;
    else if (k === "dataset") Object.assign(node.dataset, v);
    else if (k.startsWith("on") && typeof v === "function") node.addEventListener(k.slice(2), v);
    else if (k === "style" && typeof v === "object") Object.assign(node.style, v);
    else node.setAttribute(k, v === true ? "" : String(v));
  }
  for (const c of [].concat(children)) {
    if (c === null || c === undefined || c === false) continue;
    node.append(c instanceof Node ? c : document.createTextNode(String(c)));
  }
  return node;
}

export function debounce(fn, ms) {
  let t = 0;
  return (...args) => {
    clearTimeout(t);
    t = setTimeout(() => fn(...args), ms);
  };
}

export function downloadJson(filename, data) {
  const blob = new Blob([JSON.stringify(data, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.rel = "noopener";
  document.body.append(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 4000);
}

export function delay(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

export function baseName(path) {
  const s = String(path || "");
  const i = Math.max(s.lastIndexOf("/"), s.lastIndexOf("\\"));
  return i >= 0 ? s.slice(i + 1) : s;
}

export function stripExt(name) {
  return String(name || "").replace(/\.[a-z0-9]+$/i, "");
}
