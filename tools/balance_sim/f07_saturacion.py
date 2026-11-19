"""F07 - SATURACION.

Cada fuente de poder (cada stat, nodo del arbol, ranura de item, mascota y buff) entrega su efecto con una curva de saturacion propia
    e = techo_e x peso_canal x u(x),   u(x) = x (1+k)/(x+k)   (x = entrada normalizada a 0..1; u(1) = 1; k chico = sube rapido y se aplana, k grande = casi lineal)
y los efectos que van al MISMO canal (potencia, critico, vida, mitigacion, reduccion de dano, sosten, enfriamiento, mana) se combinan por COMPLEMENTO:
    C_canal = 1 - prod_i (1 - e_i)  ( = 1 - exp(-suma de riesgos) ),
asi que apilar todo sirve pero cada pieza extra rinde menos, y ningun canal pasa de 1 sin necesitar un tope duro. Las salidas de combate son funciones
simples de los canales (1 + A C, m0 + (M-m0) C, 1 - R C...). No hay min/max/clip dentro de la formula.
"""
import numpy as np
import sim
import kits

FORMULA_TEXT = (
    "ENTRADAS: s_i = stat_i/30000, t_j = nodo_j/100, tau_s = tier_s/3, pet_k (0..1), buff_m = rango/5.\n"
    "SATURACION POR FUENTE: u(x;k) = x(1+k)/(x+k); efecto de la fuente e = techo_fuente x reparto_canal x u(x;k_grupo) (cada fuente tiene su techo, cada grupo/stat su k).\n"
    "  Items: reparto_canal = g x generico + (1-g) x reparto_del_rol(ATAQUE, DEFENSA, RECURSOS, UTILIDAD); arma suma potencia extra y antisanacion; escudo suma mitigacion y Egida.\n"
    "CANALES (complemento): C_c = 1 - prod(1 - e_i) sobre todas las fuentes del canal c:\n"
    "  POT (potencia: stat principal x1, secundaria x afin_sec, resto x afin_otro; arbol ofensa; arma; piezas de ataque; mascota ataque; Expansion, GreaterDamage, InfinityArrow, Berserker),\n"
    "  CRI (AGI, arbol critico, piezas de ataque, IncCrit, mascota ataque), PEN (IgnoreDefense + critico), VID (VIT, arbol vida, piezas de defensa, mascota vital, SwellLife, IncreaseHealth, Berserker, SoulBarrier),\n"
    "  MIT (VIT, arbol defensa, piezas de defensa, escudo, SoulBarrier, Defense), RED (arbol durabilidad, piezas, mascota guardia, Defense, GreaterDefense, IncreaseBlock, Reflection),\n"
    "  SOS (piezas de recursos, mascota vital, Heal, Recovery), CDR (piezas de utilidad, mascota veloz), MAN (arbol mana, mascota veloz).\n"
    "OFENSA: dps = exp(gd(L-1)) x dmg_clase x tilt x kits.rotation(frac=C_POT, cdr=cdrMax C_CDR, mana=mbMax C_MAN)[rotacion x sostenido] x (1 + Ap C_POT) x (1 + cmax C_CRI (cm + IncCrit - 1));  pen = 0,98 m0 C_PEN.\n"
    "DEFENSA: hp = H1 exp(gh(L-1)) x tilt x hp_clase x (1 + Ah C_VID);  mit_fis = m0 + (Mmax-m0) C_MIT;  mit_mag = m0 + (Mmax-m0) mu C_MIT;  dano_recibido = 1 - Rmax C_RED;\n"
    "  regen = hp rr C_SOS;  vida al golpear = ls C_SOS;  escudo = hp (sh0 + sb C_MIT_SB);  Egida = hp aeg0 u(escudo);  reflejo = refl e_Reflection."
)

CLS = sim.CLASS_NAMES
CH = ["POT", "CRI", "VID", "MIT", "RED", "SOS", "CDR", "MAN", "PEN"]
NCH = len(CH)
CI = {c: i for i, c in enumerate(CH)}
GROUPS = ["stat_principal", "stats_otros", "arbol", "arma", "piezas", "mascotas", "buffs"]
GI = {g: i for i, g in enumerate(GROUPS)}


