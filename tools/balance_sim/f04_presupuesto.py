"""F04 - PRESUPUESTO CON PESOS.

Cada personaje junta un presupuesto de poder: suma ponderada de sus 41 elementos (5 stats, 6 nodos del arbol, 11 ranuras de items, 4 mascotas, 15 buffs).
Cada elemento aporta (peso propio x valor saturado) a unos pocos "fondos": OFF (dano), CRI (critico/penetracion), VIDA, DUR (dureza), SOS (sosten: regen y vida al golpear),
CDR (enfriamiento) y MANA. El reparto entre ofensa y defensa es suave (razon con exponente gamma: una especie de softmax de dos salidas) y las salidas de combate salen
de funciones con rendimientos decrecientes naturales (1-exp(-x/k)), sin ningun tope duro.
"""
import numpy as np
import sim
import kits

FORMULA_TEXT = (
    "FONDOS (cada elemento e aporta w_e x u_e a uno o varios fondos; u = valor saturado sat(x,k)=x(1+k)/(x+k) de su entrada normalizada):\n"
    "  OFF, CRI = ofensa;  VIDA, DUR, SOS = defensa;  CDR, MANA = rotacion.\n"
    "REPARTO: O=OFF+CRI, D=rho(VIDA+DUR+SOS), B=O+D, s=1/(1+(D/O)^gamma), O'=s B, D'=(1-s)B/rho (suave, gamma>1 especializa; cada fondo se reescala en su lado).\n"
    "OFENSA: frac=1-exp(-OFF'/Ko); rotacion=kits.rotation(frac, cdr=cdrMax(1-exp(-CDR/Kcd)), mana=mbMax(1-exp(-MANA/Kmn))); "
    "dps = exp(gd(L-1)) x kit_clase x tilt x rotacion x sostenido x (1 + cmax(1-exp(-CRI'/Kc))(cm-1)); pen = pc x prob.crit + pi x IgnoreDefense.\n"
    "DEFENSA: hp = H1 exp(gh(L-1)) x tilt x (1 + Ah(1-exp(-VIDA'/Kh))); mit_fis = 1-exp(-DUR'/Km); mit_mag = 1-exp(-mu DUR'/Km); dano_recibido = exp(-DUR'/Kd); "
    "regen = hp rr SOS'/(SOS'+Ks); vida al golpear = ls SOS'/(SOS'+Ks); escudo = hp sh0."
)

