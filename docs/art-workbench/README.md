# 美术工作台 · 启动

独立预览音符、命中、长按、HUD、选曲稿、结算稿。不替换局内美术，不改判定窗口。

## 怎么打开

**方式 A · 独立场景（推荐调参）**

1. Unity 打开本工程（先停 Play）。
2. 菜单 `Tools > FallenAngel > Build Art Workbench Scene`。
3. 打开 `Assets/Scenes/ArtWorkbench.unity`，按 Play。
4. 不要用「Build Default Game Scene」覆盖这个场景。

**方式 B · 叠在正式场景上**

1. 打开正式场景 `Assets/Scenes/FallenAngel.unity`，按 Play。
2. 点左上调试条 **美术工作台**，或按 `F9`。
3. 再按一次 `F9` 或点「关闭」退出。正式轨道与判定原样保留。

## 怎么调

- 测试场里点 **Perfect / Great / Miss** 看三档命中；点 **长按开始 / 结束** 看长条。
- 拖底部滑条改音符尺寸、爆发半径、长按脉冲。改完立刻画在场上。
- **保存 A/B/C**：写入 `%userprofile%\AppData\LocalLow\<公司>\FallenAngel\art-workbench\`。
- 停 Play 再进，应自动读上一次槽位（A07）。
- **绑定实机事件**：正式场景演奏时打开工作台并勾上。有 `JudgeManager` 则转发命中/连击/分数；独立场景没有实机事件，状态栏会写明回退。

## 怎么验收

菜单 `Tools > FallenAngel > Validate Art Workbench` 打 A01–A11 清单。
Play 中再跑 `Tools > FallenAngel > Capture Art Workbench Evidence` 把截图写进 `docs/art-workbench/evidence/`。
