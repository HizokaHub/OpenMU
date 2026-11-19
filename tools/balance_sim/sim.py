"""Simulador de formulas de balance del MOBA (herramienta de analisis; NO toca el juego).

Genera millones de personajes (nivel 1-30, 7 clases, stats, arbol, 11 ranuras de items con tier y rol, 4 mascotas, 15 buffs),
le pide a una FORMULA (modulo fNN_*.py) que los convierta en ataque y defensa, los hace pelear 1v1 y puntua la formula.
Ver INTERFACE.md. Uso:  python -I -X utf8 run.py <modulo> [--n 200000] [--params json]
"""
from __future__ import annotations

import importlib
import json
import math
import sys
from dataclasses import dataclass, field

import numpy as np

# ----------------------------------------------------------------------------------------------------------------------
# Catalogo de elementos
# ----------------------------------------------------------------------------------------------------------------------
CLASS_NAMES = ["BK", "RF", "DL", "MG", "FE", "GM", "SUM"]
STAT_NAMES = ["STR", "AGI", "ENE", "VIT", "CMD"]
TREE_NAMES = ["arbol_ofensa", "arbol_defensa", "arbol_critico", "arbol_durabilidad", "arbol_vida", "arbol_mana"]
SLOT_NAMES = ["arma", "escudo", "casco", "armadura", "pantalon", "guantes", "botas", "alas", "colgante", "anillo1", "anillo2"]
PET_NAMES = ["mascota_ataque", "mascota_guardia", "mascota_vital", "mascota_veloz"]
BUFF_NAMES = ["SoulBarrier", "Expansion", "Defense", "SwellLife", "Heal", "GreaterDefense", "GreaterDamage", "InfinityArrow",
              "IncCrit", "Reflection", "Berserker", "IgnoreDefense", "IncreaseHealth", "IncreaseBlock", "Recovery"]
ROLE_NAMES = ["ATAQUE", "DEFENSA", "RECURSOS", "UTILIDAD"]
SLOT_COST_WEIGHT = np.array([1.4, 1.0, 0.8, 1.2, 1.0, 0.7, 0.7, 1.3, 0.6, 0.6, 0.6])
TIER_PRICE = np.array([0.0, 1.0, 2.5, 6.25])

# Por clase: stat primario (indice en STAT_NAMES), tilt de vida, tilt de dano, parte de dano magico (0 fisico, 1 magico), puntos de stats totales.
CLS_PRIMARY = np.array([0, 0, 4, 2, 1, 2, 2])
CLS_SECONDARY = np.array([3, 1, 0, 0, 2, 3, 3])
CLS_HP_TILT = np.array([1.30, 1.30, 1.15, 1.00, 0.90, 0.90, 0.90])
CLS_DMG_TILT = np.array([1.02, 1.02, 0.95, 1.00, 1.08, 1.10, 1.10])
CLS_MAG = np.array([0.0, 0.0, 0.2, 0.5, 0.0, 1.0, 1.0])
CLS_STAT_TOTAL = np.array([100e3, 112e3, 130e3, 100e3, 100e3, 100e3, 112e3])
STAT_CAP = 30000.0
MAX_LEVEL = 30

N_STAT, N_TREE, N_SLOT, N_PET, N_BUFF = 5, 6, 11, 4, 15


def avail_tier(level):
    """Mejor tier que se puede llevar en ese nivel (oro): L<3 nada, <10 T1, <20 T2, resto T3."""
    level = np.asarray(level)
    return np.where(level < 3, 0, np.where(level < 10, 1, np.where(level < 20, 2, 3)))


# Curvas de referencia del juego actual (solo como punto de partida; una formula puede definir las suyas).
def ref_health(level):
    return 1900.0 + (50000.0 - 1900.0) * (np.asarray(level) - 1) / 29.0


def ref_mana(level):
    return 450.0 + (3400.0 - 450.0) * (np.asarray(level) - 1) / 29.0


def ref_damage_scale(level):
    return 1.0 + 14.0 * (np.asarray(level) - 1) / 29.0


def ref_flat_defense(level):
    return 15.0 + 385.0 * (np.asarray(level) - 1) / 29.0


