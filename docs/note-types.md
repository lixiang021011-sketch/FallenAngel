> **English summary** — The mapping table from musical notation to FallenAngel note types, per instrument (drums, guitar, bass). It fixes the lane-colour convention (0 red / 1 yellow / 2 blue / 3 green, higher pitch further right), what flick direction means (up for upward technique, down for downward or muted technique), how a slide path is derived from pitch, the hold threshold (two beats or longer becomes a hold, applied uniformly across instruments), and the two-lane ceiling for simultaneous notes. The authoritative version has been folded back into chart-format.md §3.3; this table remains the change baseline. The full text below is in Chinese.


---

# 音符对照表（2026-08-18 已全部确认）

> 记谱 → FallenAngel 音符的映射总表。**权威版已回填 `docs/谱面转换协议.md` §3.3**；本表留存为变更底稿——后续要改映射请直接编辑本表，我会同步协议文档与 `chart_tools/rules/default.json`。
> 修改方式：改单元格内容，行结构保持不动表格就不会乱。

## 0. 音符类型速查

| 类型 | 判定 | 说明 |
|---|---|---|
| tap | 标准窗口 | 单击 |
| hold | 头+身+尾 | 定轨长按 |
| drag | 碰即 Perfect，永不 MISS | 宽松（Phigros 语义） |
| flick | 窗口 + 方向 | 方向 = 技法状态（见下） |
| slide | 头窗口 + 跟随 + 松手结束 | 绿条，轨道间平滑横移 |

**通用约定**
- 轨道：0 红 / 1 黄 / 2 蓝 / 3 绿；音高越高轨道越右（与鼓一致：低音 0 → 高音 3）
- flick 方向语义：**上行技法 → up，下行/闷音技法 → down**
- slide 路径：音高 → 连续 x 坐标线性映射，duration ≥ 0.15s 才生成（过短降级 tap；推弦 slide 例外，见风格参数）
- hold 阈值：**以拍数为临界，全乐器统一：二分音符（2 拍）以内 tap、二分及以上 hold**；tie 链合并后计拍
- **同刻多音一律压缩 ≤2 轨（双轨上限）**
- 未命中任何规则 → tap（音高→轨道）

---

## 1. 鼓 ✅

| 状态 | 记谱/鼓件 | MIDI | 音符 | 轨道 | 方向 |
|---|---|---|---|---|---|
| ✅ | 底鼓 | 35, 36 | tap | 0 | — |
| ✅ | 军鼓 | 37, 38, 40 | tap | 1 | — |
| ✅ | 嗵鼓 | 41, 43, 45, 47, 48, 50 | tap | 3（绿键=吊镲+嗵） | — |
| ✅ | 闭镲 | 42 | tap | 2 | — |
| ✅ | 开镲 | 46 | flick | 2 | **up** |
| ✅ | 部分闭镲/踏板镲 | 44 | flick | 2 | **down** |
| ✅ | 吊镲/叮叮 | 49, 51, 52, 55, 57, 59 | tap | 3 | — |
| ✅ | 滚奏/震音 | tremolo | drag | 2 或 3 | — |

> 嗵鼓→3 是双指版手感方案（绿键重映射）；若需忠实谱面模式，嗵鼓回 2。

---

## 2. 合成器 ✅

| 状态 | 记谱/来源 | 条件 | 音符 | 轨道 | 方向 | 备注 |
|---|---|---|---|---|---|---|
| ✅ | 普通音符 | duration < 2 拍（二分以内） | tap | 音高→轨道 | — | |
| ✅ | 持续音/pad（延音线或长时值） | ≥ 2 拍（二分及以上） | hold | 音高→轨道 | — | |
| ✅ | 滑音/glissando/弯音 | 音高连续变化 ≥0.15s | slide | path 音高→x | — | |
| ✅ | 颤音 trill | 两音快速交替 | drag | 音高→轨道 | — | 宽松判定 |
| ✅ | 装饰音 | grace | flick | 音高→轨道 | 上行 up / 下行 down | |
| ✅ | 和弦（pad） | 同刻多音 | 拆轨至 ≤2 键 | 各音高轨道 | — | 一律双轨 |

