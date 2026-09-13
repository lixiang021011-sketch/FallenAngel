# 第三方组件与许可

本仓库包含或依赖以下第三方内容。发布（克隆、发行包）时请保留本文件与 `LICENSES/`。

## 1. 引擎与官方包

| 组件 | 位置 | 许可 | 注意 |
|---|---|---|---|
| Unity 2022.3 LTS 运行时 | 不在仓库内（`Library/`、`Builds/` 已被 `.gitignore` 排除） | Unity 官方条款 | 仓库只提交工程源；成品随包分发受 Unity 条款约束 |
| TextMesh Pro | `Assets/TextMesh Pro/`（含示例与文档） | **Unity Companion License**（官方原文见 `LICENSES/Unity-Companion-License-TextMeshPro.md`） | 允许随 Unity 工程使用与分发；不要把这些源码/示例单独当独立库再发布，也不要改版权声明。Unity 官方包只附链接、不附条款全文，本仓库沿用其分发方式 |
| TextMesh Pro **示例**里的第三方字体与表情图 | `Assets/TextMesh Pro/Examples & Extras/Fonts/`、`Assets/TextMesh Pro/Sprites/` | 各异，见下面 2.1 | ⚠️ 这些是 Unity 示例素材，**游戏本体并不使用**；详见 2.1 与「可选清理」 |

## 2. 字体

| 组件 | 位置 | 许可 | 注意 |
|---|---|---|---|
| 思源黑体（Source Han Sans CN） | `Assets/Fonts/SourceHanSansCN-Regular.otf`；许可见同目录 `SourceHanSans-OFL.txt` | SIL Open Font License 1.1 | **必须**随字体附 OFL 全文（本仓库已就近放置）；若要改字体名需遵守 OFL 保留名称条款 |
| 由思源黑体生成的 SDF 字体资产 | `Assets/Fonts/CJK_Font SDF.asset` | 同上（衍生物） | 属于同一字体的衍生资产，随 OFL 分发 |

### 2.1 TextMesh Pro 示例附带的第三方素材

| 素材 | 位置 | 许可 | 说明 |
|---|---|---|---|
| Anton | `Assets/TextMesh Pro/Examples & Extras/Fonts/Anton.ttf` | SIL OFL 1.1（就近：`Anton OFL.txt`） | 随包已附 OFL，保留即可 |
| Bangers | 同目录 `Bangers.ttf` | SIL OFL 1.1（就近：`Bangers - OFL.txt`） | 同上 |
| Oswald Bold | 同目录 `Oswald-Bold.ttf` | SIL OFL 1.1（就近：`Oswald-Bold - OFL.txt`） | 同上 |
| LiberationSans | `Assets/TextMesh Pro/Fonts/LiberationSans.ttf` | SIL OFL 1.1（就近：`LiberationSans - OFL.txt`） | 同上（TMP 默认字体后备） |
| **Roboto Bold** | 同目录 `Roboto-Bold.ttf` | **Apache License 2.0**（Google） | ⚠️ 官方**没有**随附许可文件，本仓库已补全文：`LICENSES/Apache-2.0-Roboto.md` |
| **Electronic Highway Sign** | 同目录 `Electronic Highway Sign.TTF` | **未知**（Unity 示例中未附许可文件，来源不明确） | ⚠️ 建议随发行版**不要**包含这个字体；最干净的做法是删除未使用的 TMP 示例目录（见下） |
| EmojiOne 表情图 | `Assets/TextMesh Pro/Sprites/` | 见 `EmojiOne Attribution.txt`（要求署名，条款以其官网为准） | ⚠️ 同上下建议：不用就删，保留就随发行包附署名文件 |

### 2.2 可选清理（消除授权疑问）

Unity 的 TMP 示例（`Assets/TextMesh Pro/Examples & Extras/`）与 EmojiOne 表情图本工程并未在游戏里使用。
如果确认没有任何脚本/预制体引用它们，可以整目录删除（连同 `.meta`），这样上面 6 项第三方授权问题就一次性消失，
仓库也会小一些。删除前请先在编辑器里确认无 Missing Reference，并在 `Assets/TextMesh Pro/Resources/TMP Settings.asset`
里检查默认 sprite asset 的指向。

## 3. 明确「不入库」的第三方内容

| 内容 | 处理方式 | 原因 |
|---|---|---|
| 第三方曲目 / 音效 | `Assets/Resources/Audio/` 已在 `.gitignore` 排除，**仅在本地保留** | 避免公开分发他人作品；克隆仓库后演奏无声（有虚拟时钟兜底），见 README 第九节 |
| 第三方开源参考谱面 | `_refs/` 已在 `.gitignore` 排除 | 同上，避免把他人的谱面数据带进公开仓库 |
| AI 生成的图片输出 | `art_tools/ai_imagegen/out/` 已在 `.gitignore` 排除（只保留 workflow JSON 与脚本） | 体积大；且生成模型的许可因模型而异 |

## 4. 发布前请确认

1. 仓库根目录有 `LICENSE`（本工程已拆成 `LICENSE-CODE` / `LICENSE-ASSETS`，`LICENSE` 为索引）。
2. 发行包（Releases）里附上 `LICENSE`、`LICENSE-CODE`、`LICENSE-ASSETS`、本文件与 `LICENSES/`。
3. 若将来引入**自制**音频，可入库；若引入第三方音频，请只保留在本地，或确保已获得分发授权并在此登记。
