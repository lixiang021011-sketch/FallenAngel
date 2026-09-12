# -*- coding: utf-8 -*-
"""FallenAngel 数值平衡模拟：读取导出 JSON，模拟局循环（路线→演奏收益→商店→天赋解锁），
产出各水平玩家的成长节奏与收益构成报告。只读数据，不修改配置。
用法：python balance_sim.py [--runs N] [--seeds K]
"""
import argparse
import json
import os
import random
import sys

sys.stdout.reconfigure(encoding="utf-8")

DIR = os.path.dirname(os.path.abspath(__file__))
EXPORTED = os.path.join(DIR, "exported")


def load(name):
    with open(os.path.join(EXPORTED, name), encoding="utf-8") as f:
        return json.load(f)


STAGES = {s["stage_id"]: s for s in load("stages.json")}
MAP_NODES = {n["node_id"]: n for n in load("map_nodes.json")}
EDGES = load("map_edges.json")
TALENT_NODES = load("talent_nodes.json")
TALENT_NODE_BY_ID = {n["node_id"]: n for n in TALENT_NODES}
TALENT_EDGES = load("talent_edges.json")
TALENT_EFFECTS = load("talent_effects.json")
EQUIP_BASE = {e["equipment_id"]: e for e in load("equipment_base.json")}
EQUIP_EFFECTS = load("equipment_effects.json")
SHOP_POOL = [c for c in load("shop_candidates.json") if c["enabled"]]

BASE_CANDIDATES = 4
CAPACITY = 20
INCOME_CAP_RATIO = 0.5
PERFECT_FULL = 0.9  # 与 PortfolioDefaults.PerfectFullThreshold 一致（I0 上层线）
COMPLETION_BONUS = 100  # PortfolioSession.completionBonus 原型值
PAID_ROUTE_FEE = max(e["route_price"] for e in EDGES)  # 从表读取（唯一付费边）


def free_route():
    node = MAP_NODES["N00"]
    visited, route = set(), []
    while node["node_id"] not in visited:
        visited.add(node["node_id"])
        if node["stage_id"]:
            route.append(node["stage_id"])
        if node["node_type"] == "FINAL":
            break
        edge = next(e for e in EDGES if e["from_node_id"] == node["node_id"] and e["route_price"] == 0)
        node = MAP_NODES[edge["to_node_id"]]
    return route


FREE_ROUTE = free_route()  # 免费路线关卡序列；付费分叉把第2首换成 S02


def first(effects, handler, scope=None):
    for e in effects:
        if e["handler"] == handler and (scope is None or e.get("target_scope") == scope):
            return e
    return None


def eff_coef(target, talents):
    """覆盖升级：modify_coefficient 指向目标时用其系数"""
    for e in talents:
        if e["handler"] == "modify_coefficient" and e.get("target_effect_id") == target["effect_id"]:
            return e["coefficient"]
    return target["coefficient"]


def settle(base_income, stage_type, talents, equip_effs, perfect_rate, miss_count, opening_cash):
    """与 PortfolioIncomeService.Compute 同语义（Lite 6 效果），只返回合计"""
    return settle_lines(base_income, stage_type, talents, equip_effs, perfect_rate, miss_count, opening_cash)[1]


def settle_lines(base_income, stage_type, talents, equip_effs, perfect_rate, miss_count, opening_cash):
    """明细版结算：返回 [(名称, 金额), ...] 与合计（顺序=原始直接→增幅→保底→封顶→经济）"""
    b = base_income
    lines = [("基础B", float(b))]
    direct = b
    i0 = first(talents, "perfect_goal_reward")
    if i0 and perfect_rate >= (i0["threshold"] or 0):
        coef = eff_coef(i0, talents)
        # 分级达标：≥上层线满额；仅过下层线（threshold）半额（与引擎同语义）
        if (i0["threshold"] or 0) < PERFECT_FULL and perfect_rate < PERFECT_FULL:
            coef *= 0.5
        amt = b * coef
        lines.append(("I0达标", amt))
        direct += amt
    j0 = first(talents, "challenge_reward")
    if j0 and stage_type == "PAID_CHALLENGE":
        amt = b * eff_coef(j0, talents)
        lines.append(("J0挑战", amt))
        direct += amt
    perf = direct
    d0 = first(talents, "source_bonus", scope="PERFORMANCE_WITH_COMPENSATION")
    if d0:
        amt = direct * d0["coefficient"]
        lines.append(("D0增幅", amt))
        perf += amt
    e05 = first(equip_effs, "no_miss_multiplier")
    if e05 and miss_count == 0:
        amt = direct * e05["coefficient"]
        lines.append(("E05无Miss", amt))
        perf += amt
    g0 = first(talents, "income_floor")
    if g0:
        floor = b * eff_coef(g0, talents)
        if perf < floor:
            lines.append(("G0保底", floor - perf))
            perf = floor
    cap = b * (1 + INCOME_CAP_RATIO)
    if perf > cap:
        lines.append(("封顶裁剪", -(perf - cap)))
        perf = cap
    econ = 0.0
    c1 = first(talents, "opening_balance_interest")
    if c1 and opening_cash > 0:
        amt = min(opening_cash * c1["coefficient"], b * (c1["value_cap_b"] or 0))
        if amt > 0:
            lines.append(("C1利息", amt))
        econ = amt
    return lines, perf + econ