def _w(**kw):
    v = np.zeros(NCH)
    for k, x in kw.items():
        v[CI[k]] = x
    return v


# reparto de cada rol de pieza (ATAQUE, DEFENSA, RECURSOS, UTILIDAD) y generico
ROLE_W = np.stack([_w(POT=0.65, CRI=0.25, PEN=0.10), _w(VID=0.40, MIT=0.30, RED=0.30), _w(SOS=0.60, VID=0.25, MAN=0.15), _w(CDR=0.60, RED=0.15, POT=0.15, MAN=0.10)])
GEN_W = _w(POT=0.30, VID=0.25, MIT=0.20, RED=0.15, CRI=0.10)
PET_W = np.stack([_w(POT=0.75, CRI=0.25), _w(RED=0.8, MIT=0.2), _w(VID=0.55, SOS=0.45), _w(CDR=0.65, MAN=0.2, POT=0.15)])
BUFF_W = np.stack([
    _w(VID=0.35, MIT=0.35, RED=0.30),         # SoulBarrier
    _w(POT=1.0),                              # Expansion
    _w(MIT=0.45, RED=0.40, VID=0.15),         # Defense
    _w(VID=1.0),                              # SwellLife
    _w(SOS=1.0),                              # Heal
    _w(RED=0.8, MIT=0.2),                     # GreaterDefense
    _w(POT=1.0),                              # GreaterDamage
    _w(POT=0.75, CDR=0.25),                   # InfinityArrow
    _w(CRI=1.0),                              # IncCrit
    _w(RED=0.4, SOS=0.3, POT=0.3),            # Reflection
    _w(POT=0.5, VID=0.5),                     # Berserker
    _w(PEN=0.4, POT=0.6),                     # IgnoreDefense
    _w(VID=1.0),                              # IncreaseHealth
    _w(RED=0.55, MIT=0.45),                   # IncreaseBlock
    _w(SOS=0.7, VID=0.3),                     # Recovery
])
PRIM, SEC = sim.CLS_PRIMARY, sim.CLS_SECONDARY

# ----------------------------------------------------------------------------------------------------------------------
# Perillas: valor inicial (ajustado) y rango [x f_lo, x f_hi]
# ----------------------------------------------------------------------------------------------------------------------
INIT = {}
_ST = {"STR": 0.35, "AGI": 0.35, "ENE": 0.35, "VIT": 0.45, "CMD": 0.35}
for n_, v_ in _ST.items():
    INIT["techo_" + n_] = v_
    INIT["k_" + n_] = 0.6
_TR = {"arbol_ofensa": 0.30, "arbol_defensa": 0.30, "arbol_critico": 0.40, "arbol_durabilidad": 0.30, "arbol_vida": 0.35, "arbol_mana": 0.40}
for n_, v_ in _TR.items():
    INIT["techo_" + n_] = v_
INIT["k_arbol"] = 0.8
for n_, w_ in zip(sim.SLOT_NAMES, sim.SLOT_COST_WEIGHT):
    INIT["techo_item_" + n_] = 0.40 * float(w_)
INIT["k_item"] = 0.9
for n_, v_ in zip(sim.PET_NAMES, [0.12, 0.10, 0.12, 0.12]):
    INIT["techo_" + n_] = v_
INIT["k_mascota"] = 0.8
for n_ in sim.BUFF_NAMES:
    INIT["techo_buff_" + n_] = 0.10
