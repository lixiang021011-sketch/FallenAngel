# 下一步 · 文档里已写明的下一步

生成时间：2026-09-17T21:04:27.947Z

当时状态：
- 76 次提交，0 处未提交，最近：7e04c93 · docs: add a real screenshot of the chart editor, plus the one-click capture tool · 17 hours ago
- 文档：README_md、readme_md、AGENTS_md

## 1. 文档里已写明的下一步

往返校验零差异（730 音符测试用例）。谱面格式已支持 4 键鼓谱与 5 键吉他谱两套轨道布局（吉他模式为规划中）。
## 2. 改一处玩家能感觉到的循环，再跑无头检查

优先结算、谱面或手感。如果有 compile_check / batchmode，先跑再开新系统。
## 3. 选一个能感觉到的小增量

仓库看起来能跑。下一刀做手感、关卡、内容或反馈，而不是抽象重构。改完立刻自己过一遍。

## 给 Cursor 的提示词

```
继续推进「FallenAngel」。
路径：C:\Users\LorXer\Documents\trae_projects\FallenAngel
类型：Unity
Git：main · 工作区干净；最近提交：7e04c93 · docs: add a real screenshot of the chart editor, plus the one-click capture tool · 17 hours ago
按这个顺序做，做完就停，不要顺手大重构：
1. 文档里已写明的下一步 — 往返校验零差异（730 音符测试用例）。谱面格式已支持 4 键鼓谱与 5 键吉他谱两套轨道布局（吉他模式为规划中）。
2. 改一处玩家能感觉到的循环，再跑无头检查 — 优先结算、谱面或手感。如果有 compile_check / batchmode，先跑再开新系统。
3. 选一个能感觉到的小增量 — 仓库看起来能跑。下一刀做手感、关卡、内容或反馈，而不是抽象重构。改完立刻自己过一遍。
约束：只改这个项目；不要提交除非我明确要求；能用的改动优先于文档。
README 开头摘要：
# FallenAngel

竖屏下落式 **roguelike 音游**，独立开发，Unity 2022.3 / C#。

把「打歌」包进一局 roguelike 行程：每局沿路线图做取舍，局终把成果沉淀成永久成长。

> 单人项目：玩法设计、系统与数值、程序实现、内容与美术管线。开发中以 AI 承担实现层，设计决策与真机验证由作者负责。

| 玩法与循环 | 数值验证 | 美术管线 |
| --- | --- | --- |
| ![双层循环](portfolio/figures/fig_loop.png) | ![收益构成](portfolio/figures/fig_income.png) | ![局内背景](Assets/Art/bg/bg_gameplay.png) |
| **双层循环**：单局演奏 → 局内路线取舍 → 永久成长 | **收益构成**：技能水平与经济向成长各贡献多少 | **局内背景**：美术管线产出的局内资产 |

---

## 一、这个项目想解决什么

音游的即时反馈很强，但几乎没有长线目标——打完一首歌，除了一行分数，什么都没有留下。

Fall
```