CLS = sim.CLASS_NAMES
# --- valores finales ajustados (pesos calibrados: cada elemento mueve ~2-3 % el dano o la vida efectiva de un personaje de referencia; despues ajuste de globales y de constantes por clase) ---
INIT = {
    "kd": 0.9,
    "kh": 0.1,
    "w_STR": 0.2991,
    "w_AGI": 0.3167,
    "w_ENE": 0.3703,
    "w_VIT": 0.4531,
    "w_CMD": 0.5995,
    "w_arbol_ofensa": 0.3615,
    "w_arbol_defensa": 0.3585,
    "w_arbol_critico": 0.6631,
    "w_arbol_durabilidad": 0.36,
    "w_arbol_vida": 0.7118,
    "w_arbol_mana": 0.09933,
    "w_item_arma": 0.2453,
    "w_item_escudo": 0.2292,
    "w_item_casco": 0.3058,
    "w_item_armadura": 1.079,
    "w_item_pantalon": 0.5145,
    "w_item_guantes": 0.379,
    "w_item_botas": 0.3342,
    "w_item_alas": 0.524,
    "w_item_colgante": 0.2679,
    "w_item_anillo1": 0.2898,
    "w_item_anillo2": 0.3399,
    "w_mascota_ataque": 0.0741,
    "w_mascota_guardia": 0.04438,
    "w_mascota_vital": 0.08053,
    "w_mascota_veloz": 0.09469,
    "w_buff_SoulBarrier": 0.06748,
    "w_buff_Expansion": 0.05781,
    "w_buff_Defense": 0.06332,
    "w_buff_SwellLife": 0.07883,
    "w_buff_Heal": 0.09775,
    "w_buff_GreaterDefense": 0.06792,
    "w_buff_GreaterDamage": 0.04605,
    "w_buff_InfinityArrow": 0.06357,
    "w_buff_IncCrit": 0.1167,
    "w_buff_Reflection": 0.09011,
    "w_buff_Berserker": 0.1355,
    "w_buff_IgnoreDefense": 0.1002,
    "w_buff_IncreaseHealth": 0.0729,
    "w_buff_IncreaseBlock": 0.06672,
    "w_buff_Recovery": 0.0896,
    "sat_stat": 0.5501,
    "sat_tree": 1.0,
    "afin_sec": 0.2238,
    "afin_otro": 0.09105,
    "agi_crit": 0.8,
    "vit_vida": 0.55,
    "item_generico": 0.4,
    "arma_base": 0.5,
    "escudo_dur": 0.2884,
    "gamma": 1.562,
    "rho": 1.0,
    "Ko": 4.0,
    "Ap": 2.0,
    "Kp": 3.5,
    "Kc": 1.288,
    "cmax": 0.5,
    "cm": 1.75,
    "cdr_max": 0.45,
    "K_cdr": 0.7447,
    "mb_max": 0.9675,
    "K_mana": 0.6576,
    "gd": 0.04362,
    "gh": 0.0266,
    "H1": 670.0,
    "Ah": 1.2,
    "Kh": 2.218,
    "Km": 2.8,
    "mu": 0.7685,
    "Kd": 6.408,
    "rr": 0.02242,
    "Ks": 0.7804,
    "ls": 0.12,
    "sh0": 0.1087,
    "pen_crit": 0.6407,
    "pen_ign": 0.7156,
    "m0": 0.08606,
    "refl": 0.06,
    "dmg_BK": 0.8727,
    "hp_BK": 0.8727,
    "dmg_RF": 0.9295,
    "hp_RF": 0.9295,
    "dmg_DL": 0.9587,
    "hp_DL": 0.9524,
    "dmg_MG": 1.135,
    "hp_MG": 1.135,
    "dmg_FE": 1.068,
    "hp_FE": 1.068,
    "dmg_GM": 1.097,
    "hp_GM": 1.097,
    "dmg_SUM": 0.9759,
    "hp_SUM": 0.9759,
}
# rango de cada perilla: [inicial x f_lo, inicial x f_hi] (los pasos del ajuste automatico son proporcionales al rango)
_RANGE = {"kd": (0.0, 3.0), "kh": (0.0, 4.0), "gd": (0.1, 2.5), "gh": (0.1, 2.5), "Ap": (0.4, 2.5), "gamma": (0.7, 1.8), "m0": (0.5, 2.0), "afin_sec": (0.0, 3.0), "afin_otro": (0.0, 3.0), "vit_vida": (0.5, 1.7), "item_generico": (0.3, 2.0), "mu": (0.6, 1.3),
          "sat_stat": (0.3, 4.0), "sat_tree": (0.3, 4.0), "cm": (0.7, 1.6), "refl": (0.3, 3.0)}
PARAMS = {}
for _k, _v in INIT.items():
    _lo, _hi = _RANGE.get(_k, (0.4, 2.5) if _k.startswith("w_") else (0.5, 2.0))
    if _k.startswith(("dmg_", "hp_")):
        _lo, _hi = 0.6, 1.6
    PARAMS[_k] = (_v, max(_v * _lo, 1e-4), max(_v * _hi, 1e-3))

