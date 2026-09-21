# CC 授权测试曲包（cc_ 前缀）

作者口径（2026-09-22）："从开源平台上找几首可以直接转换用的歌曲和谱面，加到选歌界面，
我换几首曲子测试。歌曲名后标注来源游戏。"

## 为什么不是 osu! / Phira 的谱面

* **osu! 谱面**：曲目版权归各艺术家/厂牌，osu! 的 ToS 也只授权在 osu! 内使用 → 不能搬进本工程。
* **Phira（GPL-3.0）**：仓库里不含曲目与谱面（谱面走服务端），且其乐曲版权同样不属于可再分发范围。
* **Quaver（MPL-2.0）**：仓库只带一张校准谱 `Maps/Offset/offset.qua`（曲目 "Left Right" by Swan，
  MPL 只覆盖代码；曲目授权不明确）→ 不搬。
* **Cytoid**：示例关卡 `offset_guide` 是 **CC BY-NC-SA 4.0**（非商用 + 相同方式共享）→ 只在我们
  本地自测可以，不能作为发行素材，所以也没放进来。

结论：能**合法直接转换**的，是"**明确可再分发的曲目 + 我们自己的成谱**"。

## 曲目与署名（CC-BY 4.0 要求署名，曲名里已带）

曲目全部来自 Kevin MacLeod（incompetech.com），授权 **Creative Commons: By Attribution 4.0**
（<https://incompetech.com/music/royalty-free/faq.html>）。曲名后缀形如
`Voxel Revolution【Kevin MacLeod (incompetech.com) · CC-BY 4.0】`，直接显示在选歌界面里。

| 谱面文件 | 曲目 | BPM | 音符 | 备注 |
| --- | --- | --- | --- | --- |
| `cc_voxel_revolution.json` | Voxel Revolution | 122 | 600 | 密度 4.6/s（压力测试用） |
| `cc_electro_sketch.json` | Electro Sketch | 112 | 380 | 2.0/s |
| `cc_ouroboros.json` | Ouroboros | 107 | 380 | 2.4/s |
| `cc_bit_shift.json` | Bit Shift | 130 | 380 | 2.0/s |
| `cc_pinball_spring.json` | Pinball Spring | 116 | 380 | 1.6/s |

## 复现方式

```powershell
# 1) 下载（incompetech 的直链）
$dl = "$env:TEMP\fa_music"; New-Item -ItemType Directory -Force -Path $dl
foreach ($t in 'Voxel Revolution','Electro Sketch','Ouroboros','Bit Shift','Pinball Spring') {
  curl.exe -L "https://incompetech.com/music/royalty-free/mp3-royaltyfree/$($t -replace ' ','%20').mp3" `
    -o "$dl\$t.mp3"
}
# 2) 自动成谱（写出 Assets/Resources/Charts/cc_*.json + Assets/Resources/Audio/cc_*.mp3）
python -X utf8 chart_tools/cc_song_import.py --all $dl --level 6 --max-notes 380
# 3) 体检
python -X utf8 chart_tools/audit_charts.py
```

## 对齐怎么保证的

`cc_song_import.py` 的谱面时间**直接由音频起音推导**：`谱面时间 = 起音文件时间 − 前导静音`，
而前导静音就是 `metadata.offset`（本工程 `SongTime = 音频位置 − offset`）→ 不存在"差好几秒"。
只在吸附误差 ≤12ms 时才吸附到 1/4 拍网格（无条件吸附实测会带来 −20ms 的系统偏差）。

实测（`cc_*.json` 全部音符回查音频起音）：偏差中位 **0.0ms**、P90 **0.5ms**、最大 12ms。

> 注：`Assets/Resources/Audio/` 与 `Assets/Resources/Charts/cc_*.json`、`pjsk_*.json` 按 .gitignore
> 不入库（音频体积 / 版权素材），只在本机与打出的 APK 里存在。
