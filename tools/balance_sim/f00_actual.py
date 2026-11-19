"""F00 - REFERENCIA: aproximacion del juego actual (mezcla 45/20/35 con topes). No es una propuesta; sirve para comparar."""
import numpy as np
import sim

FORMULA_TEXT = "hit = (Base+PorRango*(r-1))*1.5*(1+fortalecedor)*(1+Expansion)*(1+poder_piezas)*(1+ratio*2.5*fraccion)*EscalaNivel*tilt ; fraccion=.45 stats+.20 arbol+.35 items ; vida=curva*tilt*1.2 (tope) ; mitigacion=mezcla .45/.20/.35 (tope 70%)"
PARAMS = {"hps": (1.5, 0.5, 4.0)}


def _frac(b, stats_term, tree_term, items_term):
    return 0.45 * stats_term + 0.20 * tree_term + 0.35 * items_term


def _factors(b, p):
    r = b.rank
    base = (95 + 24 * (r - 1))
    stat = np.clip(b.primary / 30000.0, 0, 1)
    tree = np.clip(b.tree[:, 0] / 100.0, 0, 1)
    items = (b.tier[:, 0] + b.tier[:, 1]) / 6.0
    frac = _frac(b, stat, tree, items)
    ratio = 0.55 * (1 + 0.6 * (r - 1) / 4.0)
    sp = (b.tier_frac([3, 4, 0], roles=[0]) * 0.05 * 3 / 3.0 * 3)  # ~ +5%/+5%/+6% a T3 en piezas de ataque
    sp = np.minimum(sp, 0.16)
    ex_rank = b.buff[:, 1]
    expansion = np.where(ex_rank > 0, 0.08 + 0.02 * (ex_rank - 1), 0.0)
    gd = np.where(b.buff[:, 6] > 0, 0.06 + 0.02 * (b.buff[:, 6] - 1), 0.0)
    fort = np.minimum(0.45, b.tree[:, 0] * 0.015) * 0.5
    pet = b.pet[:, 0] * 0.15
    return {
        "base_skill": base * 1.5, "fortalecedor": 1 + fort, "expansion+greater_damage": 1 + expansion + gd, "poder_piezas": 1 + sp,
        "ratio_x_fraccion": 1 + ratio * 2.5 * frac, "escala_nivel": sim.ref_damage_scale(b.L), "tilt": b.dmg_tilt, "mascota_ataque": 1 + pet,
    }


def attack(b, p):
    f = _factors(b, p)
    hit = np.ones(b.n)
    for v in f.values():
        hit = hit * v
    agi = np.maximum(0, b.stats[:, 1] - 0)
    crit = np.clip(0.6 * agi / (agi + 14000) + 0.03 * b.tier_frac([5, 6], roles=[0]) + 0.2 * np.clip(b.tree[:, 2] / 60, 0, 1) * 0.6, 0, 0.6)
    cm = 1.75 + np.where(b.buff[:, 8] > 0, 0.15 + 0.05 * (b.buff[:, 8] - 1), 0)
    dps = hit * (1 + crit * (cm - 1)) * p["hps"]
    return {"dps_phys": dps * (1 - b.mag), "dps_mag": dps * b.mag, "pen_phys": np.zeros(b.n), "pen_mag": np.zeros(b.n), "lifesteal": 0.10 * np.ones(b.n)}


def defense(b, p):
    hp = sim.ref_health(b.L) * b.hp_tilt * (1 + 0.2 * np.clip((b.stats[:, 3] + b.primary) / np.maximum(0.3 * b.stat_budget, 1.0), 0, 1))
    hp = hp * (1 + 0.0)
    shield = hp / 3.0
    vit = b.stats[:, 3]
    s_stats = 0.12 + 0.58 * vit / (vit + 12000)
    s_tree = np.clip(b.tree[:, 1] / 100.0, 0, 1) * 0.7
    slots = [2, 3, 4, 5, 6, 7, 8, 9, 10]
    s_items = (b.tier[:, slots].sum(axis=1) / 27.0) * 0.7
    mit = np.minimum(0.7, 0.45 * s_stats + 0.20 * s_tree + 0.35 * s_items)
    dur_tree = 0.15 * np.clip(b.tree[:, 3] / 100.0, 0, 1)
    dur_items = 0.05 * b.tier_frac([3, 4], roles=[1]) + 0.03 * b.tier_frac([9, 10], roles=[1])
    dmg = (1 - dur_tree) * (1 - dur_items)
    dmg = dmg * np.where(b.buff[:, 2] > 0, 1 - (0.12 + 0.03 * (b.buff[:, 2] - 1)), 1.0)
    dmg = dmg * np.where(b.buff[:, 5] > 0, 1 - (0.12 + 0.03 * (b.buff[:, 5] - 1)), 1.0)
    dmg = dmg * (1 - 0.05 * b.pet[:, 1])
    regen = hp * 0.007 * b.tier_frac([5, 6, 0], roles=[1, 2])
    return {"hp": hp, "shield": shield, "mit_phys": mit, "mit_mag": mit, "dmg_taken_mult": dmg, "regen": regen}


def decompose(b, p):
    return {"mode": "mult", "factors": _factors(b, p)}
