# FallenAngel — Unity 音游 Demo

## 项目概览
下落式节奏音游 demo（FallenAngel 曲目）。玩家在轨道判定线处按键击打音符，系统判定并结算。

## 工程信息
- Unity 版本：2022.3.62f3c1（国内版），安装路径 `D:\unity\2022.3.62f3c1`，C# 9
- 依赖包：TextMesh Pro、UGUI、UnityWebRequest。**保持最小依赖，不要新增包**
- 版本控制：git（生成目录已忽略；**Assets 下的 `.meta` 文件必须提交**，Unity 靠 GUID 引用资产）

## 目录与命名约定
- 脚本按功能分层：`Assets/Scripts/{Core,Data,Gameplay,Input,UI,Audio}`，新脚本放入对应层
- 命名空间与目录对应：`FallenAngel.Core`、`FallenAngel.Data`、`FallenAngel.Gameplay`、`FallenAngel.InputSystem`、`FallenAngel.UI`、`FallenAngel.Audio`
- 代码风格：PascalCase 方法/属性，camelCase 字段；序列化字段用 `[SerializeField]` + `[Header]`/`[Tooltip]`；中文 XML 注释（`///`）；单例模式 `public static X Instance { get; private set; }`
- 数据资产走 `Resources/` 加载：谱面 `Resources/Charts/*.json.bytes`，音频 `Resources/Audio/`

## 核心架构
- `GameManager`（Core）：全局单例，游戏状态机（Menu/Loading/Playing/Paused/Result）、计时与速度倍率；事件 `OnStateChanged` / `OnGameStart` / `OnGameEnd`
- `AudioManager`（Audio）：音乐播放
- `ChartLoader` / `ChartData` / `NoteData`（Data）：谱面加载与数据结构
- `NoteSpawner` / `JudgeManager` / `Note`（Gameplay）：音符生成、判定、下落
- `InputManager`（Input）：按键输入
- `SceneBuilder`（Core，Editor Only）：**工程没有保存任何场景文件**。通过 Unity 菜单 `Tools > FallenAngel > Build Default Game Scene` 一键生成场景结构（EventSystem、管理器单例、UI 等）。场景丢失或重建后用此菜单恢复

## 运行与验证
1. 用 Unity 打开工程后，若没有场景：菜单 `Tools > FallenAngel > Build Default Game Scene`
2. 运行游戏：开始菜单 → 选择歌曲 → 音符下落 → 按键判定 → 结算
3. 编译自查：关闭 Unity 编辑器后，在工程根目录执行 `./compile_check.sh`（batchmode 编译并输出错误）

## AI 协作约束（重要）
- **只改 `Assets/Scripts` 下的 `.cs` 文件**；场景、预制体、`.meta` 文件由人在编辑器内操作（手改 YAML/meta 会断引用）
- 重命名/移动资产只在 Unity 编辑器内进行
- 不要新增 Package、不要改 ProjectSettings
- 小步快跑：一次一个可编译、可验证的改动；完成后说明"如何验证"
- 修改前先看现有同类代码，沿用已有模式（单例、事件、Resources 加载）
- 关键状态用 `Debug.Log` 输出，方便排查
- 排查素材：`Logs/` 目录、玩家提供的 Console 报错原文与截图
