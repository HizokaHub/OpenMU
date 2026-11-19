"""F10 - LEY DE CONSERVACION DEL PODER DE DUELO (metodo propio, no una de las 9 familias).

IDEA CENTRAL. En un duelo, A mata a B antes de que B mate a A  <=>  DPS_A x EHP_A > DPS_B x EHP_B  (EHP = vida efectiva).
Llamo PODER DE DUELO al producto P = DPS x EHP. Si dos personajes con la MISMA inversion (puntos de stats, arbol, oro, mascotas, buffs)
tienen el MISMO P, el duelo queda 50-50 sea cual sea su reparto entre dano y defensa: el tanque tiene mas EHP y menos DPS, el de dano al reves,
pero su producto es igual (la identidad es una INCLINACION, no una ventaja).

  log DPS = log DPS_nivel(L) + kO x O' + lean_clase/2 + psi_clase(L)/2          O' = fondos ofensivos (dano, critico, enfriamiento, mana)
  log EHP = log EHP_nivel(L) + kD x D' - lean_clase/2 + psi_clase(L)/2          D' = fondos defensivos (vida, mitigacion, durabilidad, sosten)
  log P   = log P_nivel(L) + kO x O' + kD x D' + psi_clase(L)       (con kO = kD: solo importa CUANTO se invirtio, no EN QUE)

Cada elemento (5 stats, 6 nodos de arbol, 11 ranuras de item, 4 mascotas, 15 buffs) aporta peso_propio x valor_saturado a un fondo (perilla propia por elemento).
La saturacion x(1+k)/(x+k) por elemento y el 1-exp de mitigacion y de vida dan los limites de forma natural (nada de topes duros).
Las diferencias de clase NO se esconden en 14 constantes: son (a) la inclinacion base lean_clase (de los tilts del juego), (b) la forma del kit (rafaga, sostenido por mana, area, alcance,
magia vs fisico), y (c) una curva suave psi_clase(L) (3 numeros por clase) que cierra el residuo medido; se calcula en lazo cerrado con f10_calibra.py.
Contra el rival que mas magia/critico mete, el tanque lo nota: la mitigacion fisica es exp(-x_m) pero la magica solo exp(-mu x_m) (mu < 1) y el critico y IgnoreDefense penetran.
"""
import numpy as np
import sim
import kits

CLS = sim.CLASS_NAMES
POOLS = ["OFF", "CRI", "VIDA", "DUR", "SOS", "CDR"]
ROLE_POOL = np.array([[0.75, 0.25, 0, 0, 0, 0], [0, 0, 0.5, 0.5, 0, 0], [0, 0, 0.3, 0, 0.7, 0], [0.2, 0, 0, 0.2, 0, 0.6]])
PET_POOL = np.array([[0.8, 0.2, 0, 0, 0, 0], [0, 0, 0, 1.0, 0, 0], [0, 0, 0.6, 0, 0.4, 0], [0.3, 0, 0, 0, 0, 0.7]])
BUFF_POOL = np.array([
    [0, 0, 0.5, 0.5, 0, 0], [1.0, 0, 0, 0, 0, 0], [0, 0, 0.3, 0.7, 0, 0], [0, 0, 1.0, 0, 0, 0], [0, 0, 0, 0, 1.0, 0], [0, 0, 0, 1.0, 0, 0],
    [1.0, 0, 0, 0, 0, 0], [0.8, 0, 0, 0, 0, 0.2], [0, 1.0, 0, 0, 0, 0], [0.2, 0, 0, 0.5, 0.3, 0], [0.5, 0, 0.5, 0, 0, 0], [0.6, 0.4, 0, 0, 0, 0],
    [0, 0, 1.0, 0, 0, 0], [0, 0, 0.4, 0.6, 0, 0], [0, 0, 0, 0, 1.0, 0]])
GROUPS = ["stats", "arbol", "arma", "items", "mascotas", "buffs"]
_PRIM, _SEC = sim.CLS_PRIMARY, sim.CLS_SECONDARY
_LEAN0 = 0.5 * np.log(sim.CLS_DMG_TILT / sim.CLS_HP_TILT)