INIT["k_buff"] = 0.8
INIT.update({
    "afin_sec": 0.25, "afin_otro": 0.10, "agi_crit": 0.8, "vit_vida": 0.55, "item_generico": 0.35, "arma_pot": 0.20, "arma_anti": 0.5, "escudo_mit": 0.25,
    "gd": 0.09, "gh": 0.11, "H1": 1900.0,
    "e_pot": 1.0, "e_vid": 1.0, "e_mit": 1.0, "e_red": 1.0, "Ap": 7.0, "cmax": 0.55, "cm": 1.75, "cm_inc": 0.12, "Ah": 3.5, "Mmax": 0.80, "m0": 0.086, "mu": 0.77, "Rmax": 0.45,
    "cdr_max": 0.45, "mb_max": 0.95, "rr": 0.022, "ls": 0.12, "sh0": 0.11, "aeg0": 0.15, "refl": 0.06, "pen_max": 0.98,
    "dmg_BK": 0.87, "hp_BK": 0.87, "dmg_RF": 0.93, "hp_RF": 0.93, "dmg_DL": 0.96, "hp_DL": 0.95, "dmg_MG": 1.135, "hp_MG": 1.135,
    "dmg_FE": 1.07, "hp_FE": 1.07, "dmg_GM": 1.10, "hp_GM": 1.10, "dmg_SUM": 0.976, "hp_SUM": 0.976,
})
# --- valores ajustados (los reescribe resultados/f07_bake.py; son los iniciales finales de las perillas) ---
# BAKE-INICIO
_FINAL = {
    "techo_STR": 0.162,
    "k_STR": 0.6,
    "techo_AGI": 0.162,
    "k_AGI": 0.6,
    "techo_ENE": 0.162,
    "k_ENE": 0.6,
    "techo_VIT": 0.15481,
    "k_VIT": 0.6,
    "techo_CMD": 0.162,
    "k_CMD": 0.6,
    "techo_arbol_ofensa": 0.162,
    "techo_arbol_defensa": 0.110732,
    "techo_arbol_critico": 0.162,
    "techo_arbol_durabilidad": 0.153764,
    "techo_arbol_vida": 0.162,
    "techo_arbol_mana": 0.041801,
    "k_arbol": 0.8,
    "techo_item_arma": 0.162,
    "techo_item_escudo": 0.134899,
    "techo_item_casco": 0.162,
    "techo_item_armadura": 0.162,
    "techo_item_pantalon": 0.162,
    "techo_item_guantes": 0.162,
    "techo_item_botas": 0.162,
    "techo_item_alas": 0.162,
    "techo_item_colgante": 0.13973,
    "techo_item_anillo1": 0.151026,
    "techo_item_anillo2": 0.149858,
    "k_item": 0.9,
    "techo_mascota_ataque": 0.118784,
    "techo_mascota_guardia": 0.032661,
    "techo_mascota_vital": 0.057589,
    "techo_mascota_veloz": 0.035755,
    "k_mascota": 0.8,
    "techo_buff_SoulBarrier": 0.033748,
    "techo_buff_Expansion": 0.14196,
    "techo_buff_Defense": 0.029866,
    "techo_buff_SwellLife": 0.061493,
    "techo_buff_Heal": 0.054222,
    "techo_buff_GreaterDefense": 0.032661,
    "techo_buff_GreaterDamage": 0.14196,
    "techo_buff_InfinityArrow": 0.064223,
    "techo_buff_IncCrit": 0.005582,
    "techo_buff_Reflection": 0.061789,
    "techo_buff_Berserker": 0.122901,
    "techo_buff_IgnoreDefense": 0.162,
    "techo_buff_IncreaseHealth": 0.061493,
    "techo_buff_IncreaseBlock": 0.028548,
    "techo_buff_Recovery": 0.055419,
    "k_buff": 0.8,
    "afin_sec": 0.25,
    "afin_otro": 0.1,
    "agi_crit": 0.8,
    "vit_vida": 0.55,
    "item_generico": 0.35,
    "arma_pot": 0.2,
    "arma_anti": 0.5,
    "escudo_mit": 0.25,
    "gd": 0.075,
    "gh": 0.105,
    "H1": 3800.0,
    "e_pot": 1.0,
    "e_vid": 1.0,
    "e_mit": 1.0,
    "e_red": 1.0,
    "Ap": 42.0,
    "cmax": 0.55,
    "cm": 1.75,
    "cm_inc": 0.12,
    "Ah": 18.0,
    "Mmax": 0.8,
    "m0": 0.086,
    "mu": 0.77,
    "Rmax": 0.45,
    "cdr_max": 0.45,
    "mb_max": 0.95,
    "rr": 0.022,
    "ls": 0.12,
    "sh0": 0.11,
    "aeg0": 0.15,
    "refl": 0.06,
    "pen_max": 0.98,
    "dmg_BK": 0.88975,
    "hp_BK": 0.883117,
    "dmg_RF": 0.850018,
    "hp_RF": 0.875884,
    "dmg_DL": 1.046499,
    "hp_DL": 1.006241,
    "dmg_MG": 1.127928,
    "hp_MG": 1.13028,
    "dmg_FE": 1.101816,
    "hp_FE": 1.091107,
    "dmg_GM": 1.024014,
    "hp_GM": 1.048741,
    "dmg_SUM": 1.018462,
    "hp_SUM": 1.004106,
}
# BAKE-FIN
INIT.update(_FINAL)
_RANGE = {"gd": (0.7, 1.3), "gh": (0.7, 1.3), "H1": (0.5, 2.0), "Ah": (0.3, 2.0), "Rmax": (0.5, 1.2), "afin_sec": (0.0, 3.0), "afin_otro": (0.0, 3.0), "vit_vida": (0.5, 1.7), "item_generico": (0.3, 2.0),
          "Mmax": (0.6, 1.06), "m0": (0.5, 2.0), "mu": (0.6, 1.3), "cm": (0.7, 1.6), "e_pot": (0.6, 2.0), "e_vid": (0.6, 2.0), "e_mit": (0.6, 2.0), "e_red": (0.6, 2.0), "refl": (0.3, 3.0), "pen_max": (0.5, 1.0), "agi_crit": (0.3, 1.2), "arma_anti": (0.0, 3.0)}
