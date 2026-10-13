"""Ask the local Qwen what it hears, on this machine, without a network.

Why this exists
---------------
`listening_ai` has two halves that answer two different questions:

    musicnn   what is this, and how different is it from a moment ago
    the model why it might matter

The second half has been a cloud call (`clip/ExplanationClient.java`) since the
beginning, which means the app stops asking good questions the moment there is
no network, no key, or no credit. This is the same question asked of a model
that lives on the desk instead. Qwen2.5-Omni rather than a text-only Qwen on
purpose: the clip goes in, not a description of the clip, because the whole
point of the question is what the audio actually does.

It is deliberately outside the app: the phone cannot hold a 4.5 GB model, and
the app already knows how to talk to an OpenAI-shaped endpoint, so the local
model is served over one (see serve.ps1) and the app's endpoint field is
pointed at it. Nothing in the Android code has to change for that to work, and
the answer is parsed by the parser that is already there.

What it does not do
-------------------
It does not decide anything. The local policy still decides when to ask; this
only answers, and an answer that is useless is recorded as useless, exactly
like the cloud one.

Usage
-----
    python qwen_listen.py ask     --audio song.wav --start 42 --seconds 6 \
                                  --prompt "这段音乐在做什么"
    python qwen_listen.py explain --audio song.wav --start 42 --facts facts.txt
    python qwen_listen.py sweep   --audio song.wav --start 30 --to 60 --step 5 \
                                  --prompt "这里有没有换段" --jsonl out.jsonl

`--audio` takes a WAV of any rate/channel count, or a headerless 44.1 kHz mono
s16le `.raw` (the fixtures under overlay_beat/tools/raw are that). The window is
cut and converted to the 16 kHz mono that the model's audio frontend wants, in
a temp file that is thrown away afterwards.
"""

import argparse
import array
import json
import math
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import wave

MODEL_RATE = 16000  # what the model's audio encoder is built for
RAW_RATE = 44100    # the sample rate of the .raw fixtures this repo keeps

# The same sentences the app sends to the cloud model, so that a local answer and
# a cloud answer to the same moment are comparable. Kept verbatim from
# ExplanationClient.SYSTEM; if that changes, this has to change with it.
SYSTEM = (
    "你在帮一个人研究「为什么某个音乐瞬间会让人停下来听」。"
    "我会给你一段几秒的音频，以及本地分析已经测到的事实。"
    "请给出 3 到 5 个候选解释：这个人刚才可能注意到的是哪一件事。"
    "要求："
    "每个解释都要短（不超过 12 个字），是音乐上的具体事情，"
    "不要笼统地说「变化」「感觉」；"
    "必须能被这段音频支持，不要编造本地事实里没有提到的乐器或事件；"
    "confidence 是 0 到 1 的把握，不要都写同一个数；"
    "只输出 JSON，形如 "
    "{\"candidates\":[{\"id\":\"c1\",\"label\":\"…\","
    "\"confidence\":0.6,\"evidence\":\"一句话说明依据\"}]}"
)


def _sinc(x):
    if x == 0.0:
        return 1.0
    return math.sin(math.pi * x) / (math.pi * x)


def resample(samples, src_rate, dst_rate):
    """Windowed-sinc resample, mono float in, mono float out.

    Written out rather than pulled from a library because the only audio work
    this tool does is this one conversion, and a dependency for it would have to
    be installed on a machine with 1.5 GB free on C:.
    """
    if src_rate == dst_rate or not samples:
        return list(samples)
    ratio = dst_rate / src_rate
    # Below 0.5 cycles/input-sample in the input's own units; 0.475 leaves the
    # top of the band alone rather than folding it back down as aliasing.
    fc = 0.475 * min(1.0, ratio)
    half = 16
    n_out = int(len(samples) * ratio)
    out = [0.0] * n_out
    for i in range(n_out):
        t = i / ratio
        first = int(math.floor(t))
        acc = 0.0
        wsum = 0.0
        for k in range(first - half, first + half + 1):
            if k < 0 or k >= len(samples):
                continue
            d = t - k
            w = 2.0 * fc * _sinc(2.0 * fc * d) * _sinc(d / (half + 1))
            acc += samples[k] * w
            wsum += w
        out[i] = acc / wsum if wsum else 0.0
    return out


