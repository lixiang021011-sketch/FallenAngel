# 收尾验收清单（需你确认的事项）

分三类：**A. 需要你 Play 验收的改动**（未提交，通过后我提交）｜**B. 需要你拍板的事项**｜**C. 需要你提供的素材**。

---

## A. 需要你 Play 验收（本轮未提交的改动）

| # | 验收点 | 预期表现 | 怎么看 |
| --- | --- | --- | --- |
| A1 | 加载页（页面 19 准备演出） | 全屏深底 + 航标 + 往复滑动的不定进度条 + 说明文字，**不显示百分比** | 新游戏 → 点开始演奏，加载瞬间 |
| A2 | 危险确认弹窗 | 永久解锁天赋、放弃本局 = **暗红**确认按钮；购买确认 = 白底；提示弹窗 = 单按钮 | 天赋页点解锁 / 地图页放弃 / 商店买不起时点击 |
| A3 | 商店固定详情区 | 点候选只在下方详情显示效果与价格；买不起→按钮置灰并写差额；购买后留在商店并提示「已获得 Exx」 | 进入商店房间 |
| A4 | 装备页固定详情 | 详情在页面底部固定，不再有盖住格子的悬浮条签；空背包显示空图形 + 暂无装备 + 0/20 | 地图页 →「装备 n/20」 |
| A5 | 圆角语言 | 所有按钮、卡片、弹窗均为圆角；面板右上角 ✕ 是正十字，不再扁平 | 逐页扫一遍 |
| A6 | 图标（方向 A） | 24 枚图标 + 5 个房间图标 + 4 个效果符号，居中、无变形、可按状态着色 | 地图节点、商店、天赋页 |
| A7 | 天赋节点三类框 | 普通细框 / 交汇粗框 / 终点粗框+内层细线；锁定有锁标、已解锁有勾标 | 天赋页 |

已通过：`b431ee9` 提交的那批（商店/装备固定详情、方向 A 图标、圆角组件、错位修复）+ 编译自检 + 27 项几何自检。

## B. 需要你拍板

| # | 事项 | 选项 | 影响 |
| --- | --- | --- | --- |
| B1 | 占位音频 | 换可授权音源 / 继续用占位 | 占位音频不能随作品集外发 |
| B2 | 临时验收悬浮条 | 现在删 / 出包前删 | 它只在 `#if UNITY_EDITOR`，不影响正式包 |
| B3 | 出包目标 | Android（已有脚本）/ Windows / 两个都要 | 决定我补不补 Standalone 构建目标 |
| B4 | 作品集文档形式 | 一页式 PDF（推荐）/ 可点击网页 / PPT | 决定排版工具链 |
| B5 | 第 1 项剩余四项增量 | 现在做 / 出包后做 | UI009 旅程结算评级块、FX003 获得闪光、FX001 页面淡入、FX005 环境缓动 |

## C. 需要你提供

1. 截图 8–10 张：主菜单、选择存档、行程地图、商店（含详情区）、天赋树、装备背包、演奏中、结算明细、空态、加载页。
2. 录屏 30–60s：按分镜顺序（主菜单→地图→商店→演奏→结算→天赋→回地图）。

---

## 对照文档路径

| 用途 | 路径 |
| --- | --- |
| 开发记录（每轮完成内容/难点/教训/待办） | `C:\Users\LorXer\Desktop\音游\开发记录.md` |
| 收尾清单（本文件） | `C:\Users\LorXer\Documents\trae_projects\FallenAngel\portfolio\release_review_checklist.md` |
| 作品集工作稿（文案/结构/分镜/打包清单） | `C:\Users\LorXer\Documents\trae_projects\FallenAngel\portfolio\README.md` |
| 美术交付对照表（需求项→文件路径） | `C:\Users\LorXer\Documents\trae_projects\FallenAngel\art_tools\review\art_delivery_review.md` |
| QA001 字体与资源清单 | `C:\Users\LorXer\Documents\trae_projects\FallenAngel\art_tools\review\qa001_assets_and_fonts.md` |
| 图标与标识总览（拼版图） | `C:\Users\LorXer\Documents\trae_projects\FallenAngel\art_tools\review\sheet_icons_and_marks.png` |
| 按钮与卡片总览（拼版图） | `C:\Users\LorXer\Documents\trae_projects\FallenAngel\art_tools\review\sheet_components.png` |
| 图标风格四方向选型对比 | `C:\Users\LorXer\Documents\trae_projects\FallenAngel\art_tools\review\directions\directions_compare.png` |
| 图标几何自检报告（27 项） | `C:\Users\LorXer\Documents\trae_projects\FallenAngel\Logs\icon_geometry_check.txt` |
| 配置/天赋/存档事务自检报告（95 项） | `C:\Users\LorXer\Documents\trae_projects\FallenAngel\Logs\portfolio_report_20260910.txt` |
| 页面设计规格与交互提案 | `C:\Users\LorXer\Documents\Codex\2026-09-07\referenced-chatgpt-conversation-this-is-an\outputs\page_redesign_0909\design_handoff.md` |
| 美术需求与工期表 | 同目录 `art_resource_requirements.xlsx` |
| 项目设计说明 / 开发规范 | `C:\Users\LorXer\Documents\trae_projects\FallenAngel\docs\project-overview.md`、`docs\architecture.md` |
