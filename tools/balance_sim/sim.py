"""Simulador de formulas de balance del MOBA, v2 (herramienta de analisis; NO toca el juego).

1. TRAYECTORIAS: cada personaje se construye nivel por nivel (puntos de stats por nivel, puntos del arbol, rangos de skills, oro -> items por tier,
   mascotas por nivel, buffs de equipo). Un mismo personaje se mide en los niveles 1..30 tal como estaria en cada uno.
2. FORMULA: un modulo fNN_*.py convierte el personaje en ataque y defensa (ver INTERFACE.md).
3. DUELO por pasos de 0,5 s (hasta 90 s): rafaga inicial, sostenido, escudo y Egida, regeneracion, vida al golpear, anticurar, reflejo, control con tenacidad,
   penetracion, alcance (kiting). 4. MATRIZ de cruces clase x arquetipo en CADA nivel: la meta es 40-60 % (5-5, 6-4, 4-6).
"""
from __future__ import annotations

import importlib
import json
import math
import sys
from dataclasses import dataclass

import numpy as np

# ----------------------------------------------------------------------------------------------------------------------
# Catalogo
# ----------------------------------------------------------------------------------------------------------------------
CLASS_NAMES = ["BK", "RF", "DL", "MG", "FE", "GM", "SUM"]
STAT_NAMES = ["STR", "AGI", "ENE", "VIT", "CMD"]
TREE_NAMES = ["arbol_ofensa", "arbol_defensa", "arbol_critico", "arbol_durabilidad", "arbol_vida", "arbol_mana"]
SLOT_NAMES = ["arma", "escudo", "casco", "armadura", "pantalon", "guantes", "botas", "alas", "colgante", "anillo1", "anillo2"]
PET_NAMES = ["mascota_ataque", "mascota_guardia", "mascota_vital", "mascota_veloz"]
BUFF_NAMES = ["SoulBarrier", "Expansion", "Defense", "SwellLife", "Heal", "GreaterDefense", "GreaterDamage", "InfinityArrow",
              "IncCrit", "Reflection", "Berserker", "IgnoreDefense", "IncreaseHealth", "IncreaseBlock", "Recovery"]
ROLE_NAMES = ["ATAQUE", "DEFENSA", "RECURSOS", "UTILIDAD"]
ARCH_NAMES = ["dano", "tanque", "mixto"]
SLOT_COST_WEIGHT = np.array([1.4, 1.0, 0.8, 1.2, 1.0, 0.7, 0.7, 1.3, 0.6, 0.6, 0.6])
TIER_PRICE = np.array([0.0, 1.0, 2.5, 6.25])
UNIT_GOLD = 700.0
GOLD_AT_30 = 62000.0

CLS_PRIMARY = np.array([0, 0, 4, 2, 1, 2, 2])
CLS_SECONDARY = np.array([3, 1, 0, 0, 2, 3, 3])
CLS_HP_TILT = np.array([1.30, 1.30, 1.15, 1.00, 0.90, 0.90, 0.90])
CLS_DMG_TILT = np.array([1.02, 1.02, 0.95, 1.00, 1.08, 1.10, 1.10])
CLS_MAG = np.array([0.0, 0.0, 0.2, 0.5, 0.0, 1.0, 1.0])
CLS_STAT_TOTAL = np.array([100e3, 112e3, 130e3, 100e3, 100e3, 100e3, 112e3])
CLS_RANGE = np.array([2, 2, 4, 3, 6, 6, 6])
STAT_CAP = 30000.0
MAX_LEVEL = 30
N_STAT, N_TREE, N_SLOT, N_PET, N_BUFF = 5, 6, 11, 4, 15
TREE_NODE_CAP = 100.0
PET_UNLOCK = np.array([4, 8, 12, 16])

# Parametros del duelo (ver INFORME_SIMULADOR.md)
DT = 0.5
FIGHT_TMAX = 90.0
BURST_SECONDS = 6.0
NOISE_SIGMA = 0.12
RANGE_PENALTY = 0.05


def avail_tier(level):
    level = np.asarray(level)
    return np.where(level < 3, 0, np.where(level < 10, 1, np.where(level < 20, 2, 3)))


def ref_health(level):
    return 1900.0 + (50000.0 - 1900.0) * (np.asarray(level) - 1) / 29.0


def ref_mana(level):
    return 450.0 + (3400.0 - 450.0) * (np.asarray(level) - 1) / 29.0


def ref_damage_scale(level):
    return 1.0 + 14.0 * (np.asarray(level) - 1) / 29.0


def ref_flat_defense(level):
    return 15.0 + 385.0 * (np.asarray(level) - 1) / 29.0


