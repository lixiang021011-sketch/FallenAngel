# 校准（判定偏移）· 开源实现对照分析

作者口径（2026-09-22）："成熟的开发者应该先去 GitHub 找开源项目，先学习他们的写法，分析完了，
实在没得抄再自己写。"

本文件 = 这次"找开源项目"的结果。读了 4 个开源音游的校准实现（含 2 个 Unity/C# 同栈项目），
连同上一轮查到的上游（PJSK）遗留文案，合成出我们的方案。**只动 `art_tools/`，代码未改。**

## 0. 结论先行

| 环节 | 抄谁 | 出处 |
| --- | --- | --- |
| 界面语言（下落音符 + 判定线 + 命中音 + 粒子） | **Phira** | `phira/src/page/offset.rs` |
| 采样流程 / 离群剔除 / 进度 | **Cytoid**（Unity C#，同栈） | `GlobalCalibrator.cs` |
| 建议值口径 + 三段提示 + 一键应用 | **osu!** | `AudioOffsetAdjustControl.cs` + `SessionAverageHitErrorTracker.cs` |
| 手动微调滑块 | **Phira**（`-500..500ms`、步长 5） | 同上 |
| 文案 / 标题 / 确认弹窗 | **上游 PJSK**（唯一必须复现的形态） | `master_wording*.txt` 六条 |

## 1. 四个开源实现（逐项摘录）

### 1.1 osu!lazer — "打歌统计 + 一键应用"，不做专用小游戏

`osu.Game/Configuration/SessionAverageHitErrorTracker.cs`：数据来自**真实游玩**，
取一局 hit error 的**中位数**（抗离群），要求该局判定数 ≥ 50，最多保留 50 局：

```csharp
if (newScore.HitEvents.Count < 50) return;
if (newScore.HitEvents.CalculateMedianHitError() is not double medianError) return;
double globalOffset = configManager.Get<double>(OsuSetting.AudioOffset);
averageHitErrorHistory.Add(new DataPoint(medianError, globalOffset));
public double SuggestedGlobalAudioOffset => GlobalAudioOffset - AverageHitError;
```

`osu.Game/Overlays/Settings/Sections/Audio/AudioOffsetAdjustControl.cs`：一个 ms 滑块 +
**notch 直方图**（每个样本一根竖线，位置 `clamp(offset,min,max)/(2*max)`，旧的 alpha×0.95）+
「Apply suggested offset」按钮（仅当 |建议−当前| ≥ 1ms 可点）+ 三段提示文字：

```csharp
SuggestedOffset.Value = averageHitErrorHistory.Any()
    ? Math.Round(averageHitErrorHistory.Average(d => d.SuggestedGlobalAudioOffset)) : null;
// 无数据 → "SuggestedOffsetNote"；已准确 → "SuggestedOffsetCorrect(n)"；否则 "SuggestedOffsetValueReceived(n, value)"
applySuggestion.Action = () => { current.Value = SuggestedOffset.Value.Value; hitErrorTracker.ClearHistory(); };
```

要点：**不要求玩家做额外动作**；界面是"设置页里的滑块 + 收敛过程可视化 + 一键应用"。

### 1.2 Phira — 用游戏本体的下落语言做"跟着点 + 拖滑块"

`phira/src/page/offset.rs`（Rust / macroquad）：播一段专用校准音频 `cali.ogg`（2 秒循环）
与命中音 `cali_hit.ogg`，用**资源包里的音符贴图**（`respack.note_style.click`）与判定线：

```rust
let mut t = self.tm.now() as f32 - config.offset;   // 偏移直接改变音符位置
if t < 0. { t += 2.; } if t >= 2. { t -= 2.; }
let ny = ct.1 + (t - 1.) * 0.6;                     // ct = 判定线中心
if t <= 1. { draw note } else if self.cali_last { emit particle; play cali_hit; }
// 玩家按下：记录 (按下时刻, 当时音符 y)，之后 0.8s 内淡出一条白线标在按下位置
self.slider.render(ui, Rect::new(0.46, -0.1, 0.45, 0.2), ot, offset, format!("{offset:.0}ms"));
```

