# FallenAngel · AI 生图（imagegen）工作区

用途：用真正的 AI 生图（`gpt-image-2`）产出演奏层的**背景三层**，替换当前的全程序化几何背景。
音符、轨道、判定线、HUD 仍按 `gameplay美术需求与工作计划.xlsx` 保持「程序绘制优先」，不在此列。

## 目录
- `probe_directions.jsonl` — 方向探测：V1 深潜 / V2 月映 / V3 幽林 各 1 张，1024×1536、quality low（便宜、快）。
- `bg_layers_V1.jsonl` / `_V2` / `_V3` — 选定方向后的三层背景正式稿：远景 / 中景 / 近景暗角，2160×3840、quality high。
- `out/probe/` — 探测稿输出；`out/V1|V2|V3/` — 正式稿输出。

## 三方向对照（色值取自已定稿的 `art_tools/gameplay_directions_v2.py`）

| 方向 | 名字 | 底色 | 强调色 | 气质 |
|---|---|---|---|---|
| V1 | 深潜 | #081A2C → #030B14 | #7EE8D6 青绿荧光 | 最贴近现有基调 |
| V2 | 月映 | #1A2E40 → #091420 | #D6F0F6 月白 | 最空灵、留白最多 |
| V3 | 幽林 | #061E22 → #020A0E | #70F0BE 荧光藻绿 | 最「深蓝之森」 |

三者同属蓝—青—月白同色系（色相 152°~224°），符合「同色系 + 深海生物荧光」的要求。

## 生图硬约束（已写进每一条 prompt）

- 画布竖版 9:16；逻辑 1080×1920，源图 2160×3840。
- **中央竖向走廊必须干净、低对比**：那里要叠透视轨道（判定线处轨组约 1004px 宽，顶端收窄到 42%）。
- 两侧各留 38px、上下各留 120px 安全边。
- 远景 / 中景明度差 ≥ 8%；近景暗角**不得压暗中央走廊**。
- 不出现文字、UI、角色、logo、水印；避免霓虹洋红、橙色等高饱和多色。
- 整体压暗，保证亮色音符在上面对比度足够。

## 跑法（需要 OPENAI_API_KEY）

```powershell
$env:OPENAI_API_KEY = "<你的 key>"
$IMG  = "C:\Users\LorXer\.codex\skills\.system\imagegen\scripts\image_gen.py"
$ROOT = "C:\Users\LorXer\Documents\trae_projects\FallenAngel\art_tools\ai_imagegen"

# 1) 方向探测（3 张，便宜）
python -X utf8 $IMG generate-batch --input "$ROOT\probe_directions.jsonl" --out-dir "$ROOT\out\probe" --concurrency 3

# 2) 选定方向后跑正式三层（示例为 V1）
python -X utf8 $IMG generate-batch --input "$ROOT\bg_layers_V1.jsonl" --out-dir "$ROOT\out\V1" --concurrency 3
```

不花钱先验证参数（`--dry-run` 不联网、不需要 key）：

```powershell
python -X utf8 $IMG generate --prompt "test" --out "$ROOT\out\_dryrun.png" --dry-run
```

## 合成方式说明

`gpt-image-2` 不支持透明背景，所以三层按「无 alpha 的整幅图」产出，在 Unity 里这样用：

- FAR / MID：各做成一张全幅 Sprite，按视差系数分层移动（远景位移小、近景位移大）。
- NEAR：当成**暗角叠加层**，材质用 Multiply 混合，只保留两侧压暗与边缘遮挡，中央走廊保持接近白色才不会压暗音符。

如果后续确实需要真正带 alpha 的三层，再单独确认是否切到 `gpt-image-1.5` 的透明背景路径（该模型不支持 `--input-fidelity`，且需你明确同意）。

## 备注

- 若改用 Codex 内置 `image_gen` 工具（ChatGPT 登录，不消耗 API key），把 `.jsonl` 里对应那条的 `prompt` 整段复制过去即可，约束已写全。
- 定稿后把选中的三层复制进 `Assets/Art/`，保持 `GP_BG_FAR / GP_BG_MID / GP_BG_NEAR` 命名与挂载点不变。

---

# 本机开源生图链路（ComfyUI，已装好）

不需要任何 API key。ComfyUI 便携版：`D:\AI\ComfyUI_windows_portable`，
启动方式（双击或命令行）：`D:\AI\启动 ComfyUI (GPU).bat` → Web UI `http://127.0.0.1:8188`。

## 已安装模型

| 用途 | 文件 | 大小 | 位置 |
|---|---|---|---|
| SD1.5 底模（原装） | `v1-5-pruned-emaonly.safetensors` | 4.07GB | `models\checkpoints` |
| **SD1.5 微调（推荐默认）** | `DreamShaper_8_pruned.safetensors` | 2.03GB | `models\checkpoints` |
| SDXL 底模 | `sd_xl_base_1.0.safetensors` | 6.62GB | `models\checkpoints` |
| SDXL 加速 LoRA（4-8 步） | `sdxl_lightning_4step_lora.safetensors` | 376MB | `models\loras` |
| SDXL 稳定 VAE（fp16） | `sdxl_vae_fp16_fix.safetensors` | 319MB | `models\vae` |
| 超分模型（4×） | `RealESRGAN_x4.pth` | 64MB | `models\upscale_models` |