# ----------------------------------------------------------------------------------------------------------------------
# Personajes en un nivel
# ----------------------------------------------------------------------------------------------------------------------
@dataclass
class Builds:
    L: np.ndarray            # (N,) nivel
    cls: np.ndarray          # (N,) 0 BK, 1 RF, 2 DL, 3 MG, 4 FE, 5 GM, 6 SUM
    stats: np.ndarray        # (N,5) STR, AGI, ENE, VIT, CMD (puntos invertidos, tope 30.000 cada uno)
    stat_budget: np.ndarray  # (N,) puntos de stats que la clase tiene en total a ese nivel
    tree: np.ndarray         # (N,6) puntos por nodo del arbol
    tree_budget: np.ndarray  # (N,) 5 por nivel
    tier: np.ndarray         # (N,11) 0 vacio, 1..3 (segun el oro acumulado)
    role: np.ndarray         # (N,11) 0 ATAQUE, 1 DEFENSA, 2 RECURSOS, 3 UTILIDAD
    pet: np.ndarray          # (N,4) poder 0..1 (0 = no tiene la mascota)
    buff: np.ndarray         # (N,15) rango 0..5 del buff recibido
    rank: np.ndarray         # (N,) rango medio de skills
    a: np.ndarray            # (N,3) intencion de arquetipo (ofensa, defensa, utilidad)
    label: np.ndarray        # (N,) 0 dano, 1 tanque, 2 mixto, -1 otro
    gold: np.ndarray         # (N,) oro acumulado hasta ese nivel

    @property
    def n(self):
        return self.L.shape[0]

    def take(self, idx):
        return Builds(**{k: v[idx] for k, v in self.__dict__.items()})

    def copy(self):
        return Builds(**{k: v.copy() for k, v in self.__dict__.items()})

    @property
    def primary(self):
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
        return CLS_MAG[self.cls]

    def tier_frac(self, slots=None, roles=None):
        slots = list(range(N_SLOT)) if slots is None else list(slots)
        t = self.tier[:, slots] / 3.0
        if roles is not None:
            t = t * np.isin(self.role[:, slots], list(roles))
        return t.sum(axis=1)

    def buff_on(self, i):
        return self.buff[:, i] > 0


def _dirichlet(rng, alpha):
    g = rng.gamma(np.maximum(alpha, 1e-3))
    return g / g.sum(axis=-1, keepdims=True)