要点：**界面就是游戏自己**（同一个音符贴图、同一个打击音、同一个粒子），
玩家按屏幕只留一条"你打在哪"的痕迹；底部滑块 `-500..500ms` 步长 5，玩家自己拖到对齐。
**不自动算建议值**（自动分析是另一个工具 `prpr-auto-offset/`，给谱师用）。

### 1.3 Cytoid — 专用校准谱面 + 8 次采样 + 离群重来（Unity C#）

`engines/unity/Assets/Scripts/Utils/GlobalCalibrator.cs`：用打包的 `levels/offset_guide/`
（`music.ogg` + `Chart.txt` + `level.json`）当校准关卡，隐藏游戏 UI，分步提示 `OFFSET_SETUP_WIZARD_1..4`：

```csharp
var lastNote = game.Chart.Model.note_list.FindLast(it => it.start_time - 0.5f < game.Time);
var error = game.Time - lastNote.start_time;
offsets.Add(error);
if (offsets.Count > 1 && Math.Abs(offsets.Last() - offsets.GetRange(0, offsets.Count - 1).Average()) > 0.080)
{ retries++; needRetry = true; calibratedFourMeasures = false; offsets.Clear(); ... return; }   // 离群 → 本轮作废重来
progressIndicator.Text = $"{progress} / 8";      // 两轮 × 4 次
Context.Player.Settings.BaseNoteOffset = (float)Math.Round((decimal)offsets.Average(), 3, MidpointRounding.AwayFromZero);
```

要点：**两轮 4 次共 8 次采样**；单个样本与已收集均值偏差 > **80 ms** 判废、整轮重来；
进度 "n / 8"；重试 10 次或 120 秒后可跳过；最终取平均。

### 1.4 Quaver — 设置页一个按钮 → 进专用校准谱 → 结束后建议 + 确认

- `Quaver.Shared/Screens/Options/Items/Custom/OptionsItemCalibrateOffset.cs`：设置页「CALIBRATE」
  按钮 → 载入 `Quaver.Resources/Maps/Offset/offset.qua`，加 `NoFail`，进正常 `GameplayScreen`。
- `Screens/Gameplay/UI/Offset/OffsetCalibratorTip.cs`：顶部提示条 ——
  *"Play through the map. At the end, a new global audio offset will be suggested to you."*（3.5s 后淡出）。
- `Screens/Gameplay/UI/Offset/OffsetConfirmDialog.cs`：测完确认是否采用新偏移。

要点：和 Cytoid 同族（**用一张真谱面测**），但把"建议 + 确认"放在谱面结束之后。

## 2. 许可与红线（重要）

| 项目 | 许可 | 我们能做的 |
| --- | --- | --- |
| ppy/osu | **MIT** | 代码可以逐字抄，保留版权声明即可 |
| Quaver | MPL-2.0 | 可以引用；若改动其文件，该文件需继续 MPL |
| Phira | **GPL-3.0** | **不能搬代码**（会传染整个工程）；只能照它的**设计**自己写 |
| Cytoid | **GPL-3.0** | 同上：照设计自己写，不搬代码 |

结论：这次我们**只抄设计与参数**（流程、阈值、提示结构），代码全部自己写；
唯一可以逐字引用的文字级实现是 osu!（MIT），但我们连它也只取"口径"（中位数/均值 + 三段提示），
不搬 UI 框架代码（osu!framework 和我们技术栈不通）。

## 3. 合成到我们的界面（每条都有出处）