PARAMS = {}
for _k, _v in INIT.items():
    if _k.startswith("techo_"):
        _lo, _hi = 0.25, 3.0
    elif _k.startswith(("dmg_", "hp_")):
        _lo, _hi = 0.6, 1.6
    elif _k.startswith("k_"):
        _lo, _hi = 0.2, 4.0
    else:
        _lo, _hi = _RANGE.get(_k, (0.5, 2.0))
    PARAMS[_k] = (_v, max(_v * _lo, 1e-4), max(_v * _hi, 1e-3))
# techos nunca >= 0,9 (cada fuente es una fraccion estricta del canal; garantiza e < 1 sin recortar)
for _k in PARAMS:
    if _k.startswith("techo_"):
        PARAMS[_k] = (PARAMS[_k][0], PARAMS[_k][1], min(PARAMS[_k][2], 0.9))


def sat(x, k):
    return x * (1.0 + k) / (x + k)


class _Acc:
    """Acumula riesgos h = -ln(1-e) por canal y por grupo (el complemento del canal es exp(-suma de riesgos))."""

    def __init__(self, n):
        self.H = np.zeros((len(GROUPS), NCH, n))

    def add(self, g, w, e):
        """w: (NCH,) o (n,NCH) reparto del efecto e (n,) entre canales."""
        w = np.asarray(w, dtype=float)
        if w.ndim == 1:
            nz = np.flatnonzero(w)
            self.H[GI[g], nz] += -np.log1p(-(e[None, :] * w[nz, None]))
        else:
            nz = np.flatnonzero(w.any(axis=0))
            self.H[GI[g], nz] += -np.log1p(-(e[None, :] * w[:, nz].T))

    def total(self):
        return self.H.sum(axis=0)