INIT = {
    # --- escala por nivel: TTK de equilibrio (s) en nivel 1 y 30 y crecimiento geometrico de la vida ---
    "ttk1": 7.0, "ttk30": 12.0, "h1": 1900.0, "h30": 2500.0,
    # --- cambio de cada fondo a log-poder (kO ofensa, kD defensa; iguales = conservacion exacta) ---
    "kO": 0.5, "kD": 0.5, "lean_gain": 1.0,
    # --- peso propio de cada elemento ---
    "w_STR": 0.5, "w_AGI": 0.5, "w_ENE": 0.5, "w_VIT": 0.5, "w_CMD": 0.5,
    "w_arbol_ofensa": 0.5, "w_arbol_defensa": 0.5, "w_arbol_critico": 0.5, "w_arbol_durabilidad": 0.5, "w_arbol_vida": 0.5, "w_arbol_mana": 0.1,
    "w_item_arma": 0.5, "w_item_escudo": 0.5, "w_item_casco": 0.5, "w_item_armadura": 0.5, "w_item_pantalon": 0.5, "w_item_guantes": 0.5,
    "w_item_botas": 0.5, "w_item_alas": 0.5, "w_item_colgante": 0.5, "w_item_anillo1": 0.5, "w_item_anillo2": 0.5,
    "w_mascota_ataque": 0.2, "w_mascota_guardia": 0.2, "w_mascota_vital": 0.2, "w_mascota_veloz": 0.2,
    "K_buff": 0.12, "w_buff_SoulBarrier": 0.1, "w_buff_Expansion": 0.1, "w_buff_Defense": 0.1, "w_buff_SwellLife": 0.1, "w_buff_Heal": 0.1,
    "w_buff_GreaterDefense": 0.1, "w_buff_GreaterDamage": 0.1, "w_buff_InfinityArrow": 0.1, "w_buff_IncCrit": 0.1, "w_buff_Reflection": 0.1,
    "w_buff_Berserker": 0.1, "w_buff_IgnoreDefense": 0.1, "w_buff_IncreaseHealth": 0.1, "w_buff_IncreaseBlock": 0.1, "w_buff_Recovery": 0.1,
    # --- forma de cada fuente ---
    "sat_stat": 1.2, "sat_tree": 1.5, "afin_sec": 0.35, "afin_otro": 0.12, "agi_crit": 0.8, "vit_vida": 0.5, "item_generico": 0.4, "arma_base": 0.1, "escudo_dur": 0.2,
    # --- como se convierte cada fondo defensivo en vida efectiva ---
    "phi_mit": 0.5, "m0": 0.08, "mu": 0.72, "rr": 0.02, "Ks": 0.8, "ls": 0.12, "sh0": 0.1, "refl": 0.06, "sos_ehp": 0.5,
    # --- rotacion (solo da la FORMA: rafaga, mana, area) ---
    "Ko": 4.0, "cdr_max": 0.45, "K_cdr": 0.75, "mb_max": 0.95, "K_mana": 0.65, "sus_w": 1.0, "pen_crit": 0.6, "pen_ign": 0.7,
    # --- curvas de clase psi_c(L) = a + b x + c x^2 con x=(L-1)/29  (las rellena f10_calibra.py) ---
}
for _e in ("e1", "e2"):
    for _s in "abcdef":
        INIT[f"{_e}_{_s}"] = 0.0
for _c in CLS:
    for _s in "abd":
        INIT[f"slc_{_s}_{_c}"] = 0.0
    INIT["psi_a_" + _c] = 0.0
    INIT["psi_b_" + _c] = 0.0
    INIT["psi_c_" + _c] = 0.0
    INIT["psi_d_" + _c] = 0.0
    INIT["psi_e_" + _c] = 0.0
    INIT["psi_f_" + _c] = 0.0
    INIT["lean_" + _c] = 0.0     # ajuste fino de la inclinacion de la clase (se suma a la de los tilts)