## 实测对比（同一提示词、同 seed 777、要求"中央压暗"）

| 模型 | 输出尺寸 | 耗时 | 全图亮度 | 中央−两侧 | 饱和度 | 评价 |
|---|---|---|---:|---:|---:|---|
| SD1.5 base | 1080×1920 | 32s | 118.4 | −11.5 | 0.37 | 偏亮、偏平 |
| **DreamShaper 8** | 1080×1920 | 30s | 47.4 | **−15.4** | 0.67 | 最符合约束，默认用它 |
| SDXL+Lightning | 2160×3840 | **18s** | 84.8 | +47.7 | 0.94 | 最艳丽，但中央偏亮 |

结论：**背景层用 DreamShaper 8**（SD1.5 两段式），**主菜单/宣传图用 SDXL**（画面更抓眼）。
SDXL 想同时满足"中央压暗"需要在提示词上多迭代几轮，或改用下面的后处理方案。

## 跑法

先启动 ComfyUI，然后（在项目根目录）：

```powershell
# DreamShaper 8 —— SD1.5 两段式，输出 1080×1920
python -X utf8 art_tools/ai_imagegen/comfy_run.py `
  --prefix FA_ds8_V1 --ckpt DreamShaper_8_pruned.safetensors `
  --prompt "<正向提示词>" --negative "<负向提示词>" `
  --width 540 --height 960 --out-width 1080 --out-height 1920 --seed 777 `
  --copy-to "art_tools/ai_imagegen/out/dreamshaper8"

# SDXL + Lightning + RealESRGAN 超分 —— 输出 2160×3840
python -X utf8 art_tools/ai_imagegen/comfy_run.py `
  --workflow art_tools/ai_imagegen/workflow_fa_bg_sdxl_lightning.json `
  --prefix FA_sdxl_V1 --prompt "<正向提示词>" --negative "<负向提示词>" `
  --width 768 --height 1344 --out-width 2160 --out-height 3840 `
  --seed 777 --steps 6 --cfg 1.5 `
  --copy-to "art_tools/ai_imagegen/out/sdxl"
```

## 重要经验：不要让模型去保证亮度分布

实测下来，"中央走廊必须比两侧暗"这件事靠提示词只能碰运气（SDXL 这轮就没做到）。
更稳的做法是**把构图和压暗解耦**：

1. AI 只负责画好看的海底场景（有漂亮的珊瑚、光柱、粒子）。
2. 中央压暗交给游戏内的一层竖向渐变（Multiply 混合），想压多深随时调，不用重新生成。

这样既拿到 AI 的质感，又拿回对可玩性最关键的对比度控制。

---

# ControlNet：让 AI 按图纸画

ControlNet 解决的是"**结构由我定、质感交给 AI**"。你给一张结构图，AI 严格照着重画，
不会自己乱改轨道数量、位置和判定线高度。

## 部署（已完成，无需自定义节点）

1. 模型放进 `D:\AI\ComfyUI_windows_portable\ComfyUI\models\controlnet\`
2. **不需要装任何自定义节点**——ComfyUI 核心自带全套 ControlNet 节点。
3. 重启（或刷新）ComfyUI 即可在 `ControlNetLoader` 里看到模型。

已安装：

| 模型 | 大小 | 适用 |
|---|---|---|
| `controlnet-union-sdxl-1.0.safetensors` | 2.4GB | **SDXL 万能型**，一个模型覆盖 openpose / depth / canny / lineart / scribble / segment / tile |
| `control_v11p_sd15_lineart.safetensors` | 1.38GB | SD1.5 线稿 |
| `control_v11p_sd15_scribble.safetensors` | 1.38GB | SD1.5 涂鸦/草图 |

## 节点接法（四件套）

```
LoadImage ─┐
           ├─> ControlNetApplyAdvanced ─┬─(positive)─> KSampler
CLIPTextEncode(正/负) ──────────────────┘  (negative)─>
                        ↑
ControlNetLoader ─> SetUnionControlNetType   ← 仅 SDXL union 需要
```

关键参数：

- `strength`：**1.0 = 严格照图纸**；0.5–0.7 = 只借结构，AI 自由发挥多一点。
- `start_percent` / `end_percent`：控制在哪几个采样步生效。想"先锁结构、后期放开细节"就设 `0.0 → 0.6`。
- SDXL union 必须先用 `SetUnionControlNetType` 指定类型，本项目的图纸是线稿，选 `canny/lineart/anime_lineart/mlsd`。

## 控制图从哪来

`make_layout.py` 按项目规格（1080×1920 / 判定线距底 400 / 轨组 1004 / 单轨 184 /
五轨中心 ±410、±205、0 / 顶端缩至 42%）**程序化生成**，比手绘或从照片提取更精确：

```powershell
python -X utf8 art_tools/ai_imagegen/make_layout.py
```

产出两张：

- `out/layout/layout_gameplay_1080x1920.png` —— 黑底白线，喂给 ControlNet
- `out/layout/layout_gameplay_annotated.png` —— 带中文尺寸标注，给人看的图纸

（ComfyUI 的 `LoadImage` 只读 `ComfyUI\input\`，所以控制图要先复制过去；工作流文件里已经按这个名字引用。）

## 跑法

```powershell
python -X utf8 art_tools/ai_imagegen/comfy_run.py `
  --workflow art_tools/ai_imagegen/workflow_fa_ui_controlnet_sdxl.json `
  --prefix FA_ui_cn --prompt "<美术描述>" --negative "<排除项>" `
  --width 832 --height 1472 --out-width 1080 --out-height 1920 `
  --steps 8 --cfg 2.0 --seed 555 --copy-to "art_tools/ai_imagegen/out/ui_controlnet"
