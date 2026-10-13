#!/usr/bin/env python3
"""Build the placeholder event model, as a hand-written ONNX graph.

The real model does not exist yet, but the chain around it does, and a chain
that is only exercised by "it returned four numbers" is not exercised at all.
So this placeholder is deliberately *readable*: its four outputs are the four
things the engine can report, computed from the log-mel clip with plain
reductions.

    onset    overall energy in the clip
    kick     energy in the low mel bands
    snare    energy in the high mel bands
    novelty  how much the spectrogram changes from frame to frame

It is not a model of music. It is a model of the *contract*: the input shape,
the output shape, and what the outputs mean. Replacing it with a trained
network replaces this file and the adapter's thresholds, and nothing above.

The graph is written out node by node rather than exported from a framework:
the file is then the specification, and it can be read by anyone who has to
match it later.

    python tools/make_placeholder_model.py app/src/main/assets/models/placeholder-events.onnx
"""

import sys

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper

MELS = 64
FRAMES = 64


def main() -> int:
    out = sys.argv[1] if len(sys.argv) > 1 else "placeholder-events.onnx"
    nodes = []

    def reduce_over_clip(source: str, name: str, op: str) -> str:
        nodes.append(helper.make_node(
            op, [source], [name], axes=[1, 2, 3], keepdims=0))
        return name

    def band(name: str, start: int, end: int) -> str:
        nodes.append(helper.make_node(
            "Slice",
            ["logmel", "starts_" + name, "ends_" + name, "axes_" + name, "steps_" + name],
            ["slice_" + name]))
        # The strongest band in that register, not the average of it: the clip is
        # normalised, and an average over a quarter of the bands is dominated by
        # the ones that are silent. A peak survives normalisation; an average
        # does not.
        return reduce_over_clip("slice_" + name, name, "ReduceMax")

    low = band("low", 0, MELS // 4)
    high = band("high", MELS - MELS // 4, MELS)
    # There is no separate "energy" output: with a four-way softmax a loudness
    # term wins every clip and the band outputs are never heard. The onset class
    # is the low-band measure, so a low drum reports both - which is what it is.

    for name, start, end in (("shift", 1, FRAMES), ("rest", 0, FRAMES - 1)):
        nodes.append(helper.make_node(
            "Slice",
            ["logmel", "starts_" + name, "ends_" + name, "axes_" + name, "steps_" + name],
            ["slice_" + name]))
    nodes.append(helper.make_node("Sub", ["slice_shift", "slice_rest"], ["delta"]))
    nodes.append(helper.make_node("Abs", ["delta"], ["delta_abs"]))
    # Change is averaged: how much the picture moves, not how loud one band got.
    novelty = reduce_over_clip("delta_abs", "novelty", "ReduceMean")

    nodes.append(helper.make_node("Mul", [novelty, "two"], ["novelty_x2"]))
    for index, source in enumerate(["low", "low", "high", "novelty_x2"]):
        # Opset 13 moved Unsqueeze's axes from an attribute to an input.
        nodes.append(helper.make_node(
            "Unsqueeze", [source, "one_axis"], ["col" + str(index)]))
    nodes.append(helper.make_node(
        "Concat", ["col0", "col1", "col2", "col3"], ["logits"], axis=1))

    initialisers = [
        numpy_helper.from_array(np.asarray(2.0, dtype=np.float32), "two"),
        numpy_helper.from_array(np.asarray([1], dtype=np.int64), "one_axis"),
    ]
    for name, start, end, axis in (
            ("low", 0, MELS // 4, 2), ("high", MELS - MELS // 4, MELS, 2),
            ("shift", 1, FRAMES, 3), ("rest", 0, FRAMES - 1, 3)):
        initialisers.append(numpy_helper.from_array(
            np.asarray([start], dtype=np.int64), "starts_" + name))
        initialisers.append(numpy_helper.from_array(
            np.asarray([end], dtype=np.int64), "ends_" + name))
        initialisers.append(numpy_helper.from_array(
            np.asarray([axis], dtype=np.int64), "axes_" + name))
        initialisers.append(numpy_helper.from_array(
            np.asarray([1], dtype=np.int64), "steps_" + name))

    graph = helper.make_graph(
        nodes, "placeholder-events",
        [helper.make_tensor_value_info("logmel", TensorProto.FLOAT, [1, 1, MELS, FRAMES])],
        [helper.make_tensor_value_info("logits", TensorProto.FLOAT, [1, 4])],
        initializer=initialisers,
    )
    model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 15)],
                              producer_name="listening_ai")
    model.ir_version = 8
    onnx.checker.check_model(model)
    onnx.save(model, out)
    print("wrote " + out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