# ----------------------------------------------------------------------------------------------------------------------
# Personajes
# ----------------------------------------------------------------------------------------------------------------------
@dataclass
class Builds:
    L: np.ndarray            # (N,) nivel 1..30
    cls: np.ndarray          # (N,) 0..6 segun CLASS_NAMES
    stats: np.ndarray        # (N,5) puntos invertidos (STR, AGI, ENE, VIT, CMD), cada uno <= 30000
    stat_budget: np.ndarray  # (N,) puntos totales que la clase tiene a ese nivel
    tree: np.ndarray         # (N,6) puntos del arbol por nodo (5 por nivel en total)
    tree_budget: np.ndarray  # (N,)
    tier: np.ndarray         # (N,11) 0 vacio, 1..3
    role: np.ndarray         # (N,11) 0 ATAQUE, 1 DEFENSA, 2 RECURSOS, 3 UTILIDAD
    pet: np.ndarray          # (N,4) poder 0..1 de cada mascota (0 = no equipada)
    buff: np.ndarray         # (N,15) rango 0..5 del buff recibido (0 = sin buff)
    rank: np.ndarray         # (N,) rango medio de las skills 1..5
    a: np.ndarray            # (N,3) latente de arquetipo: ofensa, defensa, utilidad (suma 1)
    label: np.ndarray        # (N,) 0 dano, 1 tanque, 2 mixto, 3 utilidad, -1 otro

    @property
    def n(self):
        return self.L.shape[0]

    def take(self, idx):
        return Builds(**{k: v[idx] for k, v in self.__dict__.items()})

    def copy(self):
        return Builds(**{k: v.copy() for k, v in self.__dict__.items()})

    # --- ayudas para escribir formulas ---------------------------------------------------------------------------------
    @property
    def primary(self):
        """Puntos del stat primario de la clase (N,)."""
        return self.stats[np.arange(self.n), CLS_PRIMARY[self.cls]]

    @property
    def secondary(self):
        return self.stats[np.arange(self.n), CLS_SECONDARY[self.cls]]

    @property
    def hp_tilt(self):
        return CLS_HP_TILT[self.cls]

    @property
    def dmg_tilt(self):
        return CLS_DMG_TILT[self.cls]

    @property
    def mag(self):
        """Parte del dano de la clase que es magico (0 fisico .. 1 magico)."""
        return CLS_MAG[self.cls]

    def tier_frac(self, slots=None, roles=None):
        """Suma de tier/3 en las ranuras dadas (lista de indices) con los roles dados (lista de 0..3); (N,)."""
        slots = list(range(N_SLOT)) if slots is None else list(slots)
        t = self.tier[:, slots] / 3.0
        if roles is not None:
            m = np.isin(self.role[:, slots], list(roles))
            t = t * m
        return t.sum(axis=1)

    def buff_on(self, i):
        return self.buff[:, i] > 0


def _dirichlet(rng, alpha):
    g = rng.gamma(np.maximum(alpha, 1e-3))
    return g / g.sum(axis=1, keepdims=True)