# ----------------------------------------------------------------------------------------------------------------------
# Trayectorias: el personaje se construye nivel por nivel
# ----------------------------------------------------------------------------------------------------------------------
class Trajectory:
    """n personajes con su historia completa; at(k) devuelve el estado en levels[k]."""

    def __init__(self, n, levels, rng, reference=False):
        self.n, self.levels, self.reference = n, list(levels), reference
        NL = len(self.levels)
        Larr = np.array(self.levels)
        self.cls = rng.integers(0, 7, n)
        if reference:
            a = np.tile(np.array([0.4, 0.4, 0.2]), (n, 1))
        else:
            a = _dirichlet(rng, np.tile(np.array([1.2, 1.2, 0.8]), (n, 1)))
            wild = rng.random(n) < 0.2
            if wild.any():
                a[wild] = _dirichlet(rng, np.full((int(wild.sum()), 3), 0.5))
        self.a = a
        off, de, ut = a[:, 0], a[:, 1], a[:, 2]
        lab = np.full(n, -1)
        lab[off > 0.5] = 0
        lab[de > 0.5] = 1
        lab[(lab < 0) & (np.abs(off - de) < 0.15) & (ut < 0.35)] = 2
        self.label = lab
        ar = np.arange(n)

        # --- stats: puntos por nivel con politica de orden (equilibrado / tanque primero / dano primero / critico primero) ---
        prim = CLS_PRIMARY[self.cls]
        w = np.zeros((n, N_STAT))
        w[:, 0] = 0.3 + 2.5 * off * (prim == 0)
        w[:, 1] = 0.3 + 1.0 * off + 1.0 * ut + 2.5 * off * (prim == 1)
        w[:, 2] = 0.3 + 2.5 * off * (prim == 2)
        w[:, 3] = 0.3 + 3.0 * de
        w[:, 4] = 0.2 + 2.5 * off * (prim == 4) + 0.5 * ut
        w = w / w.sum(axis=1, keepdims=True)
        policy = np.zeros(n, dtype=np.int64) if reference else rng.integers(0, 4, n)
        w_early = w.copy()
        m1 = policy == 1
        w_early[m1, 3] *= 3.0
        m2 = policy == 2
        w_early[m2, prim[m2]] *= 3.0
        m3 = policy == 3
        w_early[m3, 1] *= 3.0
        w_early = w_early / w_early.sum(axis=1, keepdims=True)
        ppl = (CLS_STAT_TOTAL[self.cls] / (MAX_LEVEL - 1)) * (np.full(n, 0.7) if reference else rng.uniform(0.85, 1.0, n))
        lv = np.arange(2, MAX_LEVEL + 1)
        early = (lv <= 12)[None, :, None]
        wl = np.where(early, w_early[:, None, :], w[:, None, :])
        g = rng.standard_gamma((8.0 * wl + 0.05).astype(np.float32)).astype(np.float32)
        shares = g / g.sum(axis=2, keepdims=True)
        stats_cum = np.cumsum(shares * ppl[:, None, None].astype(np.float32), axis=1)
        self._stats = np.zeros((n, NL, N_STAT), dtype=np.float32)
        for k, L in enumerate(self.levels):
            if L >= 2:
                self._stats[:, k] = np.minimum(STAT_CAP, stats_cum[:, L - 2])
        del g, shares, stats_cum

        # --- arbol: 5 puntos por nivel; "profundo" llena nodos de a 100 en su orden de prioridad, "repartido" va proporcional ---
        tw = np.stack([0.2 + 3 * off, 0.2 + 2 * de, 0.2 + 1.5 * off, 0.2 + 3 * de, 0.2 + 2 * de, 0.2 + 2 * ut], axis=1)
        tspent = np.full(n, 0.7) if reference else rng.uniform(0.85, 1.0, n)
        deep = (np.zeros(n, bool) if reference else rng.random(n) < 0.5)
        key = rng.random((n, N_TREE)) ** (1.0 / tw)
        order = np.argsort(-key, axis=1)
        position = np.empty_like(order)
        position[ar[:, None], order] = np.arange(N_TREE)[None, :]
        tshare = tw / tw.sum(axis=1, keepdims=True)
        self._tree = np.zeros((n, NL, N_TREE), dtype=np.float32)
        for k, L in enumerate(self.levels):
            T = 5.0 * (L - 1) * tspent
            deep_pts = np.clip(T[:, None] - TREE_NODE_CAP * position, 0, TREE_NODE_CAP)
            spread_pts = T[:, None] * tshare
            self._tree[:, k] = np.where(deep[:, None], deep_pts, spread_pts)

        # --- items: eventos de mejora (ranura, tier) comprados por orden de prioridad segun el oro acumulado ---
        probs = np.stack([off, de, 0.5 * ut, 0.5 * ut], axis=1)
        probs = probs / probs.sum(axis=1, keepdims=True)
        cum = probs.cumsum(axis=1)
        r = rng.random((n, N_SLOT))
        self.role = (r[:, :, None] > cum[:, None, :]).sum(axis=2).clip(0, 3).astype(np.int8)
        atk_bias = np.array([3, 1, .5, 1.5, 1.5, .8, .8, 1, .5, .8, .8])
        def_bias = np.array([1, 2.5, 2, 3, 3, 1.5, 1.5, 1.5, .8, 1, 1])
        slot_w = 0.5 + off[:, None] * atk_bias[None, :] + de[:, None] * def_bias[None, :]
        skey = -np.log(rng.random((n, N_SLOT))) / slot_w
        slot_rank = np.argsort(np.argsort(skey, axis=1), axis=1).astype(np.float32)
        K = rng.uniform(2.0, 11.0, n)[:, None, None]
        step = np.arange(1, 4)[None, :, None]
        ekey = step * K + slot_rank[:, None, :] + rng.random((n, 3, N_SLOT)) * 0.4
        ekey = ekey.reshape(n, 3 * N_SLOT)
        eslot = np.tile(np.arange(N_SLOT), 3)[None, :].repeat(n, axis=0)
        etier = np.repeat(np.arange(1, 4), N_SLOT)[None, :].repeat(n, axis=0)
        o = np.argsort(ekey, axis=1)
        eslot, etier = np.take_along_axis(eslot, o, 1), np.take_along_axis(etier, o, 1)
        ecost = UNIT_GOLD * SLOT_COST_WEIGHT[eslot] * (TIER_PRICE[etier] - 0.7 * TIER_PRICE[etier - 1])
        ecum = np.cumsum(ecost, axis=1)
        gm = np.full(n, 0.6) if reference else np.exp(rng.normal(0, 0.15, n))
        self._gold = (GOLD_AT_30 * gm)[:, None] * (np.clip((Larr - 1) / 29.0, 0, 1) ** 1.5)[None, :]
        self._tier = np.zeros((n, NL, N_SLOT), dtype=np.int8)
        for e in range(3 * N_SLOT):
            bought = (ecum[:, e][:, None] <= self._gold).astype(np.int8)
            self._tier[ar, :, eslot[:, e]] += bought

        # --- mascotas: se desbloquean por nivel y suben de nivel con el uso ---
        pw = np.stack([0.25 + off, 0.25 + de, 0.25 + 0.5 * de + 0.5 * ut, 0.25 + ut], axis=1)
        pw = pw / pw.sum(axis=1, keepdims=True)
        has = rng.random((n, N_PET)) < np.clip(1.9 * pw, 0, 0.9)
        if reference:
            has[:] = False
        grow = np.clip((Larr[None, :, None] - PET_UNLOCK[None, None, :] + 1) / (MAX_LEVEL - PET_UNLOCK[None, None, :] + 1), 0, 1)
        self._pet = (has[:, None, :] * grow * rng.uniform(0.6, 1.0, (n, 1, N_PET))).astype(np.float32)

        # --- rango de skills y buffs de equipo (cambian de pelea en pelea) ---
        self._rank = (1.0 + 4.0 * np.clip((Larr - 1) / 16.0, 0, 1))[None, :] * rng.uniform(0.7, 1.0, (n, 1))
        self._rank = np.maximum(1.0, self._rank)
        self.buff_rate = np.zeros(n) if reference else rng.uniform(0.1, 0.7, n)
        self._buff_noise = rng.random((n, NL, N_BUFF)).astype(np.float32)
        self._buff_rank = rng.random((n, NL, N_BUFF)).astype(np.float32)

    def at(self, k):
        L = self.levels[k]
        n = self.n
        rmax = max(1, int(math.floor(1 + 4 * min(1.0, (L - 1) / 20.0))))
        on = (self._buff_noise[:, k] < self.buff_rate[:, None]) & (L >= 6)
        brank = 1 + np.floor(self._buff_rank[:, k] * rmax).astype(np.int64)
        return Builds(
            L=np.full(n, L, dtype=np.int64), cls=self.cls, stats=self._stats[:, k].astype(np.float64),
            stat_budget=CLS_STAT_TOTAL[self.cls] * (L - 1) / (MAX_LEVEL - 1), tree=self._tree[:, k].astype(np.float64),
            tree_budget=np.full(n, 5.0 * (L - 1)), tier=self._tier[:, k].astype(np.int64), role=self.role.astype(np.int64),
            pet=self._pet[:, k].astype(np.float64), buff=np.where(on, brank, 0).astype(np.int64), rank=self._rank[:, k].astype(np.float64),
            a=self.a, label=self.label, gold=self._gold[:, k],
        )