```

## 有效性实测（结构相关度 = 生成图与图纸的边缘结构相关性）

| 出图方式 | 结构相关度 |
|---|---:|
| **ControlNet（强度 1.0）** | **+0.170** |
| 无 ControlNet（纯提示词） | −0.000 |
| 无 ControlNet（另一种提示词） | +0.007 |
| 纯背景生成 | +0.023 |

在 y=1000 处按规格推算的 10 条轨道边，ControlNet 版本**全部命中（10/10）**，
局部边缘强度 1.4–2.8，而背景中位数仅 0.2。也就是说：AI 确实照着我们的图纸画，
几何位置和项目规格一致。

## 什么时候不需要 ControlNet

背景层（远景/中景/近景暗角）没有硬性几何，用 DreamShaper/SDXL 直接出即可。
只有**界面、轨道、HUD 这类"位置必须准"的东西**才需要它。

### ⚠️ ControlNet 实测结论（含一次翻车记录）

测法：把生成图的强边位置，分别与"透视图纸推导位置"和"垂直图纸推导位置"比对，
看哪边强度高。同一套 prompt、同一批种子。

**第一轮结论（已作废）**：六组配置全部偏向垂直，当时以为"模型先验压过 ControlNet"。

**真相**：`make_layout.py` 当时有个坐标系写错位的 bug——按键区矩形本该是
`[左, 判定线y, 右, 底]`，却写成了 `[左, 右, 右, 底]`，导致五条轨的顶边依次落在
y=222 / 427 / 632 / 837 / 1042，形成**从左到右阶梯下降的假轨道**。
喂给 ControlNet 的控制图本身就是错的，自然画不对。

修复后重测（SDXL union + canny/lineart，强度 1.2）：

| 控制图 | 透视位置强度 | 垂直位置强度 | 判定 |
|---|---:|---:|---|
| 修正后 seed111 | 8.27 | 3.09 | ✅ 偏透视（2.7×） |
| 修正后 seed222 | 18.97 | 9.56 | ✅ 偏透视（2.0×） |
| 修正前（含阶梯 bug） | 4.62 | 8.16 | ❌ 偏垂直 |

**结论：ControlNet 是有效的，但控制图本身必须先自检。** 教训是"垃圾进、垃圾出"——
出图不对时，先怀疑控制图，再怀疑模型。（早期那版"10/10 命中"的验证标准也过松，
拿窗口最大值跟全图背景中位数比，任何纹理都能通过，已作废。）

**两条路都可用**：

- 要 AI 的质感 + 严格几何 → ControlNet（控制图必须先用 `检查水平/垂直白线段` 自检）
- 要 100% 精确、零随机 → 程序化合成（`compose_gameplay_mockup.py`），与游戏内
  `DeepSeaGraphic / NoteHeadGraphic` 本就是程序绘制保持一致

### 控制图自检脚本（每次改完图纸都跑一下）

```python
import numpy as np
from PIL import Image
a = np.asarray(Image.open("out/layout/layout_gameplay_1080x1920.png").convert("L")) > 128
for y in range(200, 1500):           # 判定线以上的区域不该有水平长线段
    idx = np.where(a[y])[0]
    if len(idx) > 80:
        print("可疑水平线 y=", y, "x", idx.min(), "..", idx.max())
```

## 合成流程（推荐主路径）

```powershell
# 1) AI 出背景（DreamShaper 8，无轨道、中央偏暗）
#    → art_tools/ai_imagegen/out/dreamshaper8/
# 2) 程序化合成透视轨道与 HUD
python -X utf8 art_tools/ai_imagegen/compose_gameplay_mockup.py `
  --bg art_tools/ai_imagegen/out/dreamshaper8/FA_ds8_V3_00001_.png `
  --out art_tools/ai_imagegen/out/mockup/FA_mockup_V3.png `
  --seed 1234 --top-scale 0.30
```

参数：`--top-scale` 控制透视强度（0.30 默认，0.18 更夸张），`--darken` 控制中央压暗程度，
`--no-notes` 出纯净界面版。

几何自检：`--top-scale 0.18` 那张实测轨组跨度 326→972（随 y 单调递增），
即近大远小由公式保证，不依赖模型心情。