def sample_builds(n, L, rng, *, reference=False):
    """Genera n personajes de nivel L (int o array). reference=True: personaje neutro para medir elementos."""
    L = np.full(n, L, dtype=np.int64) if np.isscalar(L) else np.asarray(L, dtype=np.int64)
    cls = rng.integers(0, 7, n)
    if reference:
        a = np.tile(np.array([0.4, 0.4, 0.2]), (n, 1))
    else:
        a = _dirichlet(rng, np.tile(np.array([1.2, 1.2, 0.8]), (n, 1)))
        wild = rng.random(n) < 0.2
        if wild.any():
            a[wild] = _dirichlet(rng, np.full((int(wild.sum()), 3), 0.5))
    off, de, ut = a[:, 0], a[:, 1], a[:, 2]

    # --- stats ---
    budget = CLS_STAT_TOTAL[cls] * (L - 1) / (MAX_LEVEL - 1)
    prim = CLS_PRIMARY[cls]
    w = np.zeros((n, N_STAT))
    w[:, 0] = 0.3 + 2.5 * off * (prim == 0)
    w[:, 1] = 0.3 + 1.0 * off + 1.0 * ut + 2.5 * off * (prim == 1)
    w[:, 2] = 0.3 + 2.5 * off * (prim == 2)
    w[:, 3] = 0.3 + 3.0 * de
    w[:, 4] = 0.2 + 2.5 * off * (prim == 4) + 0.5 * ut
    shares = _dirichlet(rng, 4.0 * w + 0.2)
    spent = np.ones(n) if reference else rng.uniform(0.85, 1.0, n)
    if reference:
        spent = np.full(n, 0.7)
    stats = np.minimum(STAT_CAP, shares * (budget * spent)[:, None])

    # --- arbol ---
    tbudget = 5.0 * (L - 1)
    tw = np.stack([0.2 + 3 * off, 0.2 + 2 * de, 0.2 + 1.5 * off, 0.2 + 3 * de, 0.2 + 2 * de, 0.2 + 2 * ut], axis=1)
    tshares = _dirichlet(rng, 3.0 * tw + 0.2)
    tspent = np.full(n, 0.7) if reference else rng.uniform(0.85, 1.0, n)
    tree = tshares * (tbudget * tspent)[:, None]

    # --- items ---
    av = avail_tier(L)
    if reference:
        tier = np.tile(np.maximum(av - 1, 0)[:, None], (1, N_SLOT)).astype(np.int64)
    else:
        down = (rng.random((n, N_SLOT)) < 0.25).astype(np.int64)
        tier = np.maximum(av[:, None] - down, 0)
        # a nivel bajo algunas ranuras aun vacias
        empty = (rng.random((n, N_SLOT)) < np.clip(1.0 - L[:, None] / 14.0, 0, 0.7))
        tier = np.where(empty & (L[:, None] < 14), 0, tier)
    probs = np.stack([off, de, 0.5 * ut, 0.5 * ut], axis=1)
    probs = probs / probs.sum(axis=1, keepdims=True)
    cum = probs.cumsum(axis=1)
    r = rng.random((n, N_SLOT))
    role = (r[:, :, None] > cum[:, None, :]).sum(axis=2).clip(0, 3)

    # --- mascotas ---
    pw = np.stack([0.25 + off, 0.25 + de, 0.25 + 0.5 * de + 0.5 * ut, 0.25 + ut], axis=1)
    pw = pw / pw.sum(axis=1, keepdims=True)
    on = rng.random((n, N_PET)) < np.clip(0.9 * pw * 2.2, 0, 0.9)
    pet_power = rng.uniform(0.3, 1.0, (n, N_PET)) * np.clip(L[:, None] / 20.0, 0.2, 1.0)
    pet = np.where(on & (L[:, None] >= 4), pet_power, 0.0)
    if reference:
        pet = np.zeros((n, N_PET))

    # --- buffs ---
    rmax = np.maximum(1, np.floor(1 + 4 * np.minimum(1, (L - 1) / 20.0))).astype(np.int64)
    bon = rng.random((n, N_BUFF)) < 0.40
    brank = 1 + np.floor(rng.random((n, N_BUFF)) * rmax[:, None]).astype(np.int64)
    buff = np.where(bon & (L[:, None] >= 6), brank, 0)
    if reference:
        buff = np.zeros((n, N_BUFF), dtype=np.int64)

    rank = 1.0 + (np.maximum(1, 1 + 4 * np.minimum(1, (L - 1) / 20.0)) - 1.0) * rng.uniform(0.8, 1.0, n)
    label = np.full(n, -1)
    label[(off > 0.5)] = 0
    label[(de > 0.5)] = 1
    label[(np.maximum(np.maximum(off, de), ut) < 0.45) & (label < 0)] = 2
    label[(ut > 0.45) & (label < 0)] = 3
    return Builds(L=L, cls=cls, stats=stats, stat_budget=budget, tree=tree, tree_budget=tbudget, tier=tier, role=role,
                  pet=pet, buff=buff, rank=rank, a=a, label=label)


# ----------------------------------------------------------------------------------------------------------------------
# Elementos que se miden uno a uno (cobertura: «todo sirve y se nota»)
# ----------------------------------------------------------------------------------------------------------------------
ELEMENTS = (
    [("stat", i, STAT_NAMES[i]) for i in range(N_STAT)]
    + [("tree", j, TREE_NAMES[j]) for j in range(N_TREE)]
    + [("item", s, "item_" + SLOT_NAMES[s]) for s in range(N_SLOT)]
    + [("pet", k, PET_NAMES[k]) for k in range(N_PET)]
    + [("buff", k, "buff_" + BUFF_NAMES[k]) for k in range(N_BUFF)]
)


