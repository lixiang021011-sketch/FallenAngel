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

## 诊断：`deviation_report.py`

把"拍子跟没跟住"和"哪一拍是小节头"分成两层量。**缺一层就会产生幻觉**：两条只差一拍的
4/4 网格，16 分线完全重合，A 层的数一模一样。**不投票，不平滑。**

```powershell
python tools/docs/deviation_report.py --song flyway        # 一首歌的全套
python tools/docs/deviation_report.py --library            # 扫全库，只列相位有分歧的对
```

**A. Subdivision Fit**（16 分格贴不贴音乐事件）

- 整体偏移（圆平均，**不判好坏**：里面混着 ADTOF 固定延迟、演奏习惯、网格相位差）
- 校正掉这个固定偏移之后还贴不贴 → **held-out**：前半估偏移、后半评，反向再评一次，
  免得同一批事件既拟合又给自己打分
- 逐 4 小节窗的偏移 → 漂移（极差 ≤0.10 几乎没有 / ≤0.25 轻微 / 更大局部明显）
- `affine-fit residual`：含义严格限定为"一条固定 BPM 的直线描述不了这组 beat"，
  **不等于"模型坏了"**——也可能这首歌本来就有局部变速
- Mapperatorinator 那种 piecewise 来源**逐 section 评分再汇总**，绝不把红线摊成整曲 pseudo-grid

**B. Measure Phase**（这些 16 分格里哪一格该叫"小节 1 拍"）

各来源的小节头两两对相位：票数少的那条当 A，拿它自己的小节头去问"你在 B 里是第几拍"，
只留残差 ≤ 0.25 拍（差整数拍）或"附近根本没有"两种结果。

- 来源表先报**它自己数的每小节拍数**（如 `4 拍×63 2 拍×20`）。必须有：一段"错了两拍"
  可能只是它在那一小段把拍号数成了 2 拍，不写出来会把两件完全不同的事看成一件
- 小节长度差 >15% 直接说"相位不可比"，不硬凑
- 滑 vs 跳分开：连续滑（两条小节线长度略有出入，跑几分钟才攒出偏差）与
  **跳一格**（≈整数拍，这才是相位错位）分别报。相位放在"小节"这个圆上再展开，
  否则最近邻在两侧来回跳会被算成一次 4 拍的假跳变
- `aligned → 错位 → aligned` 报成 reconvergence（规格 §14）；没回来的那头照实说

## 第五条来源：Mapperatorinator（`import_mapperatorinator_timing.py`）

它在这个体系里**只是局部 timing / measure 观察者**：不替换任何现有网格，不当真值，不投票。

```powershell
# 先让 Mapperatorinator 把模型自己那串 observation 导出来（见下）
D:\fa_tools\mapperatorinator\venv\Scripts\python.exe inference.py ^
  audio_path="D:\fa_tools\mapperatorinator\in\<歌>_full.wav" ^
  output_path="D:\fa_tools\mapperatorinator\out\<歌>_timing_events" ^
  gamemode=3 output_type=[TIMING] dump_timing_events=true

# 接进 MGMCS：归档 + 派生
python tools/docs/import_mapperatorinator_timing.py --song <歌> --refresh
python tools/docs/import_mapperatorinator_timing.py --rebuild        # 只从归档重算派生层
python tools/docs/import_mapperatorinator_timing.py --rebuild --derived-root <临时目录>   # 比字节用
```

```
归档层（不可重算）  static/observations/source/<歌>.timing_events.json          模型原样输出
                    static/observations/source/<歌>.mapperatorinator.source.json 重建所需的全部输入
                   = 上面那份 + 红线的 bpm/meter + 音频时长
派生层（可重算）    static/observations/<歌>.mapperatorinator.json
```

**为什么走 `timing_events.json` 而不是直接读 .osu**：`.osu` 只能装红线，模型报的每一个
MEASURE 在导出时就被折叠掉了，读回来的人只能**假设**"从红线起每 meter 拍是一个小节"。
这个假设正好是观察者不该做的（不等长小节、局部缺失、重新对齐都会丢）。为此给本机的
Mapperatorinator 打了一个 `dump_timing_events=true`（补丁存在
`tools/docs/patches/mapperatorinator-dump-timing-events.patch`，只增加一个输出文件，
不改任何生成结果）。没有这份文件时才退回读红线，并在 `observationsFrom` 里写明那是折算的。

几条守住的边界：

- 模型的 TIMING_POINT token 只带时刻，`bpm` / `meter` 是导出成 .osu 时按拍数推出来的 →
  写清来源是**红线**，取不到就留空，不补猜测
- 小节头那里模型发的是 MEASURE 不是 BEAT，所以"拍的位置" = BEAT ∪ MEASURE，
  放在 `derived.pulsePositions` 里（派生读法，不改原始 observation）
- `health` 只评**它自己输出得像不像话**（局部缺失、输出越界、拍间距异常比例过高）；
  小事（比如两处红线只隔 300ms）走 `notes`，不当 verdict；
  **"和 Beat This 不一致"不算 suspect**——分歧只是证据
- `gaps` / `validRanges` 原样保留，绝不插值；`--rebuild` 与 `--refresh` 必须字节一致

## 四条读法，不投票（研究者 2026-10-10 定的方向）

同一首歌上我们要并排看的是**四组 observation**，不是一个"正确的网格"：

```
human_unquantized   人打的那一遍，未吸附（.raw 变体里就是它）
workbench           隔壁那条：鼓起音定 16 分格 + Beat This! 定相位与强拍（import_workbench_grid.py）
librosa             machine_beats.py 的拍格（现在文档里的 calibrationId 还是 'machine'）
madmom              downbeats_madmom.py 的小节线
```

每组各自带 **health**（它自己可不可信）、**有效范围**（valid，暂时多是 unknown）、
以及它参与的每条派生结论的 **analysisEligibility**。**不是四票多数表决。**

### 两条边界（都要守住）

- `beat_this_lock < 0.9 → suspect` 只属于 **workbench 这条校准**的 source-health 规则：
  它不是歌曲 health，也不等于"Beat This! 本身不可信"。0.82 / 0.88 这种若与人 / librosa /
  madmom 在局部一致，照样有研究价值。
- **`needsEars` 只影响人工审校提示**，不让整条校准变 suspect。它表达的是
  "本读法按当前规则提出了一个局部 bar-structure 假设，等人耳确认"，
  而不是"这段时间轴是错的"。`odd_bars → barLines` 是一条**读法**，不是 timing truth。

### Grid Pair Diff 的价值定位

**不是选赢家，而是找"哪些区段不同读法会让下游音乐结论发生变化"。**
如果四条网在时间上有偏差，但在 boundary-associated events、bar-level density、
entry/exit 这类结论上一致，那这一段就该**自动降噪**、不值得人盯。
做到这一点，MGMCS 才从"网格比较器"变成"研究风险定位器"。

## 脚本里的路径

它们都写死了本机路径（`F:\workbench曲库\_batch\manifest.json`、`_listen_root`、
`static/docs`）。这是刻意的：这些脚本是这台机器上的重建入口，不是要给人复用的库。
换机器要改的就是每份脚本顶部那几行常量。
