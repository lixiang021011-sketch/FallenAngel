# MGMCS Desktop Validator v0.1

把手机算出来的校准和事件放回真实音乐上，用人眼和人耳检查多网格坐标这套方法成立不成立。

它只做四件事：导入本地音乐、导入手机给的 JSON、按 JSON 里的校准画所有平行网格、让研究者
判断哪条网格在哪一段可信，并把判断单独存成一个文件。

**它不做**：自动 BPM、自动拍号、自动变速/半小节识别、置信度、LLM、Basic Pitch、分轨、
云端、账号、数据库。它也不修改导入的 JSON 一个字节。

---

## 怎么跑

双击 **`run.bat`**。就这一步。

- 有 Python 时它起一个本地服务（`http://localhost:8123/`），关掉那个黑窗口就停。
- 没有 Python 时它直接用浏览器打开 `static/index.html`。

**这条 `run.bat` + 本地服务的路是实测过的**（Chrome，1440×900 窗口）。直接双击
`static/index.html` 用 `file://` 打开也应该能用：页面只引用了同目录的四个文件，没有 ES module
或 Worker，所以不需要服务器。唯一用到 `fetch` 的地方是 `?doc=`（见下），不带这个参数就不会
发请求。唯一可能被浏览器限制的是本机缓存
（`localStorage`）——真被限制时界面会明说「本机缓存写不进去」，判断本身照常，保存按钮也
照常。这一步我没有实测。

音频只在本机读取（文件选择器 + `blob:` 地址），**不上传任何地方**。

## 先拿演示数据试一遍

```
demo/demo_120.wav            32 秒、120 BPM、4/4 的节拍器
demo/demo_120.mgmcs.json     四条校准 + 六个事件
```

导入这两个文件，然后对着规格里的验收条件看：

| 试 | 怎么看 | 应该看到 |
|---|---|---|
| A 普通 4/4 | Grid A（`cal_A`，起点 2.0s） | 每 0.5s 一个拍点，每 2s 一条粗的小节线，正好压在重拍上 |
| B 半小节 | Grid A ↔ Grid B（`cal_B` 起点晚 1 秒） | 右侧「相位差」写着 Δtime 1.000 s、Δbeat 2.000、Δbar 0.500；两条网格的小节线差半个小节 |
| C 4/4 与 3/4 | Grid A 与 Grid C | 拍点重合，小节线每 3 拍就分叉一次，12 拍后重新对上 |
| D 同拍号重新校准 | Grid A 与 Grid D（一模一样） | 画成两条独立网格，谁也没覆盖谁 |
| E 跳转 | 点任意一小节 | 播放头跳到那小节第一拍，所有网格坐标同时刷新，播放状态不变 |

演示 JSON 里的 `half_bar` 和 `guitar_attention_in/out` 是**演示事件**，用来试事件层、
筛选和区间配对，不代表这段音频里真有这些事。

## 用自己的数据

1. `导入音频` 选歌（flac / wav / mp3）。
2. `导入 JSON` 选手机导出的 `<歌名>.mgmcs.json`。

也可以**把音频和 JSON 一起拖进窗口**（或拖一整个文件夹）：页面按名字自动配对，并在下面
写明配的是哪两个文件。拖进来的 `.validation.json` 会被认出来是结果、不当输入。

已经做完的歌放在 `static/docs/`，可以直接用网址打开，不用每次都找文件：

```
http://localhost:8123/index.html?doc=kiechau     →  static/docs/kiechau.mgmcs.json
```

音频默认要自己拖进来（歌不在这个文件夹里）。嫌麻烦就再开一个只读的小服务，让页面按网址
自己加载：

```
双击 serve_music.bat            →  把音乐文件夹挂在 http://127.0.0.1:8125/
http://localhost:8124/index.html?v=8&doc=kiechau&audio=<歌的网址>
```

`serve_music.bat` 里第一行路径改成你自己的音乐文件夹。它只监听本机、只读、不上传任何东西。
`&audio=` 只在 http 下有用，`file://` 下没有 `docs/` 可读，页面会明说打不开。

如果 JSON 里有 `song.durationSeconds` 而它和音频长度差超过 0.5 秒，页面顶部会红字提醒
——多半是拿错版本了，这时候网格整体错位不是方法的问题。

## 一轮完整流程（手机 → 验证器）

