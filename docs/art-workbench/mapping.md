# 目录映射

合同目录 → 本仓库落点。工作台代码只活在 UI 层，不打进 Gameplay 判定。

| 合同项 | 本仓库 |
| --- | --- |
| 项目审查 | `docs/art-workbench/project-audit.md` |
| 设计 token / 默认配置 | `Assets/Scripts/UI/ArtWorkbench/ArtWorkbenchConfig.cs`（默认值）+ `docs/art-workbench/presets/default.json` |
| 测试场场景 | `Tools > FallenAngel > Build Art Workbench Scene` → `Assets/Scenes/ArtWorkbench.unity` |
| 局内等价入口 | 编辑器 Play：调试条「美术工作台」；任意模式：`F9` |
| 几何音符 / 轨道 / 判定线 | `ArtWorkbenchField.cs`（独立实例，不改 `Note.cs` / `Assets/Art`） |
| 命中 / 长按反馈 | 同上 + `HitRingGraphic` / `BurstGraphic` 的工作台私有实例 |
| HUD / 选曲 / 结算稿 | `ArtWorkbenchScreens.cs` |
| 调参面板与预设 | `ArtWorkbenchPanel.cs` + `ArtWorkbenchStore` |
| 实机事件适配 | `ArtWorkbenchHost` 订阅已登记事件，失败则回退工作台事件 |
| 验收证据 | `docs/art-workbench/evidence/` |
| 启动步骤 | `docs/art-workbench/README.md` 与根 `README.md`「美术工作台」节 |