def quote_price(item_id, held, talents, equip_effs):
    """报价：常驻折扣取最大（D1 / 持有 E07），买入 E07 自身不享受其折扣"""
    discount = 0.0
    d1 = first(talents, "purchase_discount")
    if d1:
        discount = max(discount, d1["coefficient"])
    if "E07" in held and item_id != "E07":
        e07 = next((e for e in equip_effs if e["handler"] == "purchase_discount"), None)
        if e07:
            discount = max(discount, e07["coefficient"])
    return int(EQUIP_BASE[item_id]["base_price"] * (1 - discount))


def draw_candidates(rng, held):
    pool = [c["equipment_id"] for c in SHOP_POOL if c["equipment_id"] not in held]
    target = min(BASE_CANDIDATES + (1 if "E10" in held else 0), len(pool))
    out = []
    for _ in range(target):
        total = sum(c["weight"] for c in SHOP_POOL if c["equipment_id"] in pool)
        if total <= 0:
            break
        roll = rng.randint(0, total - 1)
        picked = None
        for c in SHOP_POOL:
            if c["equipment_id"] not in pool:
                continue
            roll -= c["weight"]
            if roll < 0:
                picked = c["equipment_id"]
                break
        if picked is None:
            break
        out.append(picked)
        pool.remove(picked)
    return out


def shop_visit(rng, cash, held, talents, equip_effs):
    cands = draw_candidates(rng, held)
    bought = []
    while True:
        options = [c for c in cands if quote_price(c, held, talents, equip_effs) <= cash]
        if not options:
            break
        pick = min(options, key=lambda c: quote_price(c, held, talents, equip_effs))
        price = quote_price(pick, held, talents, equip_effs)
        cash -= price
        held.add(pick)
        cands.remove(pick)
        bought.append((pick, price))
    return cash, bought


def unlock_affordable(points, unlocked):
    while True:
        best = None
        for n in TALENT_NODES:
            if n["node_id"] in unlocked:
                continue
            cost = n.get("unlock_cost")
            if cost is None or cost > points:
                continue
            preds = [e["from_node_id"] for e in TALENT_EDGES if e["to_node_id"] == n["node_id"]]
            mode = n["prerequisite_mode"]
            ok = mode == "NONE" or (mode == "ANY" and any(p in unlocked for p in preds)) \
                or (mode == "ALL" and all(p in unlocked for p in preds))
            if ok and (best is None or cost < best["unlock_cost"]):
                best = n
        if best is None:
            break
        unlocked.add(best["node_id"])
        points -= best["unlock_cost"]
    return points


def talent_effects_of(unlocked):
    ids = {TALENT_NODE_BY_ID[t]["effect_id"] for t in unlocked
           if TALENT_NODE_BY_ID.get(t, {}).get("effect_id")}
    return [e for e in TALENT_EFFECTS if e["enabled"] and e["effect_id"] in ids]


def equip_effects_of(held):
    out = []
    for i in held:
        eq = EQUIP_BASE.get(i)
        if not eq:
            continue
        eff = next((e for e in EQUIP_EFFECTS if e["effect_id"] == eq["effect_id"]), None)
        if eff and eff["enabled"]:
            out.append(eff)
    return out


def simulate_run(rng, unlocked, perfect_rate, miss_count):
    """一局：免费路线（现金够时走付费分叉）→ 每曲结算 → 两次商店 → 通关积分+奖励 → 贪婪解锁"""
    cash, held = 0, set()
    talents = talent_effects_of(unlocked)
    equip_effs = equip_effects_of(held)
    income_total, points_earned = 0.0, 0
    stage_ids = list(FREE_ROUTE)
    for idx, sid in enumerate(stage_ids):
        if idx == 1 and cash >= PAID_ROUTE_FEE:  # 付费分叉：现金够则走 S02
            stage_ids[idx] = "S02"
            cash -= PAID_ROUTE_FEE
        stage = STAGES[stage_ids[idx]]
        income = settle(stage["base_income"], stage["stage_type"], talents, equip_effs,
                        perfect_rate, miss_count, float(cash))
        cash += int(income)
        income_total += income
        points_earned += stage["growth_score"]
        talents = talent_effects_of(unlocked)
        equip_effs = equip_effects_of(held)
        if idx < len(stage_ids) - 1:  # 每曲之间经过商店（N02/N05）
            cash, bought = shop_visit(rng, cash, held, talents, equip_effs)
            equip_effs = equip_effects_of(held)
    points_earned += COMPLETION_BONUS
    return cash, income_total, points_earned, held


