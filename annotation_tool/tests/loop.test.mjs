// When the player should wrap a loop.
//
// The wrap has to happen slightly *early*, not late: a browser reports the
// playhead every few frames, so waiting until it has passed the end means
// hearing the next bar's first note before the jump. A few tens of milliseconds
// early is inaudible; late is not.

import test from "node:test";
import assert from "node:assert/strict";

import { shouldWrap } from "../js/audio.js";

const LOOP = { from: 5, to: 11.2, repeat: true };

test("there is no wrap without a loop", () => {
  assert.equal(shouldWrap(null, 11.2), false);
  assert.equal(shouldWrap({ from: 5 }, 11.2), false);
  assert.equal(shouldWrap({ from: 5, to: 11.2 }, NaN), false);
});

test("the wrap happens at the end, not before it", () => {
  assert.equal(shouldWrap(LOOP, 11.0), false);
  assert.equal(shouldWrap(LOOP, 11.2), true);
  assert.equal(shouldWrap(LOOP, 12.0), true);
});

test("it fires a hair early, because being late is audible", () => {
  assert.equal(shouldWrap(LOOP, 11.17), true, "within the default tolerance");
  assert.equal(shouldWrap(LOOP, 11.10, { tolerance: 0.01 }), false);
});

test("a degenerate range is not a loop", () => {
  assert.equal(shouldWrap({ from: 11.2, to: 11.2 }, 11.2), false);
  assert.equal(shouldWrap({ from: 11.2, to: 5 }, 11.2), false);
});