# ----------------------------------------------------------------------------------------------------------------------
# Elementos medidos uno a uno
# ----------------------------------------------------------------------------------------------------------------------
ELEMENTS = (
    [("stat", i, STAT_NAMES[i]) for i in range(N_STAT)]
    + [("tree", j, TREE_NAMES[j]) for j in range(N_TREE)]
    + [("item", s, "item_" + SLOT_NAMES[s]) for s in range(N_SLOT)]
    + [("pet", k, PET_NAMES[k]) for k in range(N_PET)]
    + [("buff", k, "buff_" + BUFF_NAMES[k]) for k in range(N_BUFF)]
)


def apply_bump(b, element):
    """Copia de b con UNA unidad estandar mas del elemento (+10 % del presupuesto de stats/arbol, +1 tier, mascota al 60 %, buff a rango 3)."""
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
# Formula y duelo
# ----------------------------------------------------------------------------------------------------------------------
def load_formula(name):
    sys.path.insert(0, str(__import__("pathlib").Path(__file__).resolve().parent))
    return importlib.import_module(name)


def default_params(mod):
    return {k: v[0] for k, v in mod.PARAMS.items()}


def _arr(d, n):
    out = {}
    for k, v in d.items():
        v = np.asarray(v, dtype=float)
        out[k] = np.full(n, float(v)) if v.ndim == 0 else v
    return out


def outputs(mod, b, p):
    return _arr(mod.attack(b, p), b.n), _arr(mod.defense(b, p), b.n)


def off_power(a):
    return a["dps_phys"] + a["dps_mag"]


def ehp(b, d):
    mit = 0.5 * d["mit_phys"] + 0.5 * d["mit_mag"]
    return (d["hp"] + d.get("shield", 0.0)) / (np.maximum(1e-6, 1.0 - mit) * d["dmg_taken_mult"]) + 30.0 * d.get("regen", 0.0)


def _g(d, k, default):
    return d[k] if k in d else default


