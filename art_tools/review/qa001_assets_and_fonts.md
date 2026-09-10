# QA001｜字体与资源整理清单

检查日期：2026-09-10　范围：全项目一次（UI/背景/动效资源与字体授权）

## 1. 字体授权

| 项目 | 现状 | 结论 |
| --- | --- | --- |
| 运行时字体 | `Assets/Fonts/SourceHanSansCN-Regular.otf`（思源黑体 CN Regular） | 可商用，随工程入库 |
| 授权文本 | `Assets/Fonts/SourceHanSans-OFL.txt`（SIL OFL 1.1） | 与字体同目录归档 |
| 已移除 | `simhei.ttf` / `msyh.ttc`（微软系统字体，不可随作品集/APK 外发） | 2026-09-09 已 `git rm` |
| TMP 图集 | `Assets/Fonts/CJK_Font SDF.asset`，由菜单 `Create Chinese TMP Font` 从思源黑体生成；源字体变更会自动重建 | 图集不可单独替换字体文件 |

**规则**：生成器只认工程内 OFL 字体，不再提供"从系统字体拷贝"的入口。

## 2. 关键符号覆盖（动态图集，按需生成）

界面实际用到的非汉字符号：`✕ ✓ ☐ ☑ · → ← ⚠ ×`。

- 思源黑体 CN Regular 覆盖以上符号（`✕`/`☑` 由 TMP 回退字形提供）。
- **待抽查**：Play 时逐个页面确认无方块；若有缺字，改用线稿图标（`ui_icon_close`/`ui_icon_check`）替代字符。

## 3. 资源命名与归档

| 类别 | 目录 | 命名规范 | 现状 |
| --- | --- | --- | --- |
| 运行时 UI 贴图 | `Assets/Art/ui/` | `ui_[系统]_[用途]_[状态]`，小写英文下划线 | 40 件 PNG（图标 24 / 按钮 12 / 卡片 4） |
| 矢量源文件 | `art_tools/source/` | 与 PNG 同名 | 40 件 SVG，工程外样张 |
| 审阅拼版 | `art_tools/review/` | `sheet_*.png`、`directions/*` | 图标/组件总览、四方向选型稿 |
| 背景 / 特效 | `Assets/Art/bg/`、`Assets/Art/fx/` | `bg_[页面]_[层]`、`fx_[事件]_[阶段]` | **空**（BG001–003、EQ001 待出图） |
| 设计源（工程外） | `page_redesign_0909/` | 需求表与页面稿 | 不入库 PSD 源稿 |

约束：禁止中文路径；图标导出 RGBA；面板九宫格留透明边；不把长背景图作为无压缩常驻纹理。

## 4. 待办

1. BG001/BG002/BG003、EQ001 出图后按上表归档，并更新 `art_delivery_review.md`。
2. 打包前抽查关键符号与中英文混排（含英文语言档）。
3. 确认 `Assets/Art/**` 的导入设置（Sprite / 压缩 / 非 2 的幂）在打包配置中生效。