# afinidad ofensiva clase x stat (0 STR, 1 AGI, 2 ENE, 3 VIT, 4 CMD): principal 1, secundaria afin_sec, resto afin_otro
_PRIM, _SEC = sim.CLS_PRIMARY, sim.CLS_SECONDARY
POOLS = ["OFF", "CRI", "VIDA", "DUR", "SOS", "CDR"]
# fondos de cada rol de item (ATAQUE, DEFENSA, RECURSOS, UTILIDAD) y de cada mascota y buff: [OFF, CRI, VIDA, DUR, SOS, CDR]
ROLE_POOL = np.array([[0.75, 0.25, 0, 0, 0, 0], [0, 0, 0.5, 0.5, 0, 0], [0, 0, 0.3, 0, 0.7, 0], [0.2, 0, 0, 0.2, 0, 0.6]])
PET_POOL = np.array([[0.8, 0.2, 0, 0, 0, 0], [0, 0, 0, 1.0, 0, 0], [0, 0, 0.6, 0, 0.4, 0], [0.3, 0, 0, 0, 0, 0.7]])
BUFF_POOL = np.array([
    [0, 0, 0.5, 0.5, 0, 0],      # SoulBarrier: escudo + menos dano
    [1.0, 0, 0, 0, 0, 0],        # Expansion
    [0, 0, 0.3, 0.7, 0, 0],      # Defense
    [0, 0, 1.0, 0, 0, 0],        # SwellLife
    [0, 0, 0, 0, 1.0, 0],        # Heal
    [0, 0, 0, 1.0, 0, 0],        # GreaterDefense
    [1.0, 0, 0, 0, 0, 0],        # GreaterDamage
    [0.8, 0, 0, 0, 0, 0.2],      # InfinityArrow
    [0, 1.0, 0, 0, 0, 0],        # IncCrit
    [0.2, 0, 0, 0.5, 0.3, 0],    # Reflection
    [0.5, 0, 0.5, 0, 0, 0],      # Berserker
    [0.6, 0.4, 0, 0, 0, 0],      # IgnoreDefense
    [0, 0, 1.0, 0, 0, 0],        # IncreaseHealth
    [0, 0, 0.4, 0.6, 0, 0],      # IncreaseBlock
    [0, 0, 0, 0, 1.0, 0],        # Recovery
])
GROUPS = ["stats", "arbol", "arma", "items", "mascotas", "buffs"]


def sat(x, k):
    return x * (1.0 + k) / (x + k)