def read_wav(path):
    with wave.open(path, 'rb') as w:
        width = w.getsampwidth()
        channels = w.getnchannels()
        rate = w.getframerate()
        frames = w.readframes(w.getnframes())
    if width == 2:
        a = array.array('h')
        a.frombytes(frames)
        peak = 32768.0
    elif width == 4:
        a = array.array('i')
        a.frombytes(frames)
        peak = 2147483648.0
    elif width == 1:
        a = array.array('B')
        a.frombytes(frames)
        peak = 128.0
    else:
        raise SystemExit("unsupported sample width: %d bytes" % width)
    if sys.byteorder == 'big':
        a.byteswap()
    mono = []
    if channels == 1:
        mono = [v / peak for v in a]
    else:
        for i in range(0, len(a) - channels + 1, channels):
            mono.append(sum(a[i:i + channels]) / (channels * peak))
    return mono, rate


def read_raw(path, rate=RAW_RATE):
    with open(path, 'rb') as f:
        a = array.array('h')
        a.frombytes(f.read())
    if sys.byteorder == 'big':
        a.byteswap()
    return [v / 32768.0 for v in a], rate


def load_any(path):
    if path.lower().endswith('.raw'):
        return read_raw(path)
    return read_wav(path)


def write_wav16(path, samples):
    a = array.array('h', (int(max(-1.0, min(1.0, v)) * 32767) for v in samples))
    if sys.byteorder == 'big':
        a.byteswap()
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(MODEL_RATE)
        w.writeframes(a.tobytes())


def cut(path, start, seconds):
    """A window of the file as 16 kHz mono, in a temp wav the model can take."""
    samples, rate = load_any(path)
    first = int(start * rate)
    count = int(seconds * rate) if seconds > 0 else len(samples) - first
    window = samples[first:first + count]
    if not window:
        raise SystemExit("the window is past the end of the file (%s)" % (len(samples) / rate,))
    window = resample(window, rate, MODEL_RATE)
    handle, clip = tempfile.mkstemp(prefix='qwen-clip-', suffix='.wav')
    os.close(handle)
    write_wav16(clip, window)
    return clip, len(window) / MODEL_RATE


def ask(args, prompt, audio_path, system=None, seed=None, n_predict=None):
    """One call. Returns (text, seconds spent, the whole stderr, for logging)."""
    cmd = [args.cli, '-m', args.model, '--mmproj', args.mmproj, '--audio', audio_path,
           '-n', str(n_predict or args.predict), '--temp', str(args.temp),
           '--no-warmup', '-ngl', str(args.ngl), '--jinja']
    if system:
        cmd += ['-sys', system]
    if seed is not None:
        cmd += ['-s', str(seed)]
    cmd += ['-p', prompt]
    started = time.time()
    done = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace',
                          timeout=args.timeout)
    spent = time.time() - started
    text = done.stdout or ''
    # In single-completion mode the prompt is echoed before the answer; the
    # answer is what comes after it.
    if prompt and prompt in text:
        text = text.split(prompt, 1)[1]
    # Anything the model prints as a chat turn arrives wrapped in its own
    # template's turn markers; they are not part of the answer.
    text = re.sub(r'<\|[^|>]*\|>', '', text).strip()
    return text, spent, done.stderr or ''


def parse_candidates(answer):
    """Read the answer the way ExplanationClient.parse does.

    Deliberately the same shape test as the app: a local model whose output the
    app cannot parse is not an improvement, it is a second bug.
    """
    text = answer.strip()
    if text.startswith('```'):
        text = re.sub(r'^```[a-zA-Z]*\s*', '', text)
        text = re.sub(r'```\s*$', '', text)
    start, end = text.find('{'), text.rfind('}')
    if start < 0 or end < 0:
        raise ValueError("no json object in the answer")
    root = json.loads(text[start:end + 1])
    out = []
    for i, item in enumerate(root.get('candidates') or []):
        if not isinstance(item, dict):
            continue
        label = str(item.get('label', '')).strip()
        if not label:
            continue
        try:
            confidence = float(item.get('confidence', 0.5))
        except (TypeError, ValueError):
            confidence = 0.5
        out.append({
            'id': str(item.get('id') or ('m%d' % i)),
            'label': label,
            'confidence': max(0.0, min(1.0, confidence)),
            'evidence': str(item.get('evidence', '')),
        })
        if len(out) == 4:
            break
    return out


