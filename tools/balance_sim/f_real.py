"""F_REAL - port de las formulas REALES del servidor (MobaSkillDamage, MobaDefense, MobaCombatStats, MobaProgression, MobaItemPower, MobaArchetype)
a las entradas del simulador. Es la linea base del juego actual. Las skills no se simulan una por una: `hps` y un factor de kit por clase (kit_XX,
se ajusta contra los duelos reales con calibrar.py) resumen la rotacion de cada clase. NO incluye: tope de 30 % de vida por golpe, freno de rafaga,
defensa plana (P1) ni los pasivos de clase (son efectos pequenos frente a las diferencias de kit)."""
import numpy as np
import sim
import kits

FORMULA_TEXT = ("hit = (95+24(r-1)) x 1,5 x (1+Expansion) x (1+poder_piezas) x (1 + 0,55(1+0,6(r-1)/4) x 2,5 x [0,45 stats + 0,20 arbol + 0,35 items]) x EscalaNivel x tilt ; "
                "dps = hit x (1+crit x 0,75) x hps x kit_clase ; vida = curva x tilt x 1,2 ; mitigacion = 0,45 stats + 0,20 arbol + 0,35 items (tope 70 %)")
CLS = ["BK", "RF", "DL", "MG", "FE", "GM", "SUM"]
PARAMS = {"hps": (0.796, 0.1, 3.0)}
KIT_CAL = {"BK": 0.79, "RF": 0.432, "DL": 0.575, "MG": 0.75, "FE": 1.685, "GM": 1.134, "SUM": 1.536}
PARAMS.update({f"kit_{c}": (KIT_CAL[c], 0.2, 4.0) for c in CLS})
OFFCAP = np.array([100.0, 64.0, 100.0, 100.0, 80.0, 80.0, 100.0])
BASIC_LS = np.array([0.14, 0.14, 0.10, 0.10, 0.12, 0.08, 0.08])
TENACITY = np.array([0.30, 0.30, 0.15, 0.0, 0.0, 0.0, 0.0])
KITS = None


def _kit(b, p):
    return np.array([p[f"kit_{c}"] for c in CLS])[b.cls]


def _offense_items(b):
    only_weapon = (b.cls == 4) | (b.cls == 1)
    return np.where(only_weapon, b.tier[:, 0] / 3.0, (b.tier[:, 0] + b.tier[:, 1]) / 6.0)


def _sp(b):
    t = b.tier / 3.0
    arm = 0.05 * (t[:, 3] * (b.role[:, 3] == 0) + t[:, 4] * (b.role[:, 4] == 0))
    wep = 0.03 * t[:, 0]
    acc = 0.04 * ((t[:, 9] * (b.role[:, 9] == 0)) + (t[:, 10] * (b.role[:, 10] == 0)))
    return arm + wep + acc


def _frac(b):
    stat = np.clip(b.primary / 30000.0, 0, 1)
    tree = np.clip(b.tree[:, 0] / OFFCAP[b.cls], 0, 1)
    return 0.45 * stat + 0.20 * tree + 0.35 * _offense_items(b)


def _factors(b, p):
    rot = kits.rotation(b, _frac(b))
    ex = np.where(b.buff[:, 1] > 0, 0.08 + 0.02 * (b.buff[:, 1] - 1), 0.0)
    return {
        "rotacion_del_kit": rot["steady"],
        "expansion": 1 + ex,
        "poder_piezas": 1 + _sp(b),
        "escala_nivel": sim.ref_damage_scale(b.L),
        "tilt": b.dmg_tilt,
    }, rot


