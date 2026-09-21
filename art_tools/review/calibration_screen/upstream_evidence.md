# 校准（判定偏移）· 上游 OpenSekai 溯源证据

作者提问（2026-09-22）："这部分你到底有没有参考 opensekai？"

结论：**系统层逐条照抄（有行号）；界面层上游没有玩家向实现，只有本地化表里留下的原版文案。**
本目录的 `calibration_upstream_*.png` 就是按那几条文案还原的对照稿（待过稿）。

上游路径：`C:\Users\LorXer\Documents\trae_projects\_refs\OpenSekai`

## 1. 系统层：抄了，可逐条核

| 环节 | 上游 | 我们 |
| --- | --- | --- |
| 存储 | `Assets/Scripts/Assembly-CSharp/Sekai/LiveSettingData.cs:38` `[Key("TimingAdjustData")] float`（默认 0，`:188` 清零） | `Assets/Scripts/Core/CalibrationSettings.cs`：`TimingAdjustData`（PlayerPrefs `FA_TimingAdjustData`） |
| 范围 | `Sekai/Live/LiveConfig.cs:26/28/30` `DefaultNoteTiming=0 / MinNoteTiming=-20 / MaxNoteTiming=20`（**单位 = 帧**） | 同值同单位，`Min/MaxTiming=±20` |
| 帧→秒 | `Sekai/Core/Live/BaseLiveController.cs:78` `timingAdjust = TimingAdjustData * 0.016667f` | `TimingAdjustSeconds = TimingAdjustData * (1f/60f)` |
| 应用点 | `Sekai/Core/Live/BaseLiveController.cs:48` `Mathf.Max(timingAdjust + currentMusicTimeMs/1000f, 0f)`（**加法，正数 = 音符提前**） | `Core/GameManager.cs`：`SongTime = clamp0(音频位置 + 判定偏移 − 谱面 offset − AudioLatency)` |
| 自检 | — | `Core/PlayClockChecks.cs`（±20 夹取、解析、帧↔ms） |

## 2. 界面层：上游**没有玩家向实现**

复现命令与结果（在上游仓库根执行）：

```
rg -l --text "WORD_TIMING_ADJUSTMENT" .   → 只有 Assets/Resources/wording/master_wording.txt 和 master_wording_zh.txt
rg -l --text "WORD_TIMING_SETTING"  .      → 同上两个文件
rg -l --text "MSG_ADJUST"           .      → 同上两个文件
rg -l --text -i "timing" Assets/Resources/screen   → 无结果（screen 资产里没有任何 timing 相关界面）
screen/prefabs/                            → 只有 Loading / Header / InsertNoti / NetworkIndicator / AssetRemoving / TouchEffect / MusicScoreMaker
```

即：这些文案 key 在上游**零引用**，是本地化表从原版（PJSK）继承下来的遗留字符串，OpenSekai 没实现对应界面。
唯一真正操作 `TimingAdjustData` 的界面是**自制谱编辑器的工程设置框**：

```
Assets/CustomMusicScoreManager/Runtime/UI/ScreenLayerCustomMusicScoreManager.cs
  CreateInputField(settingsContent, "判定偏移", "-20.0 - 20.0")
  liveSettingData.TimingAdjustData = ParseClampedSetting(text, LiveConfig.MinNoteTiming, LiveConfig.MaxNoteTiming, ...)
Assets/Scripts/Assembly-CSharp/Sekai/MusicScoreMaker/Ingame/Presenters/MusicScoreMakerPresenter.cs:2894
  _model.TimingAdjust = _model.LiveSettings.TimingAdjustData * (1f / 60f);
```

那是"玩家自己敲数字"的工程界面 —— 不是给玩家的校准工具。

## 3. 原版界面的规格（上游文案，逐字）

`Assets/Resources/wording/master_wording.txt`（日）/ `master_wording_zh.txt`（中）：

| key | 日文 | 中文 | 落点 |
| --- | --- | --- | --- |
| `WORD_TIMING_ADJUSTMENT` | タイミング調整 | 时机调整 | 标题 |
| `MSG_ADJUST_WITH_TAP_BODY` | 判定タイミングを調整できます。\n高い音が鳴るタイミングに合わせて\n下のボタンをタップしてください。 | 可以调整判定时机。\n请配合高音响起的时机\n点击下方按钮。 | 入口说明（敲"下方按钮"） |
| `MSG_ADJUST_WITH_TAP` | タップで調整 | 点击调整 | 底部大按钮 |
| `WORD_TIMING_SETTING_SOUND` | 音に合わせてタップし、タイミングをあわせてください。 | 请配合声音点击，调整时机。 | 测量中引导语 |
| `WORD_TIMING_SETTING_DESCRIPTION` | 判定ラインより上の場合は「＋」に、\n下の場合は「－」に調整してください。 | 若在判定线上方请调整为"＋"，\n下方则调整为"－"。 | ± 方向说明 |
| `MSG_ADJUST_WITH_TAP_RESULT_BODY` | 自動調整結果で設定しますか？ | 要使用自动调整结果进行设置吗？ | 自动调整结果确认弹窗 |

## 4. 顺带核对：符号口径同向

上游符号：正数 = 音符提前（`BaseLiveController.cs:48/78`）。
上游文案："音符在判定线**上方**（= 你打早了）→ 调「＋」"。
我们的测量：`Δ = 实际点击 − 音符到线时刻`（Δ>0 = 打晚），建议值 = `−Δ`
（`UI/CalibrationController.cs` + `Core/CalibrationMeasure.cs`）。
→ 打晚 → 建议取负 → 音符变晚，与"下方则「－」"一致；两者同向，无需改符号。

## 5. 本目录产物

- `calibration_upstream_compare.png` —— 三态并排（入口 / 测量中 / 自动调整结果）
- `calibration_upstream_entry.png` / `_measuring.png` / `_confirm.png` —— 单屏原尺寸
- `calibration_upstream_evidence.png` —— 第 3 节的溯源表
- 出图脚本：`art_tools/live_port/preview_calibration_upstream.py`（含"文案宽度不溢出"自检，16 项）