def _channels(b, p):
    n = b.n
    acc = _Acc(n)
    ar = np.arange(n)
    # --- stats ---
    s = b.stats / sim.STAT_CAP
    aff = np.full((n, 5), p["afin_otro"])
    aff[ar, SEC[b.cls]] = np.where(SEC[b.cls] == 3, 0.0, p["afin_sec"])
    aff[ar, PRIM[b.cls]] = 1.0
    aff[:, 3] = 0.0
    for i, nm in enumerate(sim.STAT_NAMES):
        u = sat(s[:, i], p["k_" + nm])
        t = p["techo_" + nm]
        if i != 3:
            e_pot = t * aff[:, i] * u
            main = (PRIM[b.cls] == i)
            acc.add("stat_principal", _w(POT=1.0), e_pot * main)
            acc.add("stats_otros", _w(POT=1.0), e_pot * ~main)
        if i == 1:
            acc.add("stats_otros", _w(CRI=1.0), t * p["agi_crit"] * u)
        if i == 3:
            acc.add("stats_otros", _w(VID=1.0), t * p["vit_vida"] * u)
            acc.add("stats_otros", _w(MIT=1.0), t * (1.0 - p["vit_vida"]) * u)
    # --- arbol ---
    tr = b.tree / sim.TREE_NODE_CAP
    ut = sat(tr, p["k_arbol"])
    tw = {"arbol_ofensa": _w(POT=1.0), "arbol_defensa": _w(MIT=0.8, RED=0.2), "arbol_critico": _w(CRI=0.6, POT=0.25, PEN=0.15), "arbol_durabilidad": _w(RED=1.0),
          "arbol_vida": _w(VID=1.0), "arbol_mana": _w(MAN=1.0)}
    for j, nm in enumerate(sim.TREE_NAMES):
        acc.add("arbol", tw[nm], p["techo_" + nm] * ut[:, j])
    # --- items: generico + reparto del rol; arma y escudo suman lo suyo ---
    tau = b.tier / 3.0
    gsh = p["item_generico"]
    u_sh = None
    u_wp = None
    for sl, nm in enumerate(sim.SLOT_NAMES):
        u = sat(tau[:, sl], p["k_item"])
        e = p["techo_item_" + nm] * u
        wmat = gsh * GEN_W[None, :] + (1.0 - gsh) * ROLE_W[b.role[:, sl]]
        grp = "arma" if sl == 0 else "piezas"
        acc.add(grp, wmat, e)
        if sl == 0:
            acc.add("arma", _w(POT=1.0), p["arma_pot"] * e)
            u_wp = u
        if sl == 1:
            acc.add("piezas", _w(MIT=1.0), p["escudo_mit"] * e)
            u_sh = u
    # --- mascotas ---
    for k, nm in enumerate(sim.PET_NAMES):
        acc.add("mascotas", PET_W[k], p["techo_" + nm] * sat(b.pet[:, k], p["k_mascota"]))
    # --- buffs ---
    br = b.buff / 5.0
    ub = sat(br, p["k_buff"])
    for m, nm in enumerate(sim.BUFF_NAMES):
        acc.add("buffs", BUFF_W[m], p["techo_buff_" + nm] * ub[:, m])
    return acc, u_sh, u_wp, ub


def _core(b, p):
    acc, u_sh, u_wp, ub = _channels(b, p)
    H = acc.total()
    C = 1.0 - np.exp(-H)                      # complemento por canal: 1 - prod(1-e_i)
    c = {nm: C[CI[nm]] for nm in CH}
    frac = c["POT"]
    rot = kits.rotation(b, frac, cdr=p["cdr_max"] * c["CDR"], mana_regen_bonus=p["mb_max"] * c["MAN"])
    sus = rot["sustain"]
    pw = 1.0 + p["Ap"] * c["POT"] ** p["e_pot"]
    cm = p["cm"] + p["cm_inc"] * ub[:, 8]
    critf = 1.0 + p["cmax"] * c["CRI"] * (cm - 1.0)
    lvl = np.exp(p["gd"] * (b.L - 1.0))
    dmgc = np.array([p["dmg_" + x] for x in CLS])[b.cls]
    dps = lvl * dmgc * b.dmg_tilt * rot["steady"] * sus * pw * critf
    hpc = np.array([p["hp_" + x] for x in CLS])[b.cls]
    hp = p["H1"] * np.exp(p["gh"] * (b.L - 1.0)) * b.hp_tilt * hpc * (1.0 + p["Ah"] * c["VID"] ** p["e_vid"])
    return dict(acc=acc, H=H, c=c, rot=rot, sus=sus, pw=pw, critf=critf, lvl=lvl, dmgc=dmgc, dps=dps, hp=hp, u_sh=u_sh, u_wp=u_wp, ub=ub)