def attack(b, p):
    f, rot = _factors(b, p)
    hit = np.ones(b.n)
    for v in f.values():
        hit = hit * v
    agi = np.maximum(0.0, b.stats[:, 1])
    t = b.tier / 3.0
    crit_arch = 0.03 * (t[:, 5] * (b.role[:, 5] == 0) + t[:, 6] * (b.role[:, 6] == 0)) + 0.02 * (t[:, 9] * (b.role[:, 9] == 0) + t[:, 10] * (b.role[:, 10] == 0))
    s = 0.6 * agi / (agi + 14000.0)
    tr = np.clip(b.tree[:, 2] / 60.0, 0, 1) * 0.6
    crit = np.clip(0.45 * s + 0.20 * tr + crit_arch, 0, 0.6)
    cm = 1.75 + np.where(b.buff[:, 8] > 0, 0.15 + 0.05 * (b.buff[:, 8] - 1), 0.0)
    dps = hit * (1 + crit * (cm - 1)) * p["hps"] * _kit(b, p)
    anti = np.array([0, 0.10, 0.20, 0.30])[b.tier[:, 0]] * 0.6
    return {"dps_phys": dps * (1 - b.mag), "dps_mag": dps * b.mag, "pen_phys": 0.0, "pen_mag": 0.0, "lifesteal": 0.61 * BASIC_LS[b.cls], "antiheal": anti,
            "burst_mult": rot["burst_mult"], "sustain": rot["sustain"], "aoe_share": rot["aoe_share"]}


def defense(b, p):
    vit = b.stats[:, 3]
    boost = 1 + 0.2 * np.clip((vit + b.primary) / np.maximum(0.3 * b.stat_budget, 1.0), 0, 1)
    hp = sim.ref_health(b.L) * b.hp_tilt * boost
    shield = hp / 3.0
    stats_m = 0.12 + 0.58 * vit / (vit + 12000.0)
    tree_m = np.clip(b.tree[:, 1] / 100.0, 0, 1) * 0.7
    cnt = np.where(b.cls == 3, 8, np.where(b.cls == 1, 8, 9))
    slots = [2, 3, 4, 5, 6, 7, 8, 9, 10]
    tsum = b.tier[:, slots].sum(axis=1)
    items_m = np.clip(tsum / (3.0 * cnt), 0, 1) * 0.7
    mit = np.minimum(0.7, 0.45 * stats_m + 0.20 * tree_m + 0.35 * items_m)
    t = b.tier / 3.0
    dur_tree = 0.15 * np.clip(b.tree[:, 3] / 100.0, 0, 1)
    fam_dur = np.array([0.05, 0.05, 0.06, 0.06, 0.0, 0.0, 0.0])[b.cls]
    dur_items = fam_dur * (t[:, 3] * (b.role[:, 3] == 1) + t[:, 4] * (b.role[:, 4] == 1)) + 0.03 * (t[:, 9] * (b.role[:, 9] == 1) + t[:, 10] * (b.role[:, 10] == 1))
    dmg = (1 - dur_tree) * (1 - dur_items)
    dmg = dmg * np.where(b.buff[:, 2] > 0, 1 - (0.12 + 0.03 * (b.buff[:, 2] - 1)), 1.0) * np.where(b.buff[:, 5] > 0, 1 - (0.12 + 0.03 * (b.buff[:, 5] - 1)), 1.0)
    dmg = dmg * (1 - 0.05 * b.pet[:, 1])
    regen = hp * (0.007 * (t[:, 5] * (b.role[:, 5] == 1) + t[:, 6] * (b.role[:, 6] == 1)) + 0.004 * t[:, 0] * (b.role[:, 0] == 1))
    off_tier = b.tier[:, 1]
    aeg = np.where(off_tier > 0, 0.2 * hp, 0.0)
    aeg_cd = np.array([60.0, 60.0, 50.0, 40.0])[off_tier]
    ten = TENACITY[b.cls] + 0.0
    return {"hp": hp, "shield": shield, "mit_phys": mit, "mit_mag": mit, "dmg_taken_mult": dmg, "regen": regen, "aegis": aeg, "aegis_cd": aeg_cd, "tenacity": ten}


def decompose(b, p):
    return {"mode": "mult", "factors": _factors(b, p)[0]}
