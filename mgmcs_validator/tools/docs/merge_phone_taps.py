"""把手机里的人工打点并进验证器文档：每首歌一份，人工 + librosa + madmom 三条线。

背景（2026-10-10）：手机里有 33 首、74 个会话、2753 次打点，但本地只拉过两首。
现在整个 listening 目录已经拉下来并用验证器自带的 `from_listening_ai.py` 转成了
「一会话一份」的文档（160 份，写在 `runs/phone_docs/`）。这个脚本做剩下三步：

1. 按歌挑一个会话当底——**挑人工校准条数最多的那个**（同一首歌打过多遍，最富的那遍信息最多）；
2. 用 `machine_beats.py` + `downbeats_madmom.py` 把机器网格并进去（音频取自曲库清单）；
3. 落到 `mgmcs_validator/static/docs/<短名>.mgmcs.json`，短名与 catalog / runs/ 一致。

只读手机文档；覆盖 static/docs 里同名文档前会把旧的备份到 `_mgmcs_index\backup_docs\`。
"""

from __future__ import annotations

import json
import shutil
import subprocess
from pathlib import Path

VALIDATOR = Path(r"C:\Users\LorXer\Documents\trae_projects\FallenAngel\mgmcs_validator")
PHONE_DOCS = Path(r"F:\vibe transcription\runs\phone_docs")
DOCS = VALIDATOR / "static" / "docs"
BACKUP = Path(__file__).with_name("backup_docs")
MANIFEST = Path(r"F:\workbench曲库\_batch\manifest.json")
PY_BEATS = Path(r"D:\fa_tools\bp_venv\Scripts\python.exe")
PY_MADMOM = Path(r"D:\fa_tools\db_venv\Scripts\python.exe")

# 短名 ← 手机会话目录名里的特征串。特征串要唯一区分得开同一首歌的不同版本
# （例如 赤橙 有两个版本，曲库用的是 Remastered，手机里的 -Acoustic 不算）。
MATCH = {
    "chichunhua": ("ACIDMAN-アシッドマン-赤橙", "-Acoustic"),
    "dubai": ("H-G-ハグ-独白", ""),
    "eine": ("米津玄師-よねづ-けんし-アイネクライネ", ""),
    "flyway": ("halyosy-森晴义-Flyway", ""),
    "flywayleo": ("Leo-need-Flyway", ""),
    "haiiro": ("東雲絵名-限りなく灰色へ", ""),
    "happyend": ("back-number-バックナンバー-ハッピーエンド", ""),
    "henceforth": ("Leo-need-Henceforth", ""),
    "hitchcock": ("ヨルシカ-Yorushika-ヒッチコック", ""),
    "iloveyou": ("back-number-バックナンバー-アイラブユー", ""),
    "kaitou": ("back-number-バックナンバー-怪盗", ""),
    "kaluo": ("ChiliChill乐团-啊-美丽卡洛", ""),
    "kiechau": ("Cö-shu-Nie-コシュニエ-消えちゃう前に", ""),
    "knowme": ("八木海莉-know-me", ""),
    "landaohua": ("酔シグレ-Lucia-藍悼花", ""),
    "lulu": ("Mrs-GREEN-APPLE-lulu", ""),
    "natsuyuki": ("Cö-shu-Nie-コシュニエ-夏の深雪", ""),
    "onewish": ("鸣潮先约电台-Cö-shu-Nie-コシュニエ-One-More-Wish", ""),
    "sacra": ("ReoNa-レオナ-SACRA", ""),
    "sakuranoko": ("カンザキイオリ-神崎伊织-桜の子", ""),
    "seiza": ("結束バンド-纽带乐队-星座になれたら", ""),
    "soragoto": ("明透-ソラゴト", ""),
    "spica": ("ロクデナシ-スピカ", ""),
    "taishou": ("YOASOBI-ヨアソビ-大正浪漫", ""),
    "tao": ("ChiliChill乐团-逃", ""),
    "tengoku": ("Mrs-GREEN-APPLE-天国", ""),
    "velvet": ("back-number-バックナンバー-ベルベットの詩", ""),
    "yorukake": ("Spitz-スピッツ-夜を駆ける", ""),
    "yue": ("说说Crystal-月", ""),
    "yukiguni": ("ヨルシカ-Yorushika-雪国", ""),
}


def humans(path: Path) -> list[dict]:
    try:
        doc = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return []
    return [c for c in (doc.get("calibrations") or []) if c.get("source") == "human"]


def main() -> int:
    songs = {song["name"]: song for song in
             json.loads(MANIFEST.read_text(encoding="utf-8"))["songs"]}
    BACKUP.mkdir(exist_ok=True)
    rows, skipped = [], []

    for short, (token, exclude) in MATCH.items():
        if short not in songs:
            skipped.append("%s（曲库清单里没有）" % short)
            continue
        candidates = [p for p in PHONE_DOCS.glob("*.mgmcs.json")
                      if token in p.name and (not exclude or exclude not in p.name)]
        richest = None
        for path in candidates:
            count = len(humans(path))
            if richest is None or count > richest[1]:
                richest = (path, count)
        if richest is None or richest[1] == 0:
            skipped.append("%s（没有带人工校准的会话）" % short)
            continue

        document = DOCS / ("%s.mgmcs.json" % short)
        if document.exists():                     # 旧文档先备份，别悄悄烧掉
            shutil.copy2(document, BACKUP / document.name)
        shutil.copy2(richest[0], document)        # 以手机会话为底

        audio = songs[short]["audio"]
        for python, tool in ((PY_BEATS, "machine_beats.py"),
                             (PY_MADMOM, "downbeats_madmom.py")):
            subprocess.run([str(python), str(VALIDATOR / "tools" / tool), audio,
                            "--into", str(document), "--out", str(document)],
                           cwd=str(VALIDATOR), capture_output=True, text=True)
        rows.append((short, richest[1], richest[0].name, len(humans(document))))

    print("并入 %d 首：" % len(rows))
    for short, count, source, kept in rows:
        print("  %-12s 挑中 %d 条人工校准（会话 %s）→ 合并后 %d 条" % (short, count, source, kept))
    if skipped:
        print("跳过：%s" % "、".join(skipped))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
