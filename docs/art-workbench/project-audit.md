# FallenAngel 美术工作台 · 项目审查

> 约定来源：`D:\rhythm-art-workbench-agent-contract.md`。本文件把「已确认 / 假设 / 未核实」分开写，避免把猜测写成事实。
> 回退点：`git` HEAD `33e1e58`（开始搭建工作台前的主线）。未改 `ProjectSettings`、未加 Package、未替换 `Assets/Art` 局内素材。

## 已确认

| 项 | 事实 |
| --- | --- |
| 引擎 | Unity **2022.3.62f3c1**，C# 9，内置渲染管线 |
| 分辨率 | 竖屏参考 **1080×1920**（`PlayVisualSpec` / `CanvasScaler`） |
| 玩法 | 下落式音游；主路径 4 键鼓谱 D/F/J/K；音符 tap / hold / drag / flick / slide + 宽键 |
| 判定档 | `JudgeResultType`：Perfect / Great / Good / Bad / Miss（窗口 ±80/160/180/200 ms，**工作台不得改这些数**） |
| 时间源 | 局内必须读 `GameManager.SongTime`；工作台沙盒用 `Time.unscaledDeltaTime`，避免绑死演奏时钟 |
| 已登记玩法事件 | `GameManager.OnStateChanged` / `OnGameStart` / `OnGameEnd`；`JudgeManager.OnJudgeResult(JudgeResultType, int lane)` / `OnJudgeBias` / `OnComboUpdate` / `OnScoreUpdate` / `OnJudgeCountsUpdate`；`InputManager.OnLaneInput` |
| 现有视觉 | 程序化几何为主（`NoteHeadGraphic`、`NoteShapeGeometry`、`HitRingGraphic`、`BurstGraphic`、`DeepSeaTheme`）；局内背景图在 `Assets/Art/bg/` |
| 现有美术管线 | `art_tools/`（方向稿 + 几何导出）；AGENTS.md：视觉改动先过 `art_tools/` 再进局内。**工作台视为调试工具**（与 `ModifierDebugPanel` 同类），不替换局内素材 |
| 场景约定 | 正式场景由 `Tools > FallenAngel > Build Default Game Scene` 重建；**不把工作台写进 Build Settings** |
| 本地 git | 开工时工作区仅有未跟踪 `.game-forge/`；用户未提交改动未覆盖 |

## 假设

| 项 | 说明 |
| --- | --- |
| 工作台三档视觉 | 合同要求「至少三档判定」。沙盒展示 Perfect / Great / Miss。实机 Good 视觉并入 Great，Bad 并入 Miss，**不改计分** |
| 长按事件 | 实机没有独立的 HoldStart/HoldEnd 事件。沙盒自模拟；实机绑定只转发 `OnJudgeResult` |
| 入口形态 | 独立场景 `Assets/Scenes/ArtWorkbench.unity`（编辑器菜单生成，不进构建列表）+ 局内 F9 / 编辑器调试钮，等价于独立入口 |
| 预设落盘 | `Application.persistentDataPath/art-workbench/` + PlayerPrefs 记最后槽位，重启后自动加载 |

## 未核实

| 项 | 说明 |
| --- | --- |
| 真机 120Hz 下工作台特效时长是否仍可读 | 需作者在目标机上看一眼 |
| ComfyUI 是否还要给工作台出贴图 | 本期几何即可，贴图换肤留给后续 |
| 选曲/结算稿是否要像素级对齐现有 `SongSelectPanelController` / `ResultScreen` | 本期是工作台独立稿，token 与深海色板同源，不改现有面板 |

## 明确不改

判定窗口、谱面格式、计分、音频校准、`ProjectSettings`、Package、`Assets/Art` 局内资源、`AGENTS.md`。