def apply_bump(b, element):
    """Devuelve una copia de b con UNA unidad estandar mas del elemento."""
    kind, i, _ = element
    c = b.copy()
    if kind == "stat":
        c.stats[:, i] = np.minimum(STAT_CAP, c.stats[:, i] + 0.10 * c.stat_budget)
    elif kind == "tree":
        c.tree[:, i] = c.tree[:, i] + 0.10 * c.tree_budget
    elif kind == "item":
        c.tier[:, i] = np.minimum(3, c.tier[:, i] + 1)
    elif kind == "pet":
        c.pet[:, i] = 0.6
    elif kind == "buff":
        c.buff[:, i] = 3
    return c


# ----------------------------------------------------------------------------------------------------------------------
# Formula (modulo) y duelo
# ----------------------------------------------------------------------------------------------------------------------
def load_formula(name):
    sys.path.insert(0, str(__import__("pathlib").Path(__file__).resolve().parent))
    return importlib.import_module(name)


def default_params(mod):
    return {k: v[0] for k, v in mod.PARAMS.items()}


def outputs(mod, b, p):
    a = mod.attack(b, p)
    d = mod.defense(b, p)
    return a, d


def off_power(a):
    return a["dps_phys"] + a["dps_mag"]


def ehp(b, d):
    """Vida efectiva contra un atacante medio 50/50 fisico/magico (con la vida, el escudo, la mitigacion y la reduccion de dano)."""
    mit = 0.5 * d["mit_phys"] + 0.5 * d["mit_mag"]
    return (d["hp"] + d.get("shield", 0.0)) / (np.maximum(1e-6, 1.0 - mit) * d["dmg_taken_mult"]) + 30.0 * d.get("regen", 0.0)


def duel(ao, do, bo, dob, rng, tmax=90.0, noise=0.15):
    """Duelo 1v1 esperado (A contra B). Devuelve (A gana en [0,1], ttk de A matando a B, ttk de B matando a A, clip_usado)."""
    clips = 0

    def eff(att, dfn):
        nonlocal clips
        mp = dfn["mit_phys"] - att.get("pen_phys", 0.0)
        mm = dfn["mit_mag"] - att.get("pen_mag", 0.0)
        clips += int(np.sum((mp < 0) | (mp > 0.95) | (mm < 0) | (mm > 0.95)))
        mp = np.clip(mp, 0.0, 0.95)
        mm = np.clip(mm, 0.0, 0.95)
        return (att["dps_phys"] * (1 - mp) + att["dps_mag"] * (1 - mm)) * dfn["dmg_taken_mult"]

    dps_ab = eff(ao, dob)
    dps_ba = eff(bo, do)
    heal_a = do_get(do, "regen") + do_get(ao, "lifesteal") * dps_ab
    heal_b = do_get(dob, "regen") + do_get(bo, "lifesteal") * dps_ba
    net_ab = dps_ab - heal_b          # lo que le baja a B por segundo
    net_ba = dps_ba - heal_a
    hp_a = do["hp"] + do.get("shield", 0.0)
    hp_b = dob["hp"] + dob.get("shield", 0.0)
    with np.errstate(divide="ignore", invalid="ignore"):
        ttk_ab = np.where(net_ab > 1e-9, hp_b / net_ab, np.inf)
        ttk_ba = np.where(net_ba > 1e-9, hp_a / net_ba, np.inf)
    ttk_ab = ttk_ab * np.exp(rng.normal(0, noise, ttk_ab.shape))
    ttk_ba = ttk_ba * np.exp(rng.normal(0, noise, ttk_ba.shape))
    a_wins = np.where(np.minimum(ttk_ab, ttk_ba) > tmax, 0.5, (ttk_ab < ttk_ba).astype(float))
    stall = np.minimum(ttk_ab, ttk_ba) > tmax
    return a_wins, ttk_ab, ttk_ba, clips, stall


def do_get(d, k):
    v = d.get(k, 0.0)
    return v


