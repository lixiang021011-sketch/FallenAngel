# FallenAngel — Unity 音游 Demo

> AI 协作入口文档。游戏设计详见 `docs/项目说明.md`；开发规范详见 `docs/架构约定.md`。

## 速查

- **项目**：4K 下落式音游 demo，愿景为 roguelike 音游（buff/debuff 改变核心玩法）。Unity 2022.3.62f3c1（`D:\unity\2022.3.62f3c1`），C# 9。
- **工程不保存场景文件**：Unity 菜单 `Tools > FallenAngel > Build Default Game Scene` 重建场景；`Tools > FallenAngel > Create Note Prefab` 生成音符预制体。
- **运行**：菜单 START DEMO。PC：D/F/J/K 击打，SPACE 暂停，ESC 退出；手机：屏幕下半部四分区触屏。
- **编译自查**：关闭 Unity 编辑器后执行 `./compile_check.sh`。

## 铁律（AI 协作约束）

1. **只改 `Assets/Scripts` 下的 `.cs` 文件**；场景、预制体、`.meta` 由人在编辑器内操作。
2. 重命名/移动资产只在 Unity 内进行；不新增 Package；不改 ProjectSettings。
3. 新代码遵守 `docs/架构约定.md`：分层与依赖规则、单例模式、C# 事件通信（OnEnable/Start/OnDisable 订阅规范）、**时间一律取 `GameManager.SongTime`**、音符只经 NoteSpawner 对象池创建。
4. 小步快跑：一次一个可编译、可验证的改动，完成后说明"如何验证"。
5. 关键状态用 `Debug.Log("[类名] ...")` 输出，方便玩家侧排查。
6. 排查素材：`Logs/` 目录、玩家提供的 Console 报错原文与截图。
7. **开发收尾习惯**（每次任务完成后执行）：① 把当天开发按模板追加进 `Desktop\音游\开发记录.md`（完成内容/难点与解法/教训/待办）；② 已验证的改动及时 git 提交；③ 更新持久记忆中的开发日志；④ 划掉开发记录里已解决的待办。