def run_tier(seed, perfect_rate, miss_count, max_runs):
    rng = random.Random(seed)
    unlocked = set()
    points = 0
    rows = []
    for run in range(1, max_runs + 1):
        cash, income, earned, held = simulate_run(rng, unlocked, perfect_rate, miss_count)
        points += earned
        points = unlock_affordable(points, unlocked)
        rows.append({"run": run, "points": points, "unlocked": len(unlocked),
                     "cash": cash, "equipment": len(held), "income": income})
        if len(unlocked) == len(TALENT_NODES):
            break
    return rows


def milestone(rows, node_id):
    for r in rows:
        if True:  # 里程碑按点数近似：该节点成本与贪心顺序下的解锁时点由 unlock 计数推不出单节点，用"解锁数达到含该层"近似
            pass
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--runs", type=int, default=15)
    ap.add_argument("--seeds", type=int, default=5)
    args = ap.parse_args()

    tiers = {"高手(全Perfect无Miss)": (1.0, 0), "普通(85%Perfect少量Miss)": (0.85, 2), "新手(60%Perfect较多Miss)": (0.60, 8)}
    total_cost = sum(n["unlock_cost"] or 0 for n in TALENT_NODES)
    print("=" * 72)
    print("FallenAngel 数值平衡模拟")
    print(f"免费路线: {' → '.join(FREE_ROUTE)}  (第2曲现金≥{PAID_ROUTE_FEE}时改走付费 S02)")
    print(f"天赋树: {len(TALENT_NODES)} 节点 / 总价 {total_cost} / 每局通关积分 {sum(STAGES[s]['growth_score'] for s in FREE_ROUTE)}+{COMPLETION_BONUS}(奖励) = {sum(STAGES[s]['growth_score'] for s in FREE_ROUTE)+COMPLETION_BONUS}")
    print(f"商店: 候选池 {len(SHOP_POOL)} 件 / 每次展示 {BASE_CANDIDATES} 件 / 容量 {CAPACITY}")
    print("=" * 72)

    for name, (pr, miss) in tiers.items():
        all_rows = [run_tier(100 + s, pr, miss, args.runs) for s in range(args.seeds)]
        n = min(len(r) for r in all_rows)
        avg = []
        for i in range(n):
            avg.append({k: sum(r[i][k] for r in all_rows) / args.seeds for k in ("run", "points", "unlocked", "cash", "equipment", "income")})
        print(f"\n### {name} ({args.seeds} 种子平均)")
        print(f"{'局':>3} {'总积分':>7} {'已解锁':>5} {'余现金':>7} {'装备':>4} {'累计收入':>9}")
        for r in avg:
            print(f"{r['run']:>3} {r['points']:>7.0f} {r['unlocked']:>5} {r['cash']:>7.0f} {r['equipment']:>4} {r['income']:>9.1f}")
        # 里程碑：解锁数达到 6/12/18/24 的局数
        def first_run_above(k):
            for r in avg:
                if r["unlocked"] >= k:
                    return r["run"]
            return None
        m = {k: first_run_above(k) for k in (6, 12, 18, 24)}
        print(f"里程碑(解锁≥6/12/18/24): 第{m[6]}局 / 第{m[12]}局 / 第{m[18]}局 / 第{m[24]}局")

    # 满配收益构成
    print("\n" + "=" * 72)
    print("满配收益构成(每曲,按结算引擎同语义;开场现金 100 计利息)")
    print("=" * 72)
    all_unlocked = {n["node_id"] for n in TALENT_NODES}
    all_talents = talent_effects_of(all_unlocked)
    e05_only = equip_effects_of({"E05"})
    for name, (pr, miss) in tiers.items():
        st = STAGES["S01"]
        lines, total = settle_lines(st["base_income"], st["stage_type"], all_talents, e05_only, pr, miss, 100)
        desc = "  ".join(f"{k}+{v:.1f}" for k, v in lines)
        print(f"{name} | S01: {desc} = {total:.1f}")
    st = STAGES["S02"]
    for name, (pr, miss) in tiers.items():
        lines, total = settle_lines(st["base_income"], st["stage_type"], all_talents, e05_only, pr, miss, 100)
        print(f"{name} | S02(付费挑战,路费{PAID_ROUTE_FEE}): {', '.join(f'{k}+{v:.1f}' for k, v in lines)} = {total:.1f} (净收益 {total-PAID_ROUTE_FEE:.1f})")
    print("\n注: 模拟为贪心策略(每局末解锁最便宜可解锁天赋/商店买最便宜可负担装备),真实玩家行为会不同;")


if __name__ == "__main__":
    main()