def _pools(b, p):
    """Devuelve (pool[nombre] -> (N,), grupo[pool][grupo] -> (N,), mana (N,))."""
    n = b.n
    z = lambda: np.zeros(n)
    pool = {k: z() for k in POOLS}
    grp = {k: {g: z() for g in GROUPS} for k in POOLS}
    mana = z()

    def add(pn, g, v):
        pool[pn] += v
        grp[pn][g] += v

    # --- stats ---
    s = b.stats / sim.STAT_CAP
    u = sat(s, p["sat_stat"])
    aff = np.full((n, 5), p["afin_otro"])
    ar = np.arange(n)
    aff[ar, _SEC[b.cls]] = np.where(_SEC[b.cls] == 3, 0.0, p["afin_sec"])
    aff[ar, _PRIM[b.cls]] = 1.0
    wst = np.array([p["w_" + x] for x in sim.STAT_NAMES])
    add("OFF", "stats", (u[:, [0, 1, 2, 4]] * aff[:, [0, 1, 2, 4]] * wst[[0, 1, 2, 4]]).sum(axis=1))
    add("CRI", "stats", p["agi_crit"] * wst[1] * u[:, 1])
    add("VIDA", "stats", wst[3] * p["vit_vida"] * u[:, 3])
    add("DUR", "stats", wst[3] * (1.0 - p["vit_vida"]) * u[:, 3])
    # --- arbol ---
    t = sat(b.tree / sim.TREE_NODE_CAP, p["sat_tree"])
    wt = np.array([p["w_" + x] for x in sim.TREE_NAMES])
    add("OFF", "arbol", wt[0] * t[:, 0])
    add("CRI", "arbol", wt[2] * t[:, 2])
    add("DUR", "arbol", wt[1] * t[:, 1] + wt[3] * t[:, 3])
    add("VIDA", "arbol", wt[4] * t[:, 4])
    mana += wt[5] * t[:, 5]
    # --- items: parte generica (todo item sirve) + parte segun el rol de la pieza; el arma va en su propio grupo ---
    wi = np.array([p["w_item_" + x] for x in sim.SLOT_NAMES])
    x = wi[None, :] * (b.tier / 3.0)
    g = p["item_generico"]
    for gname, sl in (("arma", [0]), ("items", list(range(1, sim.N_SLOT)))):
        xs = x[:, sl]
        gen = xs.sum(axis=1) * g
        add("OFF", gname, gen * 0.5)
        add("VIDA", gname, gen * 0.25)
        add("DUR", gname, gen * 0.25)
        for r in range(4):
            tr = (xs * (b.role[:, sl] == r)).sum(axis=1) * (1.0 - g)
            for j, pn in enumerate(POOLS):
                if ROLE_POOL[r, j]:
                    add(pn, gname, tr * ROLE_POOL[r, j])
    add("OFF", "arma", p["arma_base"] * x[:, 0])
    add("DUR", "items", p["escudo_dur"] * x[:, 1])
    # --- mascotas ---
    wp = np.array([p["w_" + x_] for x_ in sim.PET_NAMES])
    pv = b.pet * wp[None, :]
    for k in range(4):
        for j, pn in enumerate(POOLS):
            if PET_POOL[k, j]:
                add(pn, "mascotas", pv[:, k] * PET_POOL[k, j])
    # --- buffs ---
    wb = np.array([p["w_buff_" + x_] for x_ in sim.BUFF_NAMES])
    bv = (b.buff / 5.0) * wb[None, :]
    for k in range(sim.N_BUFF):
        for j, pn in enumerate(POOLS):
            if BUFF_POOL[k, j]:
                add(pn, "buffs", bv[:, k] * BUFF_POOL[k, j])
    return pool, grp, mana


def _core(b, p, with_dec=False):
    pool, grp, mana = _pools(b, p)
    eps = 1e-3
    rho = p["rho"]
    O = pool["OFF"] + pool["CRI"]
    D = rho * (pool["VIDA"] + pool["DUR"] + pool["SOS"])
    B = O + D
    sig = 1.0 / (1.0 + np.exp(-p["gamma"] * (np.log(O + eps) - np.log(D + eps))))
    so = sig * B / (O + eps)
    sd = (1.0 - sig) * B / (D + eps)
    off = pool["OFF"] * so
    cri = pool["CRI"] * so
    vida = pool["VIDA"] * sd
    dur = pool["DUR"] * sd
    sos = pool["SOS"] * sd
    cdr = p["cdr_max"] * (1.0 - np.exp(-pool["CDR"] / p["K_cdr"]))
    mb = p["mb_max"] * (1.0 - np.exp(-mana / p["K_mana"]))
    frac = 1.0 - np.exp(-off / p["Ko"])
    rot = kits.rotation(b, frac, cdr=cdr, mana_regen_bonus=mb)
    pw = 1.0 + p["Ap"] * (1.0 - np.exp(-off / p["Kp"]))
    ccrit = p["cmax"] * (1.0 - np.exp(-cri / p["Kc"]))
    critf = 1.0 + ccrit * (p["cm"] - 1.0)
    lvl = np.exp(p["gd"] * (b.L - 1.0)) * b.L ** (-p["kd"])
    dmgc = np.array([p["dmg_" + c] for c in CLS])[b.cls]
    sus = rot["sustain"]
    dps = lvl * dmgc * b.dmg_tilt * rot["steady"] * sus * critf * pw
    out = dict(pool=pool, grp=grp, so=so, sd=sd, off=off, cri=cri, vida=vida, dur=dur, sos=sos, cdr=cdr, mb=mb, frac=frac, rot=rot, ccrit=ccrit,
               critf=critf, pw=pw, lvl=lvl, dmgc=dmgc, dps=dps, sus=sus)
    return out