_RANGE_ABS = {"ttk1": (3, 14), "ttk30": (6, 22), "h1": (1000, 4000), "h30": (4000, 60000), "kO": (0.3, 3.0), "kD": (0.3, 3.0), "lean_gain": (0, 3),
              "sat_stat": (0.2, 6), "sat_tree": (0.2, 6), "phi_mit": (0.1, 0.9), "m0": (0.01, 0.3), "mu": (0.4, 1.0), "Ko": (1, 12), "sus_w": (0, 2),
              "cdr_max": (0.1, 0.6), "mb_max": (0.2, 1.5)}
PARAMS = {}
for _k, _v in INIT.items():
    if _k in _RANGE_ABS:
        PARAMS[_k] = (_v, *_RANGE_ABS[_k])
    elif _k.startswith(("psi_", "lean_", "e1_", "e2_", "slc_")):
        PARAMS[_k] = (_v, -3.0, 3.0)
    elif _k.startswith("w_"):
        PARAMS[_k] = (_v, _v * 0.2, _v * 5.0)
    else:
        PARAMS[_k] = (_v, _v * 0.3 if _v > 0 else -1.0, _v * 3.0 if _v > 0 else 1.0)
FORMULA_TEXT = __doc__


def sat(x, k):
    return x * (1.0 + k) / (x + k)


def _pools(b, p):
    n = b.n
    z = lambda: np.zeros(n)
    pool = {k: z() for k in POOLS}
    grp = {k: {g: z() for g in GROUPS} for k in POOLS}
    mana = z()

    def add(pn, g, v):
        pool[pn] += v
        grp[pn][g] += v

    s = b.stats / sim.STAT_CAP
    u = sat(s, p["sat_stat"])
    ar = np.arange(n)
    aff = np.full((n, 5), p["afin_otro"])
    aff[ar, _SEC[b.cls]] = np.where(_SEC[b.cls] == 3, 0.0, p["afin_sec"])
    aff[ar, _PRIM[b.cls]] = 1.0
    wst = np.array([p["w_" + x] for x in sim.STAT_NAMES])
    add("OFF", "stats", (u[:, [0, 2, 4]] * aff[:, [0, 2, 4]] * wst[[0, 2, 4]]).sum(axis=1) + (1.0 - p["agi_crit"]) * wst[1] * u[:, 1] * aff[:, 1])
    add("CRI", "stats", p["agi_crit"] * wst[1] * u[:, 1])
    add("VIDA", "stats", wst[3] * p["vit_vida"] * u[:, 3])
    add("DUR", "stats", wst[3] * (1.0 - p["vit_vida"]) * u[:, 3])
    t = sat(b.tree / sim.TREE_NODE_CAP, p["sat_tree"])
    wt = np.array([p["w_" + x] for x in sim.TREE_NAMES])
    add("OFF", "arbol", wt[0] * t[:, 0])
    add("CRI", "arbol", wt[2] * t[:, 2])
    add("DUR", "arbol", wt[1] * t[:, 1] + wt[3] * t[:, 3])
    add("VIDA", "arbol", wt[4] * t[:, 4])
    mana += wt[5] * t[:, 5]
    # items: peso de la ranura x su precio relativo (paridad de coste por diseno) x tier/3; parte generica (todo item sirve) + parte por rol
    wi = np.array([p["w_item_" + x] for x in sim.SLOT_NAMES]) * sim.SLOT_COST_WEIGHT
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
    wp = np.array([p["w_" + x_] for x_ in sim.PET_NAMES])
    pv = b.pet * wp[None, :]
    for k in range(4):
        for j, pn in enumerate(POOLS):
            if PET_POOL[k, j]:
                add(pn, "mascotas", pv[:, k] * PET_POOL[k, j])
    wb = np.array([p["w_buff_" + x_] for x_ in sim.BUFF_NAMES])
    bv = (b.buff / 5.0) * wb[None, :]
    # todos los buffs suman a la vez, pero apilados con rendimientos decrecientes naturales: Kb (1 - exp(-suma / Kb)) por fondo (uno solo cuenta casi entero)
    for j, pn in enumerate(POOLS):
        v = (bv * BUFF_POOL[None, :, j]).sum(axis=1)
        add(pn, "buffs", p["K_buff"] * (1.0 - np.exp(-v / p["K_buff"])))
    return pool, grp, mana


