#!/usr/bin/env python3
"""Export a pretrained musicnn checkpoint to ONNX, and check it is the same model.

musicnn ships its weights inside its own wheel: MSD_musicnn is 9.5 MB of a TF1
checkpoint, not a 6 GB download. What it does not ship is an ONNX file, and
TensorFlow will not run on the Python this machine had by default - which is
why this runs in its own environment (see the notes at the bottom).

The graph is exported *from the mel spectrogram*, not from audio: the frontend
is ours (see LogMel and FeatureSpec in the app), and putting a second, silently
different mel implementation inside the file would be a way to get answers that
disagree with the ones the app computes.

The shape is the one musicnn actually feeds: [batch, time=187, freq=96], which
is the opposite way round from the usual mel convention - a detail worth having
read out of the source rather than assumed.

    python tools/export_musicnn.py --out app/src/main/assets/models/musicnn_msd.onnx
"""

import argparse
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_PACKAGE = os.environ.get("MUSICNN_PACKAGE", r"D:\musicnn_export\musicnn_pkg")

# The contract, as read from musicnn's own configuration and extractor.
SR = 16000
N_FFT = 512
HOP = 256
N_MELS = 96
N_FRAMES = 187
N_CLASSES = 50
TOP_HZ = None            # librosa's default: the nyquist


def load_musicnn(package: str):
    """Import the package from where it was unpacked, not from site-packages."""
    sys.path.insert(0, package)
    from musicnn import models  # noqa: E402
    return models


def build_graph(models, checkpoint: str):
    import tensorflow as tf

    # musicnn is TensorFlow 1 code, and its batch-norm layers create resource
    # variables under TF2's default behaviour. Freezing those leaves
    # ReadVariableOp nodes in the graph, which the ONNX converter then cannot
    # resolve. Asking for v1 behaviour makes the variables ordinary ones that
    # freeze into constants, which is what an exported inference graph wants.
    tf.compat.v1.disable_v2_behavior()
    graph = tf.Graph()
    with graph.as_default():
        mel = tf.compat.v1.placeholder(tf.float32, [None, N_FRAMES, N_MELS], name="mel")
        # is_training is passed as a plain False, not a placeholder: inference
        # wants dropout off and batch-norm on its moving statistics, and a
        # boolean input on the exported graph would be one more thing the phone
        # could get wrong. The layers accept a Python bool and decide statically.
        # The signature is (x, is_training, model, num_classes): the model name is
        # the third argument, not the first.
        outputs = models.define_model(mel, False, "MSD_musicnn", N_CLASSES)
        # define_model returns (logits, timbral, temporal, cnn1, cnn2, cnn3,
        # mean_pool, max_pool, penultimate) - the two worth exporting are the tag
        # logits and the penultimate layer, which is the embedding.
        logits, penultimate = outputs[0], outputs[-1]
        logits = tf.identity(logits, name="tags_logits")
        penultimate = tf.identity(penultimate, name="embedding")
        with tf.compat.v1.Session(graph=graph) as session:
            tf.compat.v1.train.Saver().restore(session, checkpoint)
            frozen = tf.compat.v1.graph_util.convert_variables_to_constants(
                session, graph.as_graph_def(), ["tags_logits", "embedding"])
    return frozen


def mel_frontend(audio: np.ndarray) -> np.ndarray:
    """The frontend musicnn uses, in the layout and scaling it uses."""
    import librosa

    mel = librosa.feature.melspectrogram(y=audio, sr=SR, hop_length=HOP,
                                         n_fft=N_FFT, n_mels=N_MELS).T
    mel = mel.astype(np.float16).astype(np.float32)
    return np.log10(10000 * mel + 1)


def clip_from(path: str) -> np.ndarray:
    """Three seconds of a raw 44.1 kHz mono PCM file, at musicnn's rate."""
    import librosa

    raw = np.fromfile(path, dtype="<i2").astype(np.float32) / 32768.0
    if raw.size < SR * 3:
        raise SystemExit(f"{path} is shorter than three seconds")
    start = raw.size // 2 - int(SR * 1.5)
    taken = raw[start:start + int(44100 * 3)]
    return librosa.resample(taken, orig_sr=44100, target_sr=SR)


