// Every id the code reaches for exists in the page.
//
// This project has no DOM test harness, and a mistyped id is silent: the button
// does nothing, the line stays blank, and the only way to find out is to press
// it. Reading the two files and comparing the sets is cheap and catches exactly
// that class of mistake.

import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = (p) => readFileSync(new URL(p, import.meta.url), "utf8");

function idsDefinedIn(html) {
  const ids = new Set();
  for (const m of html.matchAll(/\bid="([^"]+)"/g)) ids.add(m[1]);
  return ids;
}

function idsUsedIn(js) {
  const ids = new Set();
  for (const m of js.matchAll(/\$\(\s*"([^"]+)"\s*\)/g)) ids.add(m[1]);
  for (const m of js.matchAll(/getElementById\(\s*"([^"]+)"\s*\)/g)) ids.add(m[1]);
  return ids;
}

test("every element app.js looks for is in index.html", () => {
  const defined = idsDefinedIn(read("../index.html"));
  const used = idsUsedIn(read("../js/app.js"));
  const missing = [...used].filter((id) => !defined.has(id));
  assert.deepEqual(missing, [], `missing from index.html: ${missing.join(", ")}`);
});

test("every element the views look for is in index.html", () => {
  const defined = idsDefinedIn(read("../index.html"));
  const used = idsUsedIn(read("../js/views.js"));
  const missing = [...used].filter((id) => !defined.has(id));
  assert.deepEqual(missing, [], `missing from index.html: ${missing.join(", ")}`);
});

test("the structure layer's controls are all present", () => {
  const defined = idsDefinedIn(read("../index.html"));
  for (const id of [
    "structure", "structure-position", "structure-segment", "structure-loop",
    "btn-prev-bar", "btn-next-bar", "btn-loop-1", "btn-loop-4",
    "btn-replay-segment", "btn-similar", "btn-section-boundary",
    "machine", "machine-line", "machine-review",
    "btn-bnd-yes", "btn-bnd-no", "btn-bnd-ignore",
    "btn-pick-analysis", "analysis-input", "analysis-status",
  ]) {
    assert.ok(defined.has(id), `index.html has no #${id}`);
  }
});