def _xl(b):
    return (b.L - 1.0) / 29.0


def _core(b, p):
    pool, grp, mana = _pools(b, p)
    xl = _xl(b)
    ci = b.cls
    lean = np.array([_LEAN0[i] * p["lean_gain"] + p["lean_" + c] for i, c in enumerate(CLS)])[ci]
    psi = (np.array([p["psi_a_" + c] for c in CLS])[ci] + np.array([p["psi_b_" + c] for c in CLS])[ci] * xl + np.array([p["psi_c_" + c] for c in CLS])[ci] * xl ** 2
           + np.array([p["psi_d_" + c] for c in CLS])[ci] * np.exp(-(b.L - 1.0) / 2.5)
           + np.array([p["psi_e_" + c] for c in CLS])[ci] * np.exp(-(b.L - 1.0) / 8.0)
           + np.array([p["psi_f_" + c] for c in CLS])[ci] * np.exp(-(b.L - 1.0) / 1.2))
    O0 = pool["OFF"] + pool["CRI"] + pool["CDR"] + 0.5 * mana
    D0 = pool["VIDA"] + pool["DUR"] + p["sos_ehp"] * pool["SOS"]
    om = p["kO"] * O0 / np.maximum(p["kO"] * O0 + p["kD"] * D0, 1e-9)            # omega: parte ofensiva de la inversion (0 tanque ... 1 cristal)
    bas = [1.0, xl, xl ** 2, np.exp(-(b.L - 1.0) / 2.5), np.exp(-(b.L - 1.0) / 8.0), np.exp(-(b.L - 1.0) / 1.2)]
    e1 = sum(p["e1_" + s_] * v for s_, v in zip("abcdef", bas))
    e2 = sum(p["e2_" + s_] * v for s_, v in zip("abcdef", bas))
    exs = np.exp(-(b.L - 1.0) / 2.5)
    slc = (np.array([p["slc_a_" + c] for c in CLS])[ci] + np.array([p["slc_b_" + c] for c in CLS])[ci] * xl + np.array([p["slc_d_" + c] for c in CLS])[ci] * exs)
    psi = psi + slc * (om - 0.5) / 0.15 + e1 * (0.5 - om) + e2 * (((om - 0.5) / 0.15) ** 2 - 1.0)                   # correccion suave de la inclinacion (compensa el bono de repartir la inversion)
    cdr = p["cdr_max"] * (1.0 - np.exp(-pool["CDR"] / p["K_cdr"]))
    mb = p["mb_max"] * (1.0 - np.exp(-mana / p["K_mana"]))
    O = pool["OFF"] + pool["CRI"] + pool["CDR"] + 0.5 * mana
    sos = pool["SOS"]
    D = pool["VIDA"] + pool["DUR"] + p["sos_ehp"] * sos
    frac = 1.0 - np.exp(-pool["OFF"] / p["Ko"])
    rot = kits.rotation(b, frac, cdr=cdr, mana_regen_bonus=mb)
    sus = np.maximum(rot["sustain"], 1e-3) ** p["sus_w"]
    # base por nivel: el equilibrio EHP/DPS = TTK(L)
    hp_base = p["h1"] * (p["h30"] / p["h1"]) ** xl
    ttk = p["ttk1"] + (p["ttk30"] - p["ttk1"]) * xl
    dps_base = hp_base / ttk
    logd = np.log(dps_base) + p["kO"] * O + 0.5 * lean + 0.5 * psi
    loge = np.log(hp_base) + p["kD"] * D - 0.5 * lean + 0.5 * psi
    return dict(pool=pool, grp=grp, cdr=cdr, mb=mb, frac=frac, rot=rot, sus=sus, hp_base=hp_base, dps_base=dps_base, O=O, D=D, lean=lean, psi=psi, logd=logd, loge=loge)