def write_frontend(path: str) -> None:
    """The window and mel filters, as data, for the app to read instead of guess.

    This is the part of musicnn the app cannot work out for itself. Its mel is
    librosa's, which is Slaney-normalised triangles over the htk mel scale, with
    a periodic hann window - and a mel this project derives by hand is not that:
    measured against it, the total filter weight came out 80 times too large and
    the per-band error varied by a factor of three. So they are exported.

    Seven significant digits, because the file is read by a parser rather than a
    linker: that keeps it a document somebody can read instead of a few hundred
    kilobytes of digits, and it moves the mel by less than one part in a million.
    """
    import json

    import librosa

    window = librosa.filters.get_window("hann", N_FFT, fftbins=True)
    filters = librosa.filters.mel(sr=SR, n_fft=N_FFT, n_mels=N_MELS)

    def numbers(values) -> str:
        return "[" + ",".join("%.7g" % value for value in values) + "]"

    body = ",\n".join(
        [
            '"_comment": "The window and mel filters this model was trained with. '
            'Read, not re-derived: see tools/export_musicnn.py."',
            '"source": "librosa melspectrogram(sr=16000, n_fft=512, hop_length=256, '
            "n_mels=96, htk=False, norm='slaney', center=True, pad_mode='constant')\"",
            '"mels": %d' % N_MELS,
            '"fft": %d' % N_FFT,
            '"hop": %d' % HOP,
            '"center": true',
            '"window": ' + numbers(window),
            '"filters": [\n' + ",\n".join(numbers(row) for row in filters) + "\n]",
        ]
    )
    with open(path, "w", encoding="utf-8") as out:
        out.write("{\n" + body + "\n}\n")
    print("wrote", path, "%.0f KB" % (os.path.getsize(path) / 1024))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", default="app/src/main/assets/models/musicnn_msd.onnx")
    parser.add_argument("--frontend",
                        default="app/src/main/assets/models/musicnn_msd.frontend.json",
                        help="where to write the window and mel filters the model was "
                             "trained with, which the app reads rather than deriving")
    parser.add_argument("--package", default=DEFAULT_PACKAGE,
                        help="where the musicnn package was unpacked")
    parser.add_argument("--clip", default=None,
                        help="a .raw PCM file to check the exported model on")
    args = parser.parse_args()

    checkpoint = os.path.join(args.package, "musicnn", "MSD_musicnn", "model.ckpt")
    if not os.path.exists(checkpoint + ".index"):
        found = os.path.join(args.package, "musicnn", "MSD_musicnn")
        names = [n for n in os.listdir(found) if n.endswith(".index")]
        if not names:
            raise SystemExit(f"no checkpoint in {found}")
        checkpoint = os.path.join(found, names[0][:-len(".index")])

    import tensorflow as tf
    print("tensorflow", tf.__version__)
    models = load_musicnn(args.package)
    frozen = build_graph(models, checkpoint)
    print("frozen graph:", len(frozen.node), "nodes")

    import tf2onnx
    # The converter keys its tensors by "node:port" - passing the bare node names
    # gets you "Inputs/Outputs Not Found" rather than a helpful message.
    model, _ = tf2onnx.convert.from_graph_def(
        frozen,
        input_names=["mel:0"],
        output_names=["tags_logits:0", "embedding:0"],
        # ...and it carries the ":0" through into the ONNX names, which would
        # leave the phone feeding an input called "mel:0". Rename on the way out.
        tensors_to_rename={"mel:0": "mel",
                           "tags_logits:0": "tags_logits",
                           "embedding:0": "embedding"},
        opset=15,
    )
    import onnx
    onnx.checker.check_model(model)
    onnx.save(model, args.out)
    print("wrote", args.out, f"{os.path.getsize(args.out)/1e6:.1f} MB")
    write_frontend(args.frontend)

    if not args.clip:
        print("no --clip given, so the export is unverified")
        return 0

    # The check that matters, in two halves: the exported file has to agree with
    # the model it came from, and it has to say something sensible about music.
    import onnxruntime as ort
    audio = clip_from(args.clip)
    patch = mel_frontend(audio)[:N_FRAMES][None, :, :].astype(np.float32)
    session = ort.InferenceSession(args.out)
    inputs = [i.name for i in session.get_inputs()]
    if inputs != ["mel"]:
        raise SystemExit(f"the exported file asks for {inputs}, not just ['mel']")
    onnx_logits, onnx_embedding = session.run(None, {"mel": patch})

    with tf.Graph().as_default() as verify:
        tf.import_graph_def(frozen, name="")
        with tf.compat.v1.Session(graph=verify) as session:
            tf_logits, tf_embedding = session.run(
                ["tags_logits:0", "embedding:0"], {"mel:0": patch})

    print()
    print("patch shape      :", patch.shape)
    print("embedding shape  :", onnx_embedding.shape,
          " L2 =", round(float(np.linalg.norm(onnx_embedding)), 3))
    print("tf vs onnx logits: max |delta| =",
          f"{float(np.max(np.abs(tf_logits - onnx_logits))):.6f}")
    print("tf vs onnx embed : max |delta| =",
          f"{float(np.max(np.abs(tf_embedding - onnx_embedding))):.6f}")

    from musicnn import configuration as config
    tags = 1 / (1 + np.exp(-onnx_logits[0]))
    best = np.argsort(-tags)[:8]
    print("top tags         :",
          [(config.MSD_LABELS[i], round(float(tags[i]), 3)) for i in best])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