把手机里那首歌的目录拉下来（里面有 `events.jsonl`），然后三步。两个机器引擎各占一个
Python 环境：librosa 在 `bp_venv`，madmom 在 `db_venv`（下一节说明它怎么装上的）。

```bat
rem 1. 手机的原始记录 → 验证器的输入（从原始按压重算，不改动按压本身）
D:\fa_tools\bp_venv\Scripts\python.exe tools\from_listening_ai.py "拉下来的目录" --out static\docs

rem 2. 机器拍格：librosa。它只知道"拍"，不知道小节有几拍
D:\fa_tools\bp_venv\Scripts\python.exe tools\machine_beats.py "歌.ogg" ^
    --into static\docs\歌.mgmcs.json --out static\docs\歌.mgmcs.json

rem 3. 机器小节线：madmom 的强拍模型。它说哪一拍是第 1 拍
D:\fa_tools\db_venv\Scripts\python.exe tools\downbeats_madmom.py "歌.ogg" ^
    --into static\docs\歌.mgmcs.json --out static\docs\歌.mgmcs.json
```

两个机器工具都只**增加**自己的网格和事件（`machine` / `madmom`，`machine_beat` /
`machine_downbeat`），不碰手机给的 calibration 一个字段。打开：
`http://localhost:8123/index.html?doc=歌名`。

### madmom 是怎么装上来的

它 2018 年发布，官方支持到 Python 3.7，在这台机器上要修四处才编得出（环境在
`D:\fa_tools\db_venv`：Python 3.11 + numpy 1.26 + madmom 0.16.1）：

1. 用 `'numpy<2'` 和 `'cython<3'` 装依赖，再用 `--no-build-isolation` 编译——madmom 是
   老 Cython 语法，Cython 3 生成的 C 会在 `PyCode_New` 上编译失败。
2. 3.11 把 `longintrepr.h` 挪进了 `cpython/`，而 madmom 生成的一段 C 直接 include 老名字。
   建一个转发头文件（内容就一行 `#include "cpython/longintrepr.h"`），用 `set CL=/I<目录>`
   让 cl.exe 找得到。这台机器上有 MSVC（VS Build Tools），所以能编。
3. madmom 用的是 `collections.MutableSequence`、`np.float`、`np.int` 这些 3.10/1.24 之后
   被删掉的名字；`tools/downbeats_madmom.py` 在 import 之前把别名补回去。
4. `madmom/features/downbeats.py` 里有一处 `np.asarray(results)[:, 1]`，numpy ≥ 1.24 不再
   接受这种不规则数组；那一步改成从列表里直接读。

结论：**Windows 上跑得了 madmom，不用 WSL**（之前记的"需要 WSL"是错的）。

## JSON 从哪来

两条路，都能用：

**一、手机自己导出**（新的）。手机上每次对拍都记一条网格，歌结束时（或停止收听时）
在歌的目录里写一份 `<歌名>.mgmcs.json`，和 `events.jsonl` 放在一起。里面有：

```json
{
  "calibrationId": "cal_01",
  "audioTime": 45.82,          // 歌曲时间，bar 0 beat 0
  "bpm": 89.021,
  "beatsPerBar": 4,
  "beatUnit": 4,
  "source": "human",
  "rawTaps": [                  // 产生它的每一次按压，两个钟都在
    { "tapIndex": 0, "button": "DOWNBEAT", "songTime": 36.897,
      "captureTime": 557.6, "clientTime": 1791518524738, "sessionId": 12 }
  ]
}
```

**二、从旧记录重算**（`tools/from_listening_ai.py`）。手机早先只写 `events.jsonl`，这份
转换器能把它们补出来：

```bash
python tools/from_listening_ai.py <listening 根目录> --check     # 只报告，不写文件
python tools/from_listening_ai.py <listening 根目录>             # 写在每首歌旁边
```

它按手机自己的规则重算，而不是抄当时的结论：

* **拍速**：相邻两次按压间隔的中位数。手机上两次按压就是两拍。
* **拍号**：两个「小节头」之间按了几下——是**数**出来的，不是除出来的。
* **bar 0 beat 0**：最后一次按「小节头」的那一下。

### 两个钟，这件事很重要

手机上每一条记录都有两个时间：