def attack(b, p):
    c = _core(b, p)
    dps = np.exp(c["logd"]) * c["sus"]
    ign = b.buff[:, 11] / 5.0
    crit_part = 1.0 - np.exp(-c["pool"]["CRI"])
    pen = 0.98 * p["m0"] * (1.0 - np.exp(-(p["pen_crit"] * crit_part + p["pen_ign"] * ign)))   # menor que la mitigacion base: nunca recorta
    rot = c["rot"]
    sos = c["pool"]["SOS"]
    return {"dps_phys": dps * (1.0 - b.mag), "dps_mag": dps * b.mag, "pen_phys": pen, "pen_mag": pen, "lifesteal": p["ls"] * sos / (sos + p["Ks"]),
            "burst_mult": rot["burst_mult"] / np.maximum(c["sus"], 1e-6), "sustain": 1.0, "aoe_share": rot["aoe_share"]}


def defense(b, p):
    c = _core(b, p)
    d = c["D"]
    pool = c["pool"]
    # reparto del log-EHP defensivo: vida, mitigacion fisica (la magica la reduce mu) y menos dano recibido
    xv = p["kD"] * pool["VIDA"]
    xdur = p["kD"] * pool["DUR"]
    xm = p["phi_mit"] * xdur
    xt = (1.0 - p["phi_mit"]) * xdur
    xs = p["kD"] * p["sos_ehp"] * pool["SOS"]
    hp = np.exp(np.log(c["hp_base"]) + 0.5 * (-c["lean"] + c["psi"]) + xv + xs)
    mit = 1.0 - (1.0 - p["m0"]) * np.exp(-xm)
    mit_mag = 1.0 - (1.0 - p["m0"]) * np.exp(-p["mu"] * xm)
    dtm = np.exp(-xt)
    sos = pool["SOS"]
    regen = hp * p["rr"] * sos / (sos + p["Ks"])
    return {"hp": hp, "shield": hp * p["sh0"], "mit_phys": mit, "mit_mag": mit_mag, "dmg_taken_mult": dtm, "regen": regen, "reflect": p["refl"] * (b.buff[:, 9] / 5.0)}


def decompose(b, p):
    """Factores multiplicativos de UN golpe; la suma de sus logaritmos es el log del dps (sin el escalon de sostenido por mana).
    nivel; clase (inclinacion + curva); ofensa directa por origen (stats, arbol, arma, piezas, mascotas, buffs); critico; enfriamiento; mana."""
    c = _core(b, p)
    kO = p["kO"]
    g = c["grp"]
    pool = c["pool"]
    base1 = p["h1"] / p["ttk1"]
    def og(name):
        return np.exp(kO * g["OFF"][name])
    f = {
        "base_(nivel+clase)": np.exp(0.5 * c["lean"] + 0.5 * c["psi"]) * c["sus"] * c["dps_base"] / (p["h1"] / p["ttk30"]),
        "ofensa_stats": og("stats"),
        "ofensa_arbol": og("arbol"),
        "ofensa_arma": og("arma"),
        "ofensa_piezas": og("items"),
        "ofensa_mascotas": og("mascotas"),
        "ofensa_buffs": og("buffs"),
        "critico_agilidad": np.exp(kO * g["CRI"]["stats"]),
        "critico_equipo": np.exp(kO * (g["CRI"]["arbol"] + g["CRI"]["arma"] + g["CRI"]["items"] + g["CRI"]["mascotas"] + g["CRI"]["buffs"])),
        "rotacion_(enfriamiento+mana)": np.exp(kO * (pool["CDR"] + 0.5 * (c["O"] - pool["OFF"] - pool["CRI"] - pool["CDR"]))),
    }
    return {"mode": "mult", "factors": f}


def omega(b, p):
    """Parte ofensiva de la inversion de cada personaje (0 = tanque puro, 1 = cristal)."""
    pool, _, mana = _pools(b, p)
    O = pool["OFF"] + pool["CRI"] + pool["CDR"] + 0.5 * mana
    D = pool["VIDA"] + pool["DUR"] + p["sos_ehp"] * pool["SOS"]
    return p["kO"] * O / np.maximum(p["kO"] * O + p["kD"] * D, 1e-9)