# ----------------------------------------------------------------------------------------------------------------------
# Puntuacion
# ----------------------------------------------------------------------------------------------------------------------
def bump_score(x, lo=0.015, hi=0.12, zero_lo=0.002, zero_hi=0.30):
    """1 si el cambio relativo esta entre lo y hi; baja linealmente a 0 en zero_lo / zero_hi."""
    x = np.asarray(x, dtype=float)
    s = np.where(x < lo, (x - zero_lo) / (lo - zero_lo), np.where(x > hi, 1.0 - (x - hi) / (zero_hi - hi), 1.0))
    return np.clip(s, 0.0, 1.0)


def closeness(x, lo, hi, slack):
    """1 dentro de [lo,hi]; baja a 0 a `slack` de distancia."""
    d = np.where(x < lo, lo - x, np.where(x > hi, x - hi, 0.0))
    return np.clip(1.0 - d / slack, 0.0, 1.0)


def decompose_shares(dec):
    """dec = {'mode': 'mult'|'add', 'factors': {nombre: array}} -> {nombre: participacion media 0..1 en el total}."""
    names = list(dec["factors"].keys())
    f = np.stack([np.asarray(dec["factors"][k], dtype=float) for k in names], axis=1)
    if dec["mode"] == "mult":
        x = np.log(np.maximum(f, 1e-9))
        x = np.maximum(x, 0.0)
    else:
        x = np.maximum(f, 0.0)
    tot = np.maximum(x.sum(axis=1, keepdims=True), 1e-12)
    sh = (x / tot).mean(axis=0)
    return dict(zip(names, sh.tolist()))


LEVELS = [1, 3, 5, 8, 10, 15, 20, 25, 30]
COVER_LEVELS = [8, 15, 22, 30]


