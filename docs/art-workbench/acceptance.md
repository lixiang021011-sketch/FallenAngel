# 验收对照 A01–A11

静态项用 `Tools > FallenAngel > Validate Art Workbench`。截图项 Play 后 `Capture Art Workbench Evidence`。

| ID | 要求 | 做法 | 状态 |
| --- | --- | --- | --- |
| A01 | 独立测试场，不替换局内美术 | 独立场景 + F9 覆盖层；不写 `Assets/Art` | 代码完成 |
| A02 | 设计 token 驱动几何音符 | `ArtWorkbenchTokens` + `ArtWorkbenchMark` 两轨 | 代码完成 |
| A03 | 命中反馈 | Perfect/Great/Miss → 环+放射条 | 代码完成 |
| A04 | 参数可调且立刻生效 | 底部滑条改宽度/爆发/脉冲 | 代码完成 |
| A05 | 保存预设 | `persistentDataPath/art-workbench/*.json` | 代码完成 |
| A06 | 重启后仍在 | PlayerPrefs 记槽位，Host 启动 `LoadLastOrDefault` | 代码完成 |
| A07 | 至少三档判定 | Perfect / Great / Miss 分色 | 代码完成 |
| A08 | 长按头尾与脉冲 | HoldStart/HoldEnd 按钮 | 代码完成 |
| A09 | HUD + 选曲稿 + 结算稿 | 顶栏三个 Tab | 代码完成 |
| A10 | 能接实机事件则接，否则回退 | 勾选「绑定实机事件」；无 JudgeManager 时状态栏说明回退 | 代码完成 |
| A11 | 截图证据 | `docs/art-workbench/evidence/`（token 同源 SVG + Play 截图位） | 布局证据已生成；Unity 截图需 Play |

未改：判定窗口、谱面格式、计分、音频校准、ProjectSettings、Package、AGENTS.md。
