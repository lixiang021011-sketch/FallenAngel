> **English summary** — Implementation spec for the shared "condition group + effect group" engine that
> gives room options real, atomic effects. Structure is adapted from the Arknights Integrated Strategies
> configuration breakdown (`D:\肉鸽系统配置与说明_v2`, tables 11–14); the file layout of that breakdown
> (three-row headers, Chinese worksheet names) is deliberately **not** adopted — this project keeps its
> own code-held tables plus `Validate()` gate, the same shape as `RoomContentCatalog` / `ModifierCatalog`.
> Scope: rooms only. Shop, talents and reroute keep their current wiring until a later slice.

---

# 条件组与效果组：房间选项的结算规格

> 状态：**实现规格稿**（2026-10-01 起草）。结构参照《肉鸽配置表与写表思路_v2》的 11–14 号表
> （ConditionGroup / ConditionEntry / EffectGroup / EffectEntry），**只借结构不借版式**——
> 不引入三行表头，也不改 `config/` 那套导出管线；新表仍按本项目惯例由代码持有并自带 `Validate()`。
> 设计基线是 `docs/roguelike-chapter1-design.md`，代码规范是 `docs/architecture.md`。

---

## 1. 为什么加这一层

`RoomOptionRow` 原来是"一给一拿"两格（`GiveKind/GiveId/GiveAmount/TakeKind/...`）：一个选项最多
给一样、要一样，条件只能写"要什么"，多条效果没法表达。而已经写进 `docs/chapter-rooms-spec.md` §6
的事件草案全都要更强的表达：

| 草案事件 | 需要的能力 |
| --- | --- |
| EV01：一件通用件 ← 本层负面状态 | 一个选项里"给一件 + 加一条负面"，一起提交 |
| EV02：遇到旅伴 ← 人情 1 | 代价不是钱而是另一种资源 |
| 商店置换：先看报价再删收藏品 | 先预检、再提交，中间不能半途而废 |

所以把"给什么/要什么"换成两层：**条件组**（能不能选）与**效果组**（选了之后按序发生什么）。

---

## 2. 四张表

### `ConditionCatalog`（条件组 + 条件条目）

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `condition_group_id` | string | 主键 |
| `match_mode` | enum | `ALL` 全部成立 / `ANY` 任一成立 |
| `remark` | string | 用途备注，不参与判断 |

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `condition_id` | string | 主键 |
| `condition_group_id` | ref | 所属组 |
| `sort` | int | 组内顺序（只影响可读性，不影响结果） |
| `condition_type` | enum | 判断口径，**闸门**：只许写这里列出的词 |
| `target_id` | string | `HELD_EQUIPMENT` / `HAS_MODIFIER` 指向的对象；其余必须留空 |
| `compare_op` | enum | `EQ/NE/GT/GE/LT/LE` |
| `compare_value` | string | 比较值，按口径解析成整数；状态类口径用 `1`＝有、`0`＝无 |

起步口径（`ConditionType`）：`RUN_CASH` 局内现金、`HELD_EQUIPMENT` 是否持有某件器材、
`EQUIPMENT_COUNT` 持有件数、`LAYER` 当前层、`COMPLETED_SONGS` 已完成演奏场数、
`HAS_MODIFIER` 是否带着某条 modifier。

### `EffectCatalog`（效果组 + 效果条目）

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `effect_group_id` | string | 主键 |
| `remark` | string | 用途备注 |

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `effect_id` | string | 主键 |
| `effect_group_id` | ref | 所属组 |
| `sort` | int | 组内执行顺序 |
| `effect_type` | enum | 效果类型，**闸门**：只许写这里列出的词 |
| `target_id` | string | 器材 id / modifier id；`GIVE_CASH`、`TAKE_CASH`、`CLEAR_DEBUFF` 必须留空 |
| `value` | string | 参数，按类型解析（现金类＝金额，器材类＝数量） |
| `condition_group_id` | ref | 附加条件，**空 = 无条件执行**；不成立时跳过这一条，不判整组失败 |

起步效果（`EffectType`）：`GIVE_CASH`、`TAKE_CASH`、`GIVE_EQUIPMENT`、`ADD_MODIFIER`、`CLEAR_DEBUFF`。

---

## 3. 运行时：两段式，保证原子

