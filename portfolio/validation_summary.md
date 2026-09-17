# 验证与数值证据（可外发的自证材料）

> **为什么有这份文件**：原始报告写在 `Logs/` 下，而 `Logs/` 在 `.gitignore` 里、不随仓库分发。
> 这份文件把每一项验证的**结论、日期与复现方式**固定进仓库，原始日志仍在本地。
> 最后更新：2026-09-13。

---

## 一、自检套件（全部可无头运行）

| 套件 | 项数 | 最近结果 | 覆盖范围 |
| --- | --- | --- | --- |
| `PortfolioChecks` | 95 | **PASS 95 / 0** | 配置表一致性、天赋前置与排他、存档事务与幂等、地图与掉落、折扣与利息叠加 |
| `IconGeometryChecks` | 27 | **PASS 27 / 0** | 图标墨迹包围盒中心偏移 ≤1px、不溢出宿主矩形 |
| `NotePartChecks` | 16 | **PASS 16 / 0** | 音符 head / body / tail 部件存在性与顺序、网格非空、不溢出、配色、同押连线几何 |
| `PlayVisualChecks` | 11 | **PASS 11 / 0** | 判定线视觉位置 == 音符判定位置、按键区不越线、演奏背景已挂载、Miss 特效已接线、modifier 调试窗口可安装 |

原始日志（本地、不入库）：`Logs/portfolio_report_20260910.txt`、`Logs/icon_geometry_check.txt`、
`Logs/note_part_check.txt`、`Logs/play_visual_check.txt`。

### 复现方式

编辑器内：`Tools > FallenAngel >` 对应菜单项。命令行（Unity 编辑器需先关闭）：

```powershell
$unity = 'D:\unity\2022.3.62f3c1\Editor\Unity.exe'
$proj  = 'C:\Users\LorXer\Documents\trae_projects\FallenAngel'
foreach ($m in 'PortfolioChecks','IconGeometryChecks','NotePartChecks','PlayVisualChecks') {
  & $unity -batchmode -nographics -quit -projectPath $proj `
    -executeMethod "FallenAngel.Core.$m.Run" -logFile "$proj\Logs\$m.log"
}
```

---

## 二、判定线位置修正（2026-09-13）

**问题**：判定线的「视觉位置」与「判定位置」分别硬编码在两处——

| 东西 | 位置 | 距屏幕底部 |
| --- | --- | --- |
| 视觉判定线 | `SceneBuilder` 的 `anchoredPosition.y = 400`（底部锚点） | 400px |
| 实际判定点 | `NoteSpawner.judgeLineY = -400`（画布中心系） | 560px |

两者相差 **160px（约屏高 8%）**：玩家看到的白线不是音符被判定到的那条线。
对下落式音游来说，这是最直接影响手感的一类错位。

**修法**：把垂直基准收成单一来源 `Assets/Scripts/Core/PlayVisualSpec.cs`，
`SceneBuilder`（视觉线 / 按键区）与 `NoteSpawner`（判定位置 / 生成位置）都只读它；
新增 `PlayVisualChecks` 断言「视觉线 == 判定线」，并且在真实装配出的场景里量，而不是只比常量。

**取值口径**：按**美术方向稿**取定——判定线**距底 400**，按键区就是判定线往下的那一段
（所以按键区顶边与判定线重合）。xlsx v0.1 里的「判定位置距底 560 / 按键区 500 高」是更早一版，已被美术稿取代。

**结果**（判定位置由 560 下移到 400，与视觉线对齐）：

```
PASS 视觉线 == 判定线        视觉线距底 400 / 判定距底 400，差 0px
PASS 按键区顶边不越过判定线     按键区顶边距底 400 ≤ 判定线距底 400
PASS 演奏背景已挂载           sprite=bg_gameplay 1080x1920
PASS Miss 特效已接线          HitEffectController=1 个，轨道柱引用 5/5
演奏界面视觉基准自检：10 项通过 / 0 项异常
```

**副作用（已知并接受）**：判定点下移 160px 后，音符下落距离由 1600px 变为 1760px（+10%）。
下落时间 `ActualFallTime` 未变，所以**滚动速度相应快 10%**。若日后觉得偏快，
调 `PlayVisualSpec` 里的生成高度即可，不必动判定位置。

**触控不受影响**：移动端有效区取的是屏幕高度比例（`InputManager.touchBottomRatio = 0.6`），
与按键区高度无关。

---

## 三、数值平衡模拟

数据日期 2026-09-09（v2 调整后），2026-09-13 入库并在仓库内复跑确认一致。

| 关卡 | 高手（全 Perfect 无 Miss） | 普通（85% Perfect） | 新手（60% Perfect） |
| --- | ---: | ---: | ---: |
| S01（基础 100） | **146.1** | **116.3** | **113.0** |
| S02 付费挑战（基础 150，路费 40） | 229.5（净 189.5） | 184.3（净 144.3） | 179.4（净 139.4） |

高手 S01 明细：基础 100 + I0 达标 6 + D0 增幅 10.6 + E05 无 Miss 26.5 + C1 利息 3 = **146.1**

**模型假设（诚实声明）**：15 局 × 5 种子 × 3 档水平；贪心策略（每局末解锁最便宜可解锁天赋、
商店买最便宜可负担装备）；未建模失败/中断/真实购买决策/玩家间差异。

**模拟暴露的结构问题**：成长节奏与演奏水平完全无关（积分只来自关卡固定值）；
I0 的 90% 阈值一票否决，导致 85% 与 60% 玩家收益相同；付费路线对普通玩家净增益仅 +6.4；
贪心行为下 80 元装备永远买不起。据此把 I0 改为上下双线达标、路费 60→40、E05 价格 80→60，
重跑确认收益梯度恢复（113 → 116.3 → 146.1）。

**仍未解决**：新手与普通只差 3.3，说明**技能向效果还没接进结算**（乐句级效果 A/B/C/K0 标注为"待乐句统计接入"），
这是下一版要处理的结构问题。

**入库文件**：

| 文件 | 说明 |
| --- | --- |
| `balance/balance_sim.py` | 模拟器（纯标准库，读 `balance/exported/` 下的配置 JSON） |
| `balance/exported/*.json` | 与游戏内 `PortfolioConfig.cs` 同源的导出配置 |
| `balance/balance_report.md` | 报告全文（含 v2 调整记录） |

**复现**：`python -X utf8 balance/balance_sim.py`

---

## 四、相关图表

- `portfolio/figures/fig_loop.png` —— 双层循环示意
- `portfolio/figures/fig_income.png` —— 收益构成示意