| 我们的元素 | 内容 | 抄自 |
| --- | --- | --- |
| 标题 / 入口说明 / 底部大按钮 | 「时机调整」/「可以调整判定时机…点击下方按钮」/「点击调整」 | 上游文案 `WORD_TIMING_ADJUSTMENT`、`MSG_ADJUST_WITH_TAP*` |
| 下落区 | 游戏本体音符（同一透视曲线）+ 判定线 + 命中粒子/闪光 | Phira |
| 测量中引导 | 「请配合声音点击，调整时机。」+ 进度 `n / 8` | 上游文案 + Cytoid |
| 离群处理 | 单次偏差 > 80 ms → 该轮作废，提示"重来一次" | Cytoid |
| 建议值 | 通过样本的平均（Cytoid 口径）→ 三段提示：未测量 / 已经准确（差 < 1ms） / 建议 X | osu! 的提示结构 |
| 应用 | 确认弹窗「要使用自动调整结果进行设置吗？」 | 上游文案 `MSG_ADJUST_WITH_TAP_RESULT_BODY` |
| 手动微调 | ± 按钮 + 方向说明「上方→＋／下方→－」 | 上游文案（PJSK 是 ±，不是滑块；Phira 是 `-500..500ms` 滑块，我们取它的"实时显示当前 ms"一点） |
| 数值写回 | `TimingAdjustData`（帧，±20，正 = 音符提前） | 上游 `LiveSettingData` / `LiveConfig` |

不抄的部分及原因：
- **不做 osu! 的"只靠打歌统计"**：上游 PJSK 明确有专用校准界面（文案 + `SE_TIMING_CHECK.acb` 音效包），
  作者也要求复刻它；打歌统计可以以后作为"二次校正"再考虑。
- **不做 Cytoid/Quaver 的"专用校准谱面"**：那需要为校准单独做一张谱面与音频资源；
  我们已经有轻量的 tick + 下落模拟，效果等价且不用新资源（也不碰版权素材）。
- **不用 PJSK 原版的 `SE_TIMING_CHECK.acb`**：那是零售游戏的受版权保护素材。

## 4. 符号口径（三家对照，确认我们没搞反）

| 项目 | 采样量 | 符号约定 | 换算 |
| --- | --- | --- | --- |
| osu! | `SuggestedGlobalAudioOffset = GlobalAudioOffset - median(hitError)` | hitError 正 = 打晚 → 建议值变小 | ms |
| Cytoid | `error = game.Time - lastNote.start_time`，直接 `BaseNoteOffset = avg(error)` | 正 = 打晚 → 偏移为正 | 秒 |
| 上游 PJSK | `timingAdjust = TimingAdjustData/60`，`Max(timingAdjust + musicTime, 0)` | **正 = 音符提前** | 帧 |
| 我们 | `建议 = −平均Δ`（Δ>0 = 打晚） | 与 PJSK 同向 | 帧 |

三家"打晚"对应的偏移符号不同，但**方向一致**：打晚 → 让音符晚出现。
我们沿用 PJSK 的符号（正 = 提前），因此对 Δ 取负；这条与上游文案
「判定线上方（打早）→ ＋」自洽。

## 5. 复现命令（在上游/开源仓库根执行）

```powershell
# 上游（本地克隆）
rg -l --text "WORD_TIMING_ADJUSTMENT|MSG_ADJUST_WITH_TAP" _refs\OpenSekai
# 开源项目（本次用 GitHub API 取文件树后按名筛 offset/calibrat/wizard）
#   ppy/osu            → osu.Game/Overlays/Settings/Sections/Audio/AudioOffsetAdjustControl.cs
#                        osu.Game/Configuration/SessionAverageHitErrorTracker.cs
#   TeamFlos/phira     → phira/src/page/offset.rs
#   Cytoid/Cytoid      → engines/unity/Assets/Scripts/Utils/GlobalCalibrator.cs
#   Quaver/Quaver      → Quaver.Shared/Screens/Options/Items/Custom/OptionsItemCalibrateOffset.cs
```

## 6. 本次产物

- `calibration_v2_compare.png` 等（`art_tools/live_port/preview_calibration_v2.py` 生成）
- 出图脚本内含"文案宽度不溢出"自检