def common(parser):
    parser.add_argument('--root', default=os.environ.get('QWEN_ROOT', r'D:\qwen'),
                        help='where fetch_models.ps1 put the model (default D:\\qwen)')
    parser.add_argument('--audio', required=True)
    parser.add_argument('--clip-root', default=None,
                        help='where the model is, if not <root>/models')
    parser.add_argument('--start', type=float, default=0.0)
    parser.add_argument('--seconds', type=float, default=6.0)
    parser.add_argument('--temp', type=float, default=0.2)
    parser.add_argument('--predict', type=int, default=400)
    parser.add_argument('--ngl', type=int, default=99, help='layers on the GPU')
    parser.add_argument('--timeout', type=float, default=900.0)


def resolve(args, quant='Q4_K_M'):
    models = args.clip_root or os.path.join(args.root, 'models')
    args.model = os.path.join(models, 'Qwen2.5-Omni-7B-%s.gguf' % quant)
    args.mmproj = os.path.join(models, 'mmproj-Qwen2.5-Omni-7B-Q8_0.gguf')
    args.cli = os.path.join(args.root, 'bin', 'llama-mtmd-cli.exe')
    for path in (args.cli, args.model, args.mmproj):
        if not os.path.exists(path):
            raise SystemExit("missing %s - run fetch_models.ps1 first" % path)
    return args


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    subs = parser.add_subparsers(dest='mode', required=True)

    p_ask = subs.add_parser('ask', help='one question about one window')
    common(p_ask)
    p_ask.add_argument('--prompt', required=True)
    p_ask.add_argument('--system', default=None)

    p_explain = subs.add_parser('explain', help='the pause question, as the app asks it')
    common(p_explain)
    p_explain.add_argument('--facts', default=None,
                           help='a file with what the local analysis measured')
    p_explain.add_argument('--facts-text', default=None,
                           help='the same, said on the command line')
    p_explain.add_argument('--json', default=None, help='write the candidates here')

    p_sweep = subs.add_parser('sweep', help='the same question walked across a song')
    common(p_sweep)
    p_sweep.add_argument('--prompt', required=True)
    p_sweep.add_argument('--to', type=float, required=True)
    p_sweep.add_argument('--step', type=float, default=5.0)
    p_sweep.add_argument('--jsonl', required=True)

    args = resolve(parser.parse_args(argv))

    if args.mode == 'ask':
        clip, length = cut(args.audio, args.start, args.seconds)
        try:
            text, spent, _ = ask(args, args.prompt, clip, args.system)
        finally:
            os.remove(clip)
        print("%.2fs of audio, answered in %.1fs" % (length, spent))
        print(text)
        return 0

    if args.mode == 'explain':
        facts = ''
        if args.facts_text is not None:
            facts = args.facts_text
        elif args.facts:
            with open(args.facts, encoding='utf-8') as f:
                facts = f.read()
        clip, length = cut(args.audio, args.start, args.seconds)
        try:
            text, spent, _ = ask(args, facts or "（本地分析没有给出事实）", clip, SYSTEM)
        finally:
            os.remove(clip)
        print("%.2fs of audio, answered in %.1fs" % (length, spent))
        print(text)
        try:
            candidates = parse_candidates(text)
        except (ValueError, json.JSONDecodeError) as problem:
            print("NOT USABLE: %s" % problem)
            return 2
        if not candidates:
            print("NOT USABLE: no candidate had a label")
            return 2
        payload = {'candidates': candidates}
        if args.json:
            with open(args.json, 'w', encoding='utf-8') as f:
                json.dump(payload, f, ensure_ascii=False, indent=2)
        print("the app's parser would take %d of these:" % len(candidates))
        for c in candidates:
            print("  %-4s %-12s %.2f  %s" % (c['id'], c['label'], c['confidence'], c['evidence']))
        return 0

    if args.mode == 'sweep':
        written = 0
        with open(args.jsonl, 'w', encoding='utf-8') as out:
            start = args.start
            while start < args.to:
                clip, length = cut(args.audio, start, args.seconds)
                try:
                    text, spent, _ = ask(args, args.prompt, clip)
                finally:
                    os.remove(clip)
                row = {'start': round(start, 3), 'seconds': round(length, 3),
                       'answeredIn': round(spent, 2), 'answer': text.replace('\n', ' ').strip()}
                out.write(json.dumps(row, ensure_ascii=False) + '\n')
                out.flush()
                written += 1
                print("  %7.2fs  %5.1fs  %s" % (start, spent, row['answer'][:70]))
                start += args.step
        print("%d windows written to %s" % (written, args.jsonl))
        return 0

    return 1


if __name__ == '__main__':
    sys.exit(main())