* `captureTime`（手机自己的钟）：从开始收听算起的真实时间，精确。
* `songTime`（播放器的位置）：验证器画图用的时钟，但它**一步一跳**——播放器高兴了才发布
  一次位置，所以可能比音乐实际位置晚最多约一秒。

所以拍速和拍号来自精确的那个钟，网格再用两个钟的差放到歌曲时间轴上。转换器会把观测到
的发布跨度报出来（例如「跨度 0.74 秒」），因为那就是网格相位的精度上限——**拍速是准的，
相位可能整体偏半秒以内**。不报这个数，研究者会去音乐里找一个代码里的毛病。

实测：一首 89 BPM 的歌（アイネクライネ），第一次写这份转换器时用错了钟，算出 58.65 BPM。
换成手机自己的规则后是 86.1 / 89.0 BPM、4/4，和人对拍时的听感一致。

## 界面

- **时间轴**：横轴是真实的 `audioTime`，所有网格和事件共用它。粗线 = 小节第一拍，细线 = 普通拍。
- **缩放**：滚轮在时间轴上滚动 = 以鼠标位置为中心缩放；也可以拖右上角的滑块；`适应全曲` 一键看全曲。
- **跳转**：点一小节的**空白处**跳到该小节第一拍；点**拍点**跳到那一拍。超出音频范围时不跳，
  只给一句提示。
- **事件层**：三行——声部注意 / 变化点 / 其他。事件只按真实秒数画一次；重叠时向上堆叠；
  左侧筛选框可以关掉某一类。`attention_in` 和 `attention_out` 成对时会自动画成一条区间条，
  **这只是显示**，原始 JSON 里仍然是两个独立事件；配不上对的就只显示点。
- **点事件**：右侧显示它在每条网格里的坐标，播放头跳过去，同时可以记下「它更像哪条网格」。
- **点小节**：跳过去，并列出这一小节附近有哪些事件。
- **网格**：每条网格可以隐藏 / Solo，并可以打「有效 / 无效 / 说不清」——记的是**按下那一刻**的
  播放头位置（`atTime`）。规格里没有时间段选择，所以这里也没有。
- **标记**：`meter_change / tempo_change / half_bar / phase_shift / grid_reentry / uncertain`，
  可以带 from/to 和备注，打在播放头位置。

判过的每一条都会自动存进浏览器本机缓存（同一个 JSON 再打开会自动恢复），要留档点
**保存 song.validation.json**。存下来的永远是独立的一份，导入的 JSON 不会被改。

## JSON 格式（手机 → 电脑）

```json
{
  "schemaVersion": "0.1",
  "song": {
    "songId": "song_001",
    "title": "Example",
    "audioFileName": "song.flac",
    "durationSeconds": 226.12
  },
  "calibrations": [
    { "calibrationId": "cal_001", "audioTime": 31.428, "bpm": 120,
      "beatsPerBar": 4, "beatUnit": 4, "source": "human" }
  ],
  "events": [
    { "eventId": "evt_001", "audioTime": 48.225, "type": "guitar_attention_in" },
    { "eventId": "evt_002", "startTime": 60.0, "endTime": 66.5,
      "type": "attention_span", "payload": { "part": "bass" } }
  ]
}
```

- 每条 `calibration` 独立变成一条网格，`audioTime` 就是它的 `bar 0 beat 0`，向两头无限延伸。
  新的校准永远是新网格，**不覆盖旧的**。`songId` 和 `calibrationId` 是后面认领数据的依据。
- **一条网格也可以写成小节线列表**，代替 `audioTime`：`"barLines": [{"time": 1.974}, …]`，
  第一条就是 `bar 0`。这是给"小节不等长"的歌用的——拍速和拍号仍然要有（拍点还是等距的），
  但它们写不出"这一小节只有两拍"。列表是**一个读法**，不是测量：页面把它画出来、
  给不等长的小节打上斜纹，由人判断。列表里的网格不参与起点相位比较，也不参与半小节检测，
  因为半小节已经写在它的接缝里了。
- 事件有两种：有 `audioTime` 的是点事件，有 `startTime`/`endTime` 的是区间事件。
  **两者都必须保留真实秒数**，不能只存 bar/beat。