def attack(b, p):
    c = _core(b, p)
    ign = (b.buff[:, 11] / 5.0)
    pen = 0.98 * p["m0"] * (1.0 - np.exp(-(p["pen_crit"] * c["ccrit"] + p["pen_ign"] * ign)))   # siempre menor que la mitigacion base: nunca recorta
    rot = c["rot"]
    return {"dps_phys": c["dps"] * (1.0 - b.mag), "dps_mag": c["dps"] * b.mag, "pen_phys": pen, "pen_mag": pen,
            "lifesteal": p["ls"] * c["sos"] / (c["sos"] + p["Ks"]), "burst_mult": rot["burst_mult"] / np.maximum(c["sus"], 1e-6), "sustain": 1.0, "aoe_share": rot["aoe_share"]}


def defense(b, p):
    c = _core(b, p)
    hpc = np.array([p["hp_" + x] for x in CLS])[b.cls]
    hp = p["H1"] * np.exp(p["gh"] * (b.L - 1.0)) * b.L ** (-p["kh"]) * b.hp_tilt * hpc * (1.0 + p["Ah"] * (1.0 - np.exp(-c["vida"] / p["Kh"])))
    mit = p["m0"] + (1.0 - p["m0"]) * (1.0 - np.exp(-c["dur"] / p["Km"]))
    mit_mag = p["m0"] + (1.0 - p["m0"]) * (1.0 - np.exp(-p["mu"] * c["dur"] / p["Km"]))
    dtm = np.exp(-c["dur"] / p["Kd"])
    regen = hp * p["rr"] * c["sos"] / (c["sos"] + p["Ks"])
    return {"hp": hp, "shield": hp * p["sh0"], "mit_phys": mit, "mit_mag": mit_mag, "dmg_taken_mult": dtm, "regen": regen, "reflect": p["refl"] * (b.buff[:, 9] / 5.0)}


def decompose(b, p):
    """Factores multiplicativos de UN golpe (su log reparte el golpe): nivel, kit de la clase, rango de skill, reparto ofensivo (especializacion),
    ofensa por grupo (stats, arbol, arma, piezas, aliados = mascotas + buffs) y critico."""
    c = _core(b, p)
    n = b.n
    b1 = b.copy()
    b1.rank = np.ones(n)
    r0 = kits.rotation(b, 0.0)["steady"]
    r01 = kits.rotation(b1, 0.0)["steady"]
    cdr, mb = c["cdr"], c["mb"]
    frac_raw = 1.0 - np.exp(-c["pool"]["OFF"] / p["Ko"])
    pw_raw = 1.0 + p["Ap"] * (1.0 - np.exp(-c["pool"]["OFF"] / p["Kp"]))
    rf_raw = kits.rotation(b, frac_raw)["steady"]
    rf = kits.rotation(b, c["frac"])["steady"]
    rot = c["rot"]
    lnA = np.log(np.maximum(rf_raw / r0, 1e-9)) + np.log(pw_raw)
    offg = c["grp"]["OFF"]
    tot = np.maximum(sum(offg.values()), 1e-9)
    gsum = {"stats": offg["stats"], "arbol": offg["arbol"], "arma": offg["arma"], "piezas": offg["items"], "aliados": offg["mascotas"] + offg["buffs"]}
    f = {"nivel": np.exp(p["gd"] * (b.L - 1.0)), "kit_clase": r01 / 100.0, "rango_y_cadencia": (r0 / r01) * (rot["steady"] * c["sus"] / np.maximum(rf, 1e-9))}
    f["reparto_ofensivo"] = np.maximum(rf / np.maximum(rf_raw, 1e-9) * c["pw"] / pw_raw, 1e-9)
    for gname, v in gsum.items():
        f["ofensa_" + gname] = np.exp(lnA * v / tot)
    f["critico"] = c["critf"]
    return {"mode": "mult", "factors": f}