def fight(ao, do, bo, dob, cls_a, cls_b, rng):
    """Duelo por pasos (A contra B). Devuelve a_wins (0..1), ttk (s del primer muerto; inf si nadie), stall (nadie murio en 90 s), clips."""
    M = do["hp"].shape[0]
    clips = 0

    def eff_dps(att, dfn, att_cls, dfn_cls):
        nonlocal clips
        mp = dfn["mit_phys"] - _g(att, "pen_phys", 0.0)
        mm = dfn["mit_mag"] - _g(att, "pen_mag", 0.0)
        clips += int(np.sum((mp < -1e-9) | (mp > 0.95) | (mm < -1e-9) | (mm > 0.95)))
        mp, mm = np.clip(mp, 0.0, 0.95), np.clip(mm, 0.0, 0.95)
        up = 1.0 - RANGE_PENALTY * np.clip(CLS_RANGE[dfn_cls] - CLS_RANGE[att_cls], 0, 4)
        return (att["dps_phys"] * (1 - mp) + att["dps_mag"] * (1 - mm)) * dfn["dmg_taken_mult"] * up

    base_a = eff_dps(ao, dob, cls_a, cls_b)
    base_b = eff_dps(bo, do, cls_b, cls_a)
    cc_on_a = _g(bo, "cc_uptime", 0.0) * (1.0 - _g(do, "tenacity", 0.0))
    cc_on_b = _g(ao, "cc_uptime", 0.0) * (1.0 - _g(dob, "tenacity", 0.0))
    base_a = base_a * (1.0 - np.clip(cc_on_a, 0, 0.95))
    base_b = base_b * (1.0 - np.clip(cc_on_b, 0, 0.95))
    na = np.exp(rng.normal(0, NOISE_SIGMA, M))
    nb = np.exp(rng.normal(0, NOISE_SIGMA, M))
    burst_a, sus_a = _g(ao, "burst_mult", 1.0), _g(ao, "sustain", 1.0)
    burst_b, sus_b = _g(bo, "burst_mult", 1.0), _g(bo, "sustain", 1.0)
    hp_a, hp_b = do["hp"].copy(), dob["hp"].copy()
    max_a, max_b = hp_a.copy(), hp_b.copy()
    sh_a, sh_b = _g(do, "shield", 0.0) * np.ones(M), _g(dob, "shield", 0.0) * np.ones(M)
    aeg_a, aeg_b = _g(do, "aegis", 0.0), _g(dob, "aegis", 0.0)
    cd_a, cd_b = np.maximum(_g(do, "aegis_cd", 60.0), 1.0), np.maximum(_g(dob, "aegis_cd", 60.0), 1.0)
    regen_a, regen_b = _g(do, "regen", 0.0), _g(dob, "regen", 0.0)
    ls_a, ls_b = _g(ao, "lifesteal", 0.0), _g(bo, "lifesteal", 0.0)
    ah_a, ah_b = _g(ao, "antiheal", 0.0), _g(bo, "antiheal", 0.0)
    rf_a, rf_b = _g(do, "reflect", 0.0), _g(dob, "reflect", 0.0)
    dead_a = np.full(M, np.inf)
    dead_b = np.full(M, np.inf)
    sh_a = sh_a + aeg_a
    sh_b = sh_b + aeg_b
    t = 0.0
    for _ in range(int(FIGHT_TMAX / DT)):
        f_a = np.where(t < BURST_SECONDS, burst_a, sus_a)
        f_b = np.where(t < BURST_SECONDS, burst_b, sus_b)
        alive = (dead_a == np.inf) & (dead_b == np.inf)
        da = base_a * f_a * na * DT * alive            # dano de A a B en este paso
        db = base_b * f_b * nb * DT * alive
        ab = np.minimum(sh_b, da)
        sh_b = sh_b - ab
        hp_b = hp_b - (da - ab)
        aa = np.minimum(sh_a, db)
        sh_a = sh_a - aa
        hp_a = hp_a - (db - aa)
        hp_a = hp_a - rf_b * da                         # reflejo
        hp_b = hp_b - rf_a * db
        hp_a = np.minimum(max_a, hp_a + (regen_a * DT + ls_a * da) * (1 - np.clip(ah_b, 0, 0.95)) * alive)
        hp_b = np.minimum(max_b, hp_b + (regen_b * DT + ls_b * db) * (1 - np.clip(ah_a, 0, 0.95)) * alive)
        sh_a = sh_a + aeg_a * (np.floor((t + DT) / cd_a) - np.floor(t / cd_a))
        sh_b = sh_b + aeg_b * (np.floor((t + DT) / cd_b) - np.floor(t / cd_b))
        t += DT
        dead_a = np.where((hp_a <= 0) & (dead_a == np.inf), t, dead_a)
        dead_b = np.where((hp_b <= 0) & (dead_b == np.inf), t, dead_b)
        if np.all((dead_a < np.inf) | (dead_b < np.inf)):
            break
    stall = (dead_a == np.inf) & (dead_b == np.inf)
    ra, rb = hp_a / np.maximum(max_a, 1), hp_b / np.maximum(max_b, 1)
    a_wins = np.where(dead_b < dead_a, 1.0, np.where(dead_a < dead_b, 0.0, np.where(ra >= rb, 1.0, 0.0)))
    a_wins = np.where(stall, np.where(ra > rb, 1.0, np.where(ra < rb, 0.0, 0.5)), a_wins)
    return {"a_wins": a_wins, "ttk": np.minimum(dead_a, dead_b), "stall": stall, "clips": clips, "n": 2 * M}


