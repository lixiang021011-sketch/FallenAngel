# 仓库发布清单（FallenAngel）

> 面向「把这个仓库公开/更新到 GitHub」时的自查。数值为 2026-09-13 实测。

## 一、体积现状

| 目录 | 体积 | 是否入库 | 说明 |
|---|---|---|---|
| `Library/` | 634.6 MB | ❌ | Unity 导入缓存，`.gitignore` 已排除 |
| `Builds/` | 30.0 MB | ❌ | 成品走 **Releases**，不要提交 |
| `art_tools/` | 112.5 MB | ⚠️ 部分 | 仅入库脚本与工作流（3.3 MB）；`art_tools/ai_imagegen/out/`（出图）已排除 |
| `Assets/` | 33.0 MB | ✅ | 入库 26.7 MB（字体 SDF 10.2 MB + 思源黑体 8.0 MB 占大头） |
| `.git` | 49.4 MB | — | 历史记录 |

**入库实测：766 个文件 / 30.7 MB**，最大单文件 10.18 MB（`Assets/Fonts/CJK_Font SDF.asset`）
—— 远低于 GitHub 单文件 50 MB 告警 / 100 MB 硬上限，无需 Git LFS。

## 二、明确不入库的第三方内容（这是本仓库已经做对的地方）

| 内容 | 规则 | 原因 |
|---|---|---|
| 第三方曲目 / 音效 | `Assets/Resources/Audio/` 整目录排除，仅本地保留 | 避免公开分发他人作品；README 第九节已说明「克隆后演奏无声」 |
| 第三方开源参考谱面 | `_refs/` 排除 | 同上 |
| AI 出图结果 | `art_tools/ai_imagegen/out/` 排除，仅保留 workflow 与脚本 | 体积大 + 生成模型许可因模型而异 |
| chart_tools 缓存与生成物 | `__pycache__/`、`*_analysis.json`、`*_instruments.json`、`detailed_map.txt`、`sim-phi` 临时文件 | 可重建 |

> 若将来要给公开版配乐：**只入库自制曲目**；第三方曲目即使买了商用授权，也建议写进
> `THIRD_PARTY_NOTICES.md` 说明来源与授权范围，或者继续走「仅本地」策略。

## 三、许可（本次已补齐）

| 范围 | 文件 | 条款 |
|---|---|---|
| 源代码与脚本 | `LICENSE-CODE` | MIT |
| 谱面 / 数值 / 文档 / 美术 / 本地化 | `LICENSE-ASSETS` | 保留所有权利 + 非商业使用条款 |
| 第三方（思源黑体、TextMesh Pro） | `THIRD_PARTY_NOTICES.md`、`LICENSES/`、`Assets/Fonts/SourceHanSans-OFL.txt` | SIL OFL 1.1 / Unity Companion License |

## 四、发布步骤

```powershell
cd C:\Users\LorXer\Documents\trae_projects\FallenAngel
git status --short                 # 确认新增的周边文件（LICENSE* / .gitattributes / THIRD_PARTY_NOTICES.md / PUBLISHING.md / LICENSES/ / docs 对照文档）
git add LICENSE LICENSE-CODE LICENSE-ASSETS .gitattributes THIRD_PARTY_NOTICES.md PUBLISHING.md LICENSES docs
git commit -m "docs: 补齐许可两件套、第三方声明、发布清单与两项目周边对照"
git push
```

成品（PC 压缩包 / 安卓 APK）走网页 **Releases → Draft a new release**，不要塞回仓库。

## 五、发布前逐条自检

- [ ] `git status` 里没有 `Library/`、`Builds/`、`Logs/`、`_refs/`、`Assets/Resources/Audio/`、`art_tools/ai_imagegen/out/`
- [ ] 没有任何单文件 > 50 MB（现状最大 10.18 MB ✓）
- [ ] `LICENSE` / `LICENSE-CODE` / `LICENSE-ASSETS` 已提交
- [ ] `THIRD_PARTY_NOTICES.md` 已提交，字体 OFL 就近放在 `Assets/Fonts/`
- [ ] README 第九节仍与实际一致（不包含音频、`_refs/` 未入库）
- [ ] 发行包里附上 `LICENSE*`、`THIRD_PARTY_NOTICES.md` 与 `LICENSES/`

## 六、仓库页面建议（About / Topics）

**Description（示例，中英各一）**

```
竖屏下落式音游 Demo · 一曲多谱 + 局内 modifier 方向 · Unity 2022.3 LTS · 含通用谱面编辑器与数值模拟器
```

```
A portrait-mode falling-note rhythm game demo in Unity — multi-chart per song, modifier-driven runs, with a general chart editor and a balance simulator.
```

**Topics（可直接粘贴，最多 20 个，全部小写 + 连字符）**

```
unity rhythm-game music-game csharp game-demo mobile-game android portrait-mode chart-editor game-development interactive-media
```

（按需替换：若想强调愿景方向可加 `roguelike`；面向中文社区可加 `chinese`。）

**Social preview**：`Settings → General → Social preview` 传一张 1280×640 的游玩截图。

## 七、和 StylesVN 的关系

两个工程的周边对照、以及本次互相补齐的条目，见 `docs/仓库周边对照_FallenAngel_vs_StylesVN.md`。
