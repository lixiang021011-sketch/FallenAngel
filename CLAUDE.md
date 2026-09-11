# FallenAngel — Unity 音游 Demo

> AI 协作入口文档。游戏设计详见 `docs/project-overview.md`；开发规范详见 `docs/architecture.md`。

## 速查

- **项目**：竖屏下落式音游 Demo。正式入口是主菜单四按钮 + 行程地图（商店/天赋/三次演奏），愿景为局内 modifier 的 roguelike。Unity 2022.3.62f3c1（`D:\unity\2022.3.62f3c1`），C# 9。
- **场景**：菜单 `Tools > FallenAngel > Build Default Game Scene` 重建；`Create Note Prefab` 生成音符预制体。改 SceneBuilder 或删场景脚本后必须重建。
- **运行**：Play → 新游戏 / 选择存档。PC：D/F/J/K 击打，SPACE 暂停，ESC 退出；手机：屏幕下半部按轨分区触屏。
- **编译自查**：关闭 Unity 编辑器后执行 `./compile_check.sh`。成长表：`Tools > FallenAngel > Validate Portfolio Config and Talents`。

## 铁律（AI 协作约束）

1. **默认可改范围是 `Assets/Scripts` 下的 `.cs` 文件**；场景、预制体由人在编辑器内操作。文档仅在用户明确要求时改。删除脚本时同步删除对应 `.meta`，避免 Missing Script。
2. 重命名/移动资产只在 Unity 内进行；不新增 Package；不改 ProjectSettings。
3. 新代码遵守 `docs/architecture.md`：分层与依赖规则、单例模式、C# 事件通信（OnEnable/Start/OnDisable 订阅规范）、**时间一律取 `GameManager.SongTime`**、音符只经 NoteSpawner 对象池创建。行程地图走 `PortfolioSession`，不要复活已删除的 RunManager。
4. 小步快跑：一次一个可编译、可验证的改动，完成后说明"如何验证"。
5. 关键状态用 `Debug.Log("[类名] ...")` 输出，方便玩家侧排查。
6. 排查素材：`Logs/` 目录、玩家提供的 Console 报错原文与截图。
7. **开发收尾习惯**（执行时机：**功能经用户拍板完成、或跳转至下一个功能时**才记录/提交；未拍板的功能只留工作草稿，不写开发记录、不提交）：① 把当天开发按模板追加进 `Desktop\音游\开发记录.md`（完成内容/难点与解法/教训/待办）；② 已验证的改动及时 git 提交；③ 更新持久记忆中的开发日志；④ 划掉开发记录里已解决的待办。