# ----------------------------------------------------------------------------------------------------------------------
# Puntuacion
# ----------------------------------------------------------------------------------------------------------------------
def bump_score(x, lo=0.015, hi=0.12, zero_lo=0.002, zero_hi=0.30):
    x = np.asarray(x, dtype=float)
    s = np.where(x < lo, (x - zero_lo) / (lo - zero_lo), np.where(x > hi, 1.0 - (x - hi) / (zero_hi - hi), 1.0))
    return np.clip(s, 0.0, 1.0)


def closeness(x, lo, hi, slack):
    x = np.asarray(x, dtype=float)
    d = np.where(x < lo, lo - x, np.where(x > hi, x - hi, 0.0))
    return np.clip(1.0 - d / slack, 0.0, 1.0)


def decompose_shares(dec):
    names = list(dec["factors"].keys())
    f = np.stack([np.asarray(dec["factors"][k], dtype=float) for k in names], axis=1)
    x = np.maximum(np.log(np.maximum(f, 1e-9)), 0.0) if dec["mode"] == "mult" else np.maximum(f, 0.0)
    tot = np.maximum(x.sum(axis=1, keepdims=True), 1e-12)
    return dict(zip(names, (x / tot).mean(axis=0).tolist()))


LEVELS = [1, 2, 3, 4, 5, 6, 8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 30]
QUICK_LEVELS = [4, 8, 12, 16, 22, 30]
COVER_LEVELS = [8, 16, 24, 30]
N_CELLS = 7 * 3


def cell_of(cls, label):
    return np.where(label >= 0, cls * 3 + np.minimum(label, 2), -1)


