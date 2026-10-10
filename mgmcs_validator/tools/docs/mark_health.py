import json, os, shutil, statistics, subprocess
from pathlib import Path
docs = Path(r"C:\Users\LorXer\Documents\trae_projects\FallenAngel\mgmcs_validator\static\docs")
BACKUP = Path(r"F:\vibe transcription\_mgmcs_index\backup_docs_before_health")
AUDIO_ROOT = Path(r"F:\vibe transcription\_listen_root")
FFPROBE = r"C:\Users\LorXer\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffprobe.exe"
FAMILY = [1, 2, 3, 4]     # 拍速读法的倍数家族：1/2 拍、2 倍、3 倍、4 倍都算“同一条读法”
TOL = 0.03

def real_duration(name):
    if not name:
        return None
    path = AUDIO_ROOT / name
    if not path.exists():
        return None
    out = subprocess.run([FFPROBE, "-v", "error", "-show_entries", "format=duration",
                          "-of", "default=nw=1:nk=1", str(path)], capture_output=True, text=True).stdout.strip()
    try:
        return float(out)
    except ValueError:
        return None

def in_family(ratio):
    for base in FAMILY:
        for candidate in (base, 1.0 / base):
            if abs(ratio - candidate) <= TOL * candidate:
                return True
    return False

if BACKUP.exists():                                   # 把上一版写坏的 health 撤回
    for p in BACKUP.glob("*.mgmcs.json"):
        shutil.copy2(p, docs / p.name)
    print("已从备份还原 %d 份文档" % len(list(BACKUP.glob('*.mgmcs.json'))))

suspects = []
for p in sorted(docs.glob("*.mgmcs.json")):
    d = json.loads(p.read_text(encoding="utf-8"))
    cals = d.get("calibrations") or []
    humans = [c for c in cals if c.get("source") == "human"]
    duration = real_duration(d.get("song", {}).get("audioFileName"))
    bpms = [c["bpm"] for c in humans if isinstance(c.get("bpm"), (int, float)) and c["bpm"] > 0]
    consensus = statistics.median(bpms) if len(bpms) >= 3 else None
    for c in cals:
        reasons = []
        if c.get("source") == "human":
            origin, bpm = c.get("audioTime"), c.get("bpm")
            if duration and isinstance(origin, (int, float)) and not (0 <= origin <= duration):
                reasons.append("outOfRange: 起点 %.1fs 不在音频 [0, %.1f] 内" % (origin, duration))
            if consensus and isinstance(bpm, (int, float)) and bpm > 0:
                ratio = bpm / consensus
                if abs(ratio - 1) > TOL and not in_family(ratio):
                    reasons.append("tempoUnrelated: %.2f 对共识 %.2f 是 %.2f 倍，不属于 1/2/3/4 家族" % (bpm, consensus, ratio))
        c["health"] = "suspect" if reasons else "ok"
        c["suspectReasons"] = reasons
        if reasons:
            suspects.append((p.name[: -len(".mgmcs.json")], c.get("calibrationId"), reasons[0]))
    p.write_text(json.dumps(d, ensure_ascii=False, indent=2), encoding="utf-8")

print("标为 suspect：%d 条" % len(suspects))
for name, cid, why in suspects:
    print("  %-14s %-8s %s" % (name, cid, why))