_CACHE = [None, None, None]


def _get(b, p):
    if _CACHE[0] is b and _CACHE[1] is p:
        return _CACHE[2]
    r = _core(b, p)
    _CACHE[0], _CACHE[1], _CACHE[2] = b, p, r
    return r


def attack(b, p):
    k = _get(b, p)
    c = k["c"]
    rot = k["rot"]
    pen = p["pen_max"] * p["m0"] * c["PEN"]                 # siempre < mitigacion base m0: nunca hay que recortar
    return {"dps_phys": k["dps"] * (1.0 - b.mag), "dps_mag": k["dps"] * b.mag, "pen_phys": pen, "pen_mag": pen,
            "lifesteal": p["ls"] * c["SOS"], "antiheal": p["arma_anti"] * 0.4 * k["u_wp"] / (1.0 + p["arma_anti"] * 0.4 * k["u_wp"]),
            "burst_mult": rot["burst_mult"] / np.maximum(k["sus"], 1e-6), "sustain": 1.0, "aoe_share": rot["aoe_share"]}


def defense(b, p):
    k = _get(b, p)
    c = k["c"]
    hp = k["hp"]
    m0, Mm = p["m0"], p["Mmax"]
    cm_ = c["MIT"] ** p["e_mit"]
    mit = m0 + (Mm - m0) * cm_
    mit_mag = m0 + (Mm - m0) * p["mu"] * cm_
    dtm = 1.0 - p["Rmax"] * c["RED"] ** p["e_red"]
    return {"hp": hp, "shield": hp * p["sh0"], "mit_phys": mit, "mit_mag": mit_mag, "dmg_taken_mult": dtm, "regen": hp * p["rr"] * c["SOS"],
            "aegis": hp * p["aeg0"] * k["u_sh"], "aegis_cd": 50.0, "reflect": p["refl"] * k["ub"][:, 9]}


def decompose(b, p):
    """Factores multiplicativos de UN golpe (el log de cada uno reparte el golpe): nivel, kit de clase, rango de skill y cadencia (enfriamiento y mana),
    potencia por grupo de fuentes (stat principal, otros stats, arbol, arma, piezas, mascotas, buffs; cada grupo en proporcion a su riesgo -ln(1-e) dentro del canal) y critico."""
    k = _get(b, p)
    n = b.n
    c = k["c"]
    b1 = b.copy()
    b1.rank = np.ones(n)
    r01 = kits.rotation(b1, 0.0)["steady"]
    r0 = kits.rotation(b, 0.0)["steady"]
    rc = kits.rotation(b, 0.0, cdr=p["cdr_max"] * c["CDR"], mana_regen_bonus=p["mb_max"] * c["MAN"])["steady"]
    rf = kits.rotation(b, c["POT"], cdr=p["cdr_max"] * c["CDR"], mana_regen_bonus=p["mb_max"] * c["MAN"])["steady"]
    lnpot = np.log(np.maximum(rf / np.maximum(rc, 1e-9), 1e-9)) + np.log(k["pw"])
    Hpot = k["acc"].H[:, CI["POT"], :]
    tot = np.maximum(Hpot.sum(axis=0), 1e-12)
    f = {"nivel": np.exp(p["gd"] * (b.L - 1.0)), "kit_clase": r01 / 100.0, "rango_y_cadencia": rc / np.maximum(r01, 1e-9)}
    for g in GROUPS:
        f["potencia_" + g] = np.exp(lnpot * Hpot[GI[g]] / tot)
    f["critico"] = k["critf"]
    return {"mode": "mult", "factors": f}