---

## 3. 贝斯 ✅

| 状态 | 记谱/来源 | 条件 | 音符 | 轨道 | 方向 | 备注 |
|---|---|---|---|---|---|---|
| ✅ | 普通音符 | duration < 2 拍 | tap | 音高→轨道 | — | |
| ✅ | 持续音（延音线） | tie 链 ≥ 2 拍 | hold | 音高→轨道 | — | |
| ✅ | 滑弦/gliss | 音高连续变化 ≥0.15s | slide | path 音高→x | — | 贝斯滑弦很典型 |
| ✅ | 幽灵音/哑音（密集） | 间隔 ≤ 四分音符 | drag | 所在音高轨道 | — | 宽松判定 |
| ✅ | 幽灵音（稀疏）+ slap | 间隔 > 四分 | flick | 所在音高轨道 | **down** | 打击/闷语义→down |
| ✅ | 击弦 hammer-on | 连音线内，音高上行 | flick | 音高→轨道 | **up** | |
| ✅ | 勾弦 pull-off | 连音线内，音高下行 | drag | 音高→轨道 | — | |

---

## 4. 吉他 ✅

| 状态 | 记谱/来源 | 条件 | 音符 | 轨道 | 方向 | 备注 |
|---|---|---|---|---|---|---|
| ✅ | 普通音符 | duration < 2 拍 | tap | 音高→轨道 | — | |
| ✅ | 持续音（tie/let-ring） | ≥ 2 拍 | hold | 音高→轨道 | — | let-ring 余音作 hold |
| ✅ | 滑弦（legato slide/滑棒） | 音高连续变化 ≥0.15s | slide | path 音高→x | — | |
| ✅ | 震音拨弦 tremolo picking | 快速重复单音 | drag | 音高→轨道 | — | 宽松判定 |
| ✅ | 击弦 hammer-on | grace/连音线，音高上行 | flick | 音高→轨道 | **up** | |
| ✅ | 勾弦 pull-off | 连音线，音高下行 | flick | 音高→轨道 | **down** | |
| ✅ | 推弦 bend | 音高弯折 | slide | path：起 x → x+位移 | 右移 | **位移 = 半音数 × 0.5 轨（全音 = 1 整轨）** |
| ✅ | 放弦 release | 推弦回落 | slide | path：起 x → x−位移 | 左移 | 位移同上 |
| ✅ | 扫弦 | 和弦快速拨奏 | drag | 跨轨道 | — | 快速跨轨 drag |

---

## 5. 跨乐器通用规则 ✅

| 状态 | 来源 | 处理 |
|---|---|---|
| ✅ | BPM 变化 | events.bpm（前端换算时间按段积分） |
| ✅ | 拍号变化 | 仅影响网格换算，不生成音符 |
| ✅ | 同刻多音 | 一律压缩 ≤2 轨（双轨上限，chord_split） |
| ✅ | 超密段（>8 音/秒） | 转换报告警告，不静默删音 |
| ✅ | 同轨长音重叠 | 取先结束者截断，后者降级 tap（警告） |
| ✅ | 力度/重音 accent | 不参与类型映射，保留字段供未来修饰 |

## 6. 风格参数（每首歌可覆盖）

| 参数 | 默认 | 说明 |
|---|---|---|
| hold_threshold_beats | **2 拍（二分）✅ 全乐器统一** | tie 链合并后计拍，引擎按 BPM 换算成秒 |
| slide_min_duration | 0.15s | 过短降级 tap；**推弦 slide 不受此限** |
| max_simultaneous | 2 ✅ | 多音同时响一律双轨 |
| flick_enabled | 鼓/吉他 开；贝斯 部分开（击弦 flick、勾弦 drag） | 每乐器可关 |
| bend_as_slide | 开 ✅ | 位移 = 半音 × 0.5 轨 |
| ghost_density | ≤四分 → drag；稀疏/slap → flick down | 贝斯幽灵音分派 |

---

**编辑说明**：改「音符/轨道/方向/条件」列的单元格内容即可，行结构保持不动表格就不会乱；改完后告诉我，我同步协议文档 §3.3 与 `chart_tools/rules/default.json`。