def evaluate(mod, p, n=100_000, seed=1, levels=None, detail=True, quick=False):
    """Puntua una formula con parametros p. n = personajes por nivel. quick=True: version barata para ajustar parametros."""
    rng = np.random.default_rng(seed)
    levels = levels or LEVELS
    rep = {"n_por_nivel": n, "niveles": levels}
    all_bad = 0
    med_ttk, power_by_level = {}, {}
    arche_win = {0: [], 1: [], 2: [], 3: []}
    ehp_ratio, dps_ratio, tank_vs_glass, god_share, stalls, clips_tot, duels_tot = [], [], [], [], [], 0, 0
    extreme = {}

    dec_off = None
    R_OPP = 6
    for L in levels:
        A = sample_builds(n, L, rng)
        B = sample_builds(n, L, rng)
        ao, do = outputs(mod, A, p)
        bo, dob = outputs(mod, B, p)
        for d in (ao, do, bo, dob):
            for v in d.values():
                if not np.all(np.isfinite(v)):
                    all_bad += 1
        wr = np.zeros(n)
        tank_glass_w = []
        tmin_all = []
        for r_ in range(R_OPP):
            perm = rng.permutation(n)
            Bp = {k: v[perm] for k, v in bo.items()}
            Dp = {k: v[perm] for k, v in dob.items()}
            w, tab, tba, clips, stall = duel(ao, do, Bp, Dp, rng)
            clips_tot += clips
            duels_tot += 2 * n
            stalls.append(float(stall.mean()))
            wr += w / R_OPP
            tmin_all.append(np.minimum(tab, tba))
            lb = B.label[perm]
            m = (A.label == 1) & (lb == 0)
            if m.any():
                tank_glass_w.append(w[m])
            m2 = (A.label == 0) & (lb == 1)
            if m2.any():
                tank_glass_w.append(1 - w[m2])
        t = np.concatenate(tmin_all)
        t = t[np.isfinite(t)]
        med_ttk[L] = float(np.median(t)) if t.size else float("inf")
        for lab in arche_win:
            ma = (A.label == lab)
            if ma.any():
                arche_win[lab].append(float(wr[ma].mean()))
        op = off_power(ao)
        ep = ehp(A, do)
        power_by_level[L] = (float(np.median(op)), float(np.median(ep)))
        if L >= 8:
            tank = A.label == 1
            glass = A.label == 0
            if tank.sum() > 50 and glass.sum() > 50:
                ehp_ratio.append(float(np.median(ep[tank]) / np.median(ep[glass])))
                dps_ratio.append(float(np.median(op[glass]) / np.median(op[tank])))
                if tank_glass_w:
                    tank_vs_glass.append(float(np.concatenate(tank_glass_w).mean()))
            god_share.append(float((wr >= 0.95).mean()))
        if L == 30 or (30 not in levels and L == max(levels)):
            extreme = {
                "dps_p99_over_p50": float(np.percentile(op, 99.9) / np.median(op)),
                "ehp_p99_over_p50": float(np.percentile(ep, 99.9) / np.median(ep)),
                "dps_max_over_p50": float(op.max() / np.median(op)),
                "ehp_max_over_p50": float(ep.max() / np.median(ep)),
                "mit_max": float(max(do["mit_phys"].max(), do["mit_mag"].max())),
            }
            sel = np.where(A.label == 0)[0][:20000]
            if hasattr(mod, "decompose") and sel.size > 50:
                dec_off = mod.decompose(A.take(sel), p)

    # --- C1: cobertura de elementos ---
    cover = {}
    if not quick:
        ref_n = min(n, 30_000)
    else:
        ref_n = 8_000
    delta_by_el = {e[2]: [] for e in ELEMENTS}
    for L in COVER_LEVELS:
        R = sample_builds(ref_n, L, rng, reference=True)
        bo0, bd0 = outputs(mod, R, p)
        base_off, base_def = off_power(bo0), ehp(R, bd0)
        for e in ELEMENTS:
            Rb = apply_bump(R, e)
            bo1, bd1 = outputs(mod, Rb, p)
            ro = off_power(bo1) / np.maximum(base_off, 1e-9) - 1.0
            rd = ehp(Rb, bd1) / np.maximum(base_def, 1e-9) - 1.0
            po = [float(np.median(ro[R.cls == c])) for c in range(7)]
            pd = [float(np.median(rd[R.cls == c])) for c in range(7)]
            # por clase; el elemento "sirve" si alguna clase lo nota (max); min = clase para la que menos sirve
            delta_by_el[e[2]].append((max(po, key=abs), max(pd, key=abs), min(max(abs(x), abs(y)) for x, y in zip(po, pd))))
    el_delta, el_min = {}, {}
    for name, v in delta_by_el.items():
        v = np.array(v)
        el_delta[name] = (float(np.median(v[:, 0])), float(np.median(v[:, 1])))
        el_min[name] = float(np.median(v[:, 2]))
    c1_each = {k: float(bump_score(max(abs(o), abs(d)))) for k, (o, d) in el_delta.items()}
    C1 = float(np.mean(list(c1_each.values())))
    dead = [k for k, (o, d) in el_delta.items() if max(abs(o), abs(d)) < 0.005]

    # --- C2: identidad de arquetipos ---
    ew = {k: float(np.mean(v)) for k, v in arche_win.items() if v}
    C2a = float(np.mean([closeness(np.mean(ehp_ratio) if ehp_ratio else 1.0, 1.8, 3.5, 1.5)]))
    C2b = float(np.mean([closeness(np.mean(dps_ratio) if dps_ratio else 1.0, 1.8, 3.5, 1.5)]))
    C2c = float(np.mean([closeness(ew.get(k, 0.0), 0.43, 0.57, 0.12) for k in (0, 1, 2)]))
    C2d = float(closeness(np.mean(tank_vs_glass) if tank_vs_glass else 0.0, 0.40, 0.62, 0.2))
    C2 = (C2a + C2b + C2c + C2d) / 4.0

    # --- C3: sin dios ---
    gs = float(np.mean(god_share)) if god_share else 0.0
    C3 = float(closeness(gs, 0.0, 0.04, 0.15))

    # --- C4: tiempo de pelea ---
    c4 = []
    for L, t in med_ttk.items():
        lo, hi = (3.0, 30.0) if L < 5 else (5.0, 25.0)
        c4.append(float(closeness(t, lo, hi, 15.0)) if math.isfinite(t) else 0.0)
    C4 = float(np.mean(c4)) * (1.0 - min(1.0, 5.0 * float(np.mean(stalls))))

    # --- C5: suavidad por nivel ---
    lv = sorted(power_by_level)
    c5 = []
    for i in range(1, len(lv)):
        a0, a1 = power_by_level[lv[i - 1]], power_by_level[lv[i]]
        g = math.sqrt(max(a1[0] / max(a0[0], 1e-9), 1e-9) * max(a1[1] / max(a0[1], 1e-9), 1e-9))
        per_level = g ** (1.0 / max(1, lv[i] - lv[i - 1]))
        c5.append(float(closeness(per_level, 1.03, 1.15, 0.08)))
    C5 = float(np.mean(c5)) if c5 else 0.0

    # --- C6: sin topes ---
    c6 = []
    c6.append(float(closeness(extreme.get("dps_p99_over_p50", 99), 1.0, 3.0, 3.0)))
    c6.append(float(closeness(extreme.get("ehp_p99_over_p50", 99), 1.0, 3.0, 3.0)))
    c6.append(float(closeness(extreme.get("mit_max", 1.0), 0.0, 0.85, 0.15)))
    c6.append(float(closeness(clips_tot / max(duels_tot, 1), 0.0, 0.001, 0.05)))
    c6.append(0.0 if all_bad else 1.0)
    C6 = float(np.mean(c6))

    # --- C7: transparencia (participacion de cada factor del golpe) ---
    shares = {}
    c7 = 0.0
    if dec_off is not None:
        shares = decompose_shares(dec_off)
        vals = np.array(list(shares.values()))
        inband = float(np.mean((vals >= 0.01) & (vals <= 0.45)))
        c7 = inband * float(min(1.0, len(vals) / 10.0))
    C7 = c7

    # --- C8: paridad coste/efecto de los items (mejorar una ranura mas cara debe rendir mas) ---
    ratios = []
    for s in range(N_SLOT):
        o, d = el_delta["item_" + SLOT_NAMES[s]]
        cost = SLOT_COST_WEIGHT[s]
        ratios.append(max(abs(o), abs(d)) / cost)
    ratios = np.array(ratios)
    C8 = float(max(0.0, 1.0 - np.std(ratios) / max(np.mean(ratios), 1e-9)))

    total = 25 * C1 + 25 * C2 + 10 * C3 + 10 * C4 + 10 * C5 + 10 * C6 + 5 * C7 + 5 * C8
    rep.update({
        "score": round(float(total), 2),
        "C1_cobertura": round(C1, 3), "C2_arquetipos": round(C2, 3), "C3_sin_dios": round(C3, 3), "C4_ttk": round(C4, 3),
        "C5_niveles": round(C5, 3), "C6_sin_topes": round(C6, 3), "C7_transparencia": round(C7, 3), "C8_paridad_items": round(C8, 3),
    })
    if detail:
        rep["detalle"] = {
            "ttk_mediano_por_nivel": {str(k): round(v, 2) for k, v in med_ttk.items()},
            "winrate_arquetipo_vs_campo": {["dano", "tanque", "mixto", "utilidad"][k]: round(v, 3) for k, v in ew.items()},
            "ehp_tanque_sobre_dano": round(float(np.mean(ehp_ratio)), 2) if ehp_ratio else None,
            "dps_dano_sobre_tanque": round(float(np.mean(dps_ratio)), 2) if dps_ratio else None,
            "tanque_vs_dano_winrate": round(float(np.mean(tank_vs_glass)), 3) if tank_vs_glass else None,
            "partidas_tablas_pct": round(100 * float(np.mean(stalls)), 2),
            "invencibles_pct": round(100 * gs, 2),
            "extremos_nivel30": {k: round(v, 3) for k, v in extreme.items()},
            "clips_harness_pct": round(100 * clips_tot / max(duels_tot, 1), 4),
            "elementos_muertos(<0.5%)": dead,
            "minimo_entre_clases_%(la clase a la que menos le sirve)": {k: round(100 * v, 2) for k, v in el_min.items()},
            "delta_por_elemento_%(ofensa,defensa)": {k: (round(100 * o, 2), round(100 * d, 2)) for k, (o, d) in el_delta.items()},
            "participacion_en_el_golpe_nivel30_%": {k: round(100 * v, 2) for k, v in sorted(shares.items(), key=lambda kv: -kv[1])},
        }
    return rep


if __name__ == "__main__":
    mod = load_formula(sys.argv[1])
    print(json.dumps(evaluate(mod, default_params(mod), n=int(sys.argv[2]) if len(sys.argv) > 2 else 50_000), indent=1, ensure_ascii=False))
