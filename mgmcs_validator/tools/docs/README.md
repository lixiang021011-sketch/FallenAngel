# static/docs 是怎么长出来的

这一层原来散在仓库外（`F:\vibe transcription\_mgmcs_index\` 与几个临时脚本里），
2026-10-10 搬进来，好让"删掉重建"这件事真的能执行、能被验收。

## 两层，别混

```
authored（人的东西，丢了补不回来）
  手机 listening 目录里的会话文档 + 真实按压 rawTaps + 人工校准
  → 目前放在 F:\vibe transcription\runs\phone_listening3\listening（仓库外，因为它是从手机拉的原始副本）
  → 但每一份文档里都留着 rawTaps，所以 static/docs 本身也进 git

generated（可重算）
  机器网格（librosa machine / madmom）、未吸附变体 *.raw、health 标注、歌单页
  → 这一层由下面的脚本从 authored 重算
```

**注意**：目录还没拆成 `authored/` 与 `generated/` 两个子目录。原因是验证器的页面是按
`docs/<文件名>` 取文档的（`fetch('docs/' + encodeURIComponent(file))`），文件名里带斜杠会被
编码成 `%2F`、服务端解不回来。要拆目录就得同时改这个取文档的约定，所以留着以后再动；
现在靠**这份说明 + 每份文档里的 `generatedBy` / `rawTaps` / `health` 字段**区分来源。

## 一条链（顺序不能换）

```powershell
# 0) 手机里的会话文档 → 验证器输入（一会话一份，写在临时目录）
D:\fa_tools\bp_venv\Scripts\python.exe tools\from_listening_ai.py <listening 根目录> --out <临时目录>

# 1) 每首歌挑一份当底（人工校准最多的那个会话）+ 并上机器网格，落到 static/docs/<短名>.mgmcs.json
python tools/docs/merge_phone_taps.py

# 2) 未吸附变体：把人工校准换回手机当时自己算的值 → <短名>.raw.mgmcs.json
python tools/docs/un_snap.py

# 3) health 标注：起点越界 / 速度与该曲共识不成 1,2,3,4 倍关系 → suspect + 理由
python tools/docs/mark_health.py

# 4) 歌单页（可点，带音频网址）
python tools/docs/build_song_index.py
```

`fold_similar.py` 是"同一套读法的多遍收束成一条"（研究者 2026-10-10 批准）；
当前语料命中 0 条——完全重合的那些早被 `machine_beats.py` 自己折过了，
剩下的人工线彼此真的不一样。规则留着，新歌会有。

## 脚本里的路径

它们都写死了本机路径（`F:\workbench曲库\_batch\manifest.json`、`_listen_root`、
`static/docs`）。这是刻意的：这些脚本是这台机器上的重建入口，不是要给人复用的库。
换机器要改的就是每份脚本顶部那几行常量。