`RoomEffectRunner.Run(effectGroupId, run, context, commit)` 是唯一入口，`commit` 决定是试算还是落地。
`CanApplyRoomOption` 走试算，`ApplyRoomOption` 走落地——两条路共用同一段代码，不会"按钮能点但点了失败"。

1. **筛条目**：附加条件按**提交前**的状态判断，不成立的条目整条跳过（同一组里前面的效果不影响后面的条件）。
2. **试算**：从当前快照出发逐条算增量（现金加减、器材追加、modifier 列表、净化条数），
   每一条都校验能不能落地——钱够不够、器材在不在且已实现、是不是已经有一件、容量到没到、modifier 系统在不在。
   **任一条不过就整组放弃**，返回原因，`run` 一个字段都不动。
3. **落地**：全过之后再一次性写入快照与 modifier 系统，然后调用方 `Save` 一次。

代价与收益在同一个效果组里时（EV01 那种），第 2 步用的是同一条影子状态，
所以"扣了代价但给不出东西"这种半成品状态不可能出现。

---

## 4. 幂等

一局的快照新增 `resolvedRoomChoices`，存 `"节点ID|选项ID"`。同一个选项重复提交只写日志、直接返回，
不重复发奖——关掉游戏重进同一间房不会白拿第二份。

---

## 5. 与现有代码的关系

| 现有 | 这次怎么变 |
| --- | --- |
| `RoomOptionRow`（一给一拿） | 改成 `(optionId, labelKey, conditionGroupId, effectGroupId, enabled)` |
| `RoomContentCatalog.Validate` | 改成校验"引用的条件组/效果组存在"，效果词闸门移到 `EffectCatalog` |
| `RerouteCatalog` | **不动**。它仍用 `RoomContentCatalog.IsWired` 那把旧闸门（改路消耗是另一条线） |
| `ModifierManager` | 只从 `RoomEffectRunner` 读；来源固定 `ModifierSource.Node` |
| `AcquireEquipment` | **不动**。它只认 `equipment_base` 的 E 系；房间给的是 `GearCatalog` 的 CG 系器材 |

> 遗留问题（本次不解决，需作者拍板）：一局里的"器材"现在有两套——`GearCatalog` 的 CG01–CG16
> （设计稿 §4.2 的器材）与 `PortfolioConfig.EquipmentBase` 的 E01–E10（成长验证那条线的装备）。
> 商店与掉落仍走 E 系，房间走 CG 系。这次的 `GIVE_EQUIPMENT` 按房间的既有引用落 CG，
> 并让局内快照同时接受两类 id；两套要不要并成一套，是下一步的设计决定。

---

## 6. 验收

分两处（menu 与 `-executeMethod` 两用），共 34 项：

`RoomContentChecks`（21 项，纯表与结算器）：

1. 条件组：`ALL` / `ANY` 的差别、每个口径的取值、空 id＝不限制、缺组不成立，写错的行要点名。
2. 效果组：排序执行、金额与数量的类型校验、器材与 modifier 的引用校验、闸门拦住没实现的词。
3. 原子性：`TAKE_CASH` 不够钱时整组失败，且现金与器材都没有变化。
4. 试算不落地：`commit: false` 跑完整组，状态一动不动。
5. 附加条件：不成立的条目被跳过，同组其他条目照常执行。
6. modifier 系统不可用时，含 modifier 的效果组在预检阶段整组失败。

`PortfolioChecks`（新增 13 项，进程内 + 临时存档）：

7. 选项必须是这间房真的提供的（隔空选别的房间的选项会被拒）。
8. 结算真的落库、重开读回来还在；同一个"节点|选项"提交两次不重复发奖。
9. 给器材这条链路走完 `Save → ReadRun` 的快照校验（两套器材并存期间快照两边都认）。

验证命令：

```
Unity.exe -batchmode -nographics -quit -projectPath <工程> -executeMethod FallenAngel.Core.RoomContentChecks.Run
```

---

## 7. 这次不做

- **房间面板的按钮**。选项要真的能在游戏里点，需要面板视觉先过稿（铁律 8），
  09-27 的提交里也把它列为"最后一块"。这次只把引擎与数据做通。
- 商店、改路、天赋迁移到同一套条件/效果。它们留在旧链路上，下一步再谈。
- 奖励池 / 奖励候选包（v2 的 15–23）。三选一现在仍由内容行的三个选项表达，够用。