def evaluate(mod, p, n=60_000, seed=1, levels=None, detail=True, quick=False, m_pair=None):
    """Puntua una formula. n = personajes (trayectorias) simulados; m_pair = duelos por pareja de celdas y nivel."""
    rng = np.random.default_rng(seed)
    levels = levels or (QUICK_LEVELS if quick else LEVELS)
    m_pair = m_pair or (40 if quick else 400)
    traj = Trajectory(n, levels, rng)
    cells_all = cell_of(traj.cls, traj.label)
    members = [np.where(cells_all == c)[0] for c in range(N_CELLS)]
    rep = {"n_trayectorias": n, "niveles": levels, "duelos_por_pareja_de_celdas": m_pair}
    all_bad, clips_tot, duels_tot, stall_tot = 0, 0, 0, 0.0
    band_frac, extreme_frac, ttk_med, power_lv = {}, {}, {}, {}
    cell_mean = np.zeros((len(levels), N_CELLS))
    matrices = {}
    ehp_ratio, dps_ratio, tank_glass = [], [], []
    extreme, dec_off = {}, None

    for k, L in enumerate(levels):
        A = traj.at(k)
        ao, do = outputs(mod, A, p)
        for d in (ao, do):
            for v in d.values():
                if not np.all(np.isfinite(v)):
                    all_bad += 1
        op, ep = off_power(ao), ehp(A, do)
        power_lv[L] = (float(np.median(op)), float(np.median(ep)))
        IA, IB, CI, CJ = [], [], [], []
        for i in range(N_CELLS):
            for j in range(N_CELLS):
                if len(members[i]) < 15 or len(members[j]) < 15:
                    continue
                IA.append(rng.choice(members[i], m_pair))
                IB.append(rng.choice(members[j], m_pair))
                CI.append(np.full(m_pair, i))
                CJ.append(np.full(m_pair, j))
        IA, IB, CI, CJ = map(np.concatenate, (IA, IB, CI, CJ))
        sa = {kk: v[IA] for kk, v in ao.items()}
        sd = {kk: v[IA] for kk, v in do.items()}
        sb = {kk: v[IB] for kk, v in ao.items()}
        sdb = {kk: v[IB] for kk, v in do.items()}
        res = fight(sa, sd, sb, sdb, A.cls[IA], A.cls[IB], rng)
        clips_tot += res["clips"]
        duels_tot += res["n"]
        stall_tot += float(res["stall"].mean())
        W = np.full(N_CELLS * N_CELLS, np.nan)
        T = np.full(N_CELLS * N_CELLS, np.nan)
        idx = CI * N_CELLS + CJ
        cnt = np.bincount(idx, minlength=N_CELLS * N_CELLS)
        ok = cnt > 0
        W[ok] = np.bincount(idx, weights=res["a_wins"], minlength=N_CELLS * N_CELLS)[ok] / cnt[ok]
        tt = np.where(np.isfinite(res["ttk"]), res["ttk"], FIGHT_TMAX)
        T[ok] = np.bincount(idx, weights=tt, minlength=N_CELLS * N_CELLS)[ok] / cnt[ok]
        W, T = W.reshape(N_CELLS, N_CELLS), T.reshape(N_CELLS, N_CELLS)
        off_mask = ~np.eye(N_CELLS, dtype=bool) & ~np.isnan(W)
        vals = W[off_mask]
        band_frac[L] = float(np.mean((vals >= 0.40) & (vals <= 0.60)))
        extreme_frac[L] = float(np.mean((vals < 0.25) | (vals > 0.75)))
        with np.errstate(all="ignore"):
            cell_mean[k] = np.nanmean(W, axis=1)
        ttk_med[L] = float(np.nanmedian(T))
        matrices[L] = W
        if L >= 8:
            tank, glass = A.label == 1, A.label == 0
            if tank.sum() > 50 and glass.sum() > 50:
                ehp_ratio.append(float(np.median(ep[tank]) / np.median(ep[glass])))
                dps_ratio.append(float(np.median(op[glass]) / np.median(op[tank])))
            tg = [W[c * 3 + 1, c * 3] for c in range(7) if not np.isnan(W[c * 3 + 1, c * 3])]
            if tg:
                tank_glass.append(float(np.mean(tg)))
        if L == max(levels):
            extreme = {
                "dps_p99.9_sobre_mediana": float(np.percentile(op, 99.9) / np.median(op)),
                "ehp_p99.9_sobre_mediana": float(np.percentile(ep, 99.9) / np.median(ep)),
                "mit_max": float(max(do["mit_phys"].max(), do["mit_mag"].max())),
            }
            sel = np.where(A.label == 0)[0][:20000]
            if hasattr(mod, "decompose") and sel.size > 50:
                dec_off = mod.decompose(A.take(sel), p)

    # --- C1: cobertura de elementos (personaje de referencia, nivel por nivel) ---
    ref_n = 2500 if quick else 20_000
    Rt = Trajectory(ref_n, COVER_LEVELS, rng, reference=True)
    dd = {e[2]: [] for e in ELEMENTS}
    for k, L in enumerate(COVER_LEVELS):
        R = Rt.at(k)
        bo0, bd0 = outputs(mod, R, p)
        b_off, b_def = off_power(bo0), ehp(R, bd0)
        for e in ELEMENTS:
            Rb = apply_bump(R, e)
            bo1, bd1 = outputs(mod, Rb, p)
            ro = off_power(bo1) / np.maximum(b_off, 1e-9) - 1.0
            rd = ehp(Rb, bd1) / np.maximum(b_def, 1e-9) - 1.0
            po = [float(np.median(ro[R.cls == c])) for c in range(7)]
            pd = [float(np.median(rd[R.cls == c])) for c in range(7)]
            dd[e[2]].append((max(po, key=abs), max(pd, key=abs), min(max(abs(x), abs(y)) for x, y in zip(po, pd))))
    el_delta, el_min = {}, {}
    for name, v in dd.items():
        v = np.array(v)
        el_delta[name] = (float(np.median(v[:, 0])), float(np.median(v[:, 1])))
        el_min[name] = float(np.median(v[:, 2]))
    c1_each = {k_: float(bump_score(max(abs(o), abs(d)))) for k_, (o, d) in el_delta.items()}
    C1 = float(np.mean(list(c1_each.values())))
    dead = [k_ for k_, (o, d) in el_delta.items() if max(abs(o), abs(d)) < 0.005]

    # --- C2: duelos parejos en cada nivel (40-60 %) ---
    lv_scored = [L for L in levels if L >= 3]
    c2_lv = {L: max(0.0, band_frac[L] - 0.5 * extreme_frac[L]) for L in lv_scored}
    C2 = float(np.mean(list(c2_lv.values()))) if c2_lv else 0.0

    # --- C3: identidad de arquetipos ---
    C3 = float(np.mean([
        closeness(np.mean(ehp_ratio) if ehp_ratio else 1.0, 1.8, 3.5, 1.5),
        closeness(np.mean(dps_ratio) if dps_ratio else 1.0, 1.8, 3.5, 1.5),
        closeness(np.mean(tank_glass) if tank_glass else 0.0, 0.40, 0.60, 0.2),
    ]))

    # --- C4: ninguna celda manda ---
    cm = cell_mean[[levels.index(L) for L in lv_scored]]
    with np.errstate(all="ignore"):
        C4 = float(np.nanmean(closeness(cm, 0.42, 0.58, 0.12)))

    # --- C5: duracion de las peleas ---
    c5 = []
    for L, t in ttk_med.items():
        lo, hi = (3.0, 30.0) if L < 5 else (5.0, 25.0)
        c5.append(float(closeness(t, lo, hi, 15.0)) if math.isfinite(t) else 0.0)
    C5 = float(np.mean(c5)) * (1.0 - min(1.0, 5.0 * stall_tot / len(levels)))

    # --- C6: crecimiento suave entre niveles ---
    lv = sorted(power_lv)
    c6 = []
    for i in range(1, len(lv)):
        a0, a1 = power_lv[lv[i - 1]], power_lv[lv[i]]
        gr = math.sqrt(max(a1[0] / max(a0[0], 1e-9), 1e-9) * max(a1[1] / max(a0[1], 1e-9), 1e-9))
        c6.append(float(closeness(gr ** (1.0 / max(1, lv[i] - lv[i - 1])), 1.03, 1.15, 0.08)))
    C6 = float(np.mean(c6)) if c6 else 0.0

    # --- C7: sin topes ---
    C7 = float(np.mean([
        closeness(extreme.get("dps_p99.9_sobre_mediana", 99), 1.0, 3.0, 3.0),
        closeness(extreme.get("ehp_p99.9_sobre_mediana", 99), 1.0, 3.0, 3.0),
        closeness(extreme.get("mit_max", 1.0), 0.0, 0.85, 0.15),
        closeness(clips_tot / max(duels_tot, 1), 0.0, 0.001, 0.05),
        0.0 if all_bad else 1.0,
    ]))

    # --- C8: transparencia del golpe ---
    shares = {}
    C8 = 0.0
    if dec_off is not None:
        shares = decompose_shares(dec_off)
        vals = np.array(list(shares.values()))
        C8 = float(np.mean((vals >= 0.01) & (vals <= 0.45))) * min(1.0, len(vals) / 10.0)

    # --- C9: paridad coste / efecto de los items ---
    ratios = np.array([max(abs(el_delta["item_" + SLOT_NAMES[s]][0]), abs(el_delta["item_" + SLOT_NAMES[s]][1])) / SLOT_COST_WEIGHT[s] for s in range(N_SLOT)])
    C9 = float(max(0.0, 1.0 - np.std(ratios) / max(np.mean(ratios), 1e-9)))

    total = 20 * C1 + 30 * C2 + 15 * C3 + 5 * C4 + 5 * C5 + 5 * C6 + 10 * C7 + 5 * C8 + 5 * C9
    rep.update({"score": round(float(total), 2), "C1_cobertura": round(C1, 3), "C2_duelos_40_60": round(C2, 3), "C3_arquetipos": round(C3, 3),
                "C4_ninguna_celda_manda": round(C4, 3), "C5_duracion": round(C5, 3), "C6_niveles": round(C6, 3), "C7_sin_topes": round(C7, 3),
                "C8_transparencia": round(C8, 3), "C9_paridad_items": round(C9, 3)})
    if detail:
        cls_vs_cls = None
        Wl = matrices.get(16) if 16 in matrices else matrices.get(max(matrices))
        if Wl is not None:
            with np.errstate(all="ignore"):
                M7 = np.nanmean(Wl.reshape(7, 3, 7, 3), axis=(1, 3))
            cls_vs_cls = {CLASS_NAMES[i]: {CLASS_NAMES[j]: round(float(M7[i, j]), 2) for j in range(7)} for i in range(7)}
        rep["detalle"] = {
            "porcentaje_de_cruces_en_40_60_por_nivel": {str(L): round(100 * band_frac[L], 1) for L in levels},
            "porcentaje_de_cruces_extremos_(<25_o_>75)_por_nivel": {str(L): round(100 * extreme_frac[L], 1) for L in levels},
            "ttk_mediano_s_por_nivel": {str(L): round(v, 1) for L, v in ttk_med.items()},
            "winrate_clase_vs_clase_nivel16": cls_vs_cls,
            "ehp_tanque_sobre_dano": round(float(np.mean(ehp_ratio)), 2) if ehp_ratio else None,
            "dps_dano_sobre_tanque": round(float(np.mean(dps_ratio)), 2) if dps_ratio else None,
            "tanque_vs_dano_winrate": round(float(np.mean(tank_glass)), 3) if tank_glass else None,
            "partidas_sin_muerto_en_90s_%": round(100 * stall_tot / len(levels), 2),
            "extremos_ultimo_nivel": {k_: round(v, 3) for k_, v in extreme.items()},
            "recortes_del_simulador_%": round(100 * clips_tot / max(duels_tot, 1), 4),
            "duelos_simulados": duels_tot,
            "elementos_muertos(<0.5%)": dead,
            "delta_por_elemento_%(ofensa,defensa)": {k_: (round(100 * o, 2), round(100 * d, 2)) for k_, (o, d) in el_delta.items()},
            "minimo_entre_clases_%": {k_: round(100 * v, 2) for k_, v in el_min.items()},
            "participacion_en_el_golpe_nivel_max_%": {k_: round(100 * v, 2) for k_, v in sorted(shares.items(), key=lambda kv: -kv[1])},
        }
    return rep


if __name__ == "__main__":
    mod = load_formula(sys.argv[1])
    print(json.dumps(evaluate(mod, default_params(mod), n=int(sys.argv[2]) if len(sys.argv) > 2 else 30_000), indent=1, ensure_ascii=False))