- `bar` / `beat` 一律 0-based：`bar 0 beat 0` 就是校准点，往前的 bar 是负数，beat 是小数。
- 缺字段、`bpm` 不是正数、区间倒置这类问题不会让整个文件打不开：页面顶部会列出问题，
  能画的照画。不会替你把缺的 bpm 补成 120。

## 输出：`<歌名>.validation.json`

## 时间坐标映射（Time Coordinate Map v0.1）

一个模块，四个调用。它只做坐标换算，不做 BPM 检测、拍号识别、打拍、置信度，也不替你挑网格。

```js
const map = MGMCS.coordinateMap(doc);            // 浏览器侧
map.timeToCoordinate(72.5, 'grid_A');            // → { bar 20, beat 2.144 }
map.coordinateToTime(20, 2.144, 'grid_A');       // → { audioTime 72.5 }
map.projectTime(72.5);                           // → 同一时刻在每条网格上的坐标
map.generateAnchors('grid_A', 29, 32);           // → 这个窗口里的小节线/拍线
```

```python
import coordinate_map as cm                       # Python 侧，同一套算术
m = cm.CoordinateMap(cm.load_song(json.load(open(path, encoding="utf-8"))))
m.time_to_coordinate(72.5, "grid_A")
```

命令行只写参数，不写查表：

```bat
python tools\generate_map.py song.mgmcs.json --grids
python tools\generate_map.py song.mgmcs.json --anchors 20 24 --grid cal_01
```

实测：一首 3:44 的歌、四条网格、外加一个 4 秒窗口的锚点，输出 **1.5 KB**。因为映射是一条线性式
（`t = originTime + (bar × beatsPerBar + beat) × 60 / bpm`），文件大小跟"变化次数"成正比，
不跟歌长成正比——坐标实时算，不预生成。

规则：每条网格独立，不合并、不覆盖、不替你选唯一一条、不改原始 `originTime`。`bpm <= 0`、
`beatsPerBar <= 0`、`gridId` 不存在、`audioTime` 非数字都返回 `{"error": ...}`；
超出音频范围不算错，照算并带上 `outOfAudioRange: true`（网格必须能往两头延伸）。

两边的实现（`static/mgmcs.js` 的 `coordinateMap` 和 `tools/coordinate_map.py`）用**同一份夹具**
`tests/coordinate_fixtures.json` 校验，那份夹具的期望值是按规格公式手算的，不从任何一份实现里
跑出来——同一套算术实现两次，就是给自己留一个"两边悄悄不一致"的机会，所以让它俩必须同时过。

v0.1 的边界：规格的输入格式（`originTime` + 常数 BPM）表达不了"小节不等长"，所以 Python 侧
遇到 `barLines` 那种网格会明确拒收并提示用浏览器侧；浏览器侧支持它。

```json
{
  "schemaVersion": "0.1",
  "songId": "song_001",
  "gridJudgements": [],
  "changeMarkers": [],
  "eventJudgements": [],
  "notes": []
}
```

采集数据与检验数据彻底分开：这份文件只装人说的东西。

## 自测

```bash
node --test                                      # 44 个：坐标、投影、相位差、事件、文件配对
python -m unittest discover -s tests -p "test_*.py"   # 86 个：转换器 + 坐标映射 + 网格诊断（A/B 两层）+ Mapperatorinator 接入 + 体检表分类
```

`node --test` 覆盖规格里写下来的每一个数：§5 的两个投影、§7、§11 的半小节相位差、
§16 的 Test A–E，事件配对与堆叠，以及**手机导出的那份文件能不能被读进来**（这是两端的
契约）。Python 那份覆盖转换器：拍速来自哪个钟、拍号怎么数、两次对拍是两条网格、区间倒置、
没人回答的问题、看不出来的字段怎么报告。改坐标数学或转换规则之前先跑它们。

## 目录

```
mgmcs_validator/
  run.bat                     双击运行
  static/                     index.html / style.css / mgmcs.js / events.js / pairing.js / app.js
  demo/                       演示音频与 JSON
  tools/make_demo_song.py     重新生成演示数据（只用 Python 标准库）
  tools/from_listening_ai.py  把手机早先的 events.jsonl 转成验证器能读的 JSON
  tests/                      node --test 与 python -m unittest 的测试
```

## v0.1 没做

自动识别 BPM / 拍号 / 变速 / 半小节、自动置信度、云端、账号、数据库、移动端标注、
音频编辑、波形。
