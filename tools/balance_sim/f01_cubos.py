"""F01 - MULTIPLICATIVA POR CUBOS.

El golpe es un producto de cubos independientes: base de nivel x rotacion real de skills x cubo de stats x cubo de arbol x cubo de items x cubo de mascotas
x cubo de buffs x critico x identidad de clase. Cada cubo es exp(A x S(puntos)) con S(x) = x(1+k)/(x+k) (saturacion normalizada: S(0)=0, S(1)=1, crece sin
tope duro pero con rendimientos decrecientes) y A es la ganancia (en logaritmo natural) al llenar el cubo. La defensa usa cubos analogos: UN cubo de vida donde
todas las fuentes (VIT, arbol, piezas, mascota, SwellLife, IncreaseHealth, Berserker) SUMAN antes de saturar (apilan con rendimientos decrecientes), mitigacion
m = Mmax x S(x), reduccion de dano exp(-A x S(x)), escudo y regeneracion. Sin np.minimum/np.clip: todo limite sale de la forma matematica.
"""
import dataclasses

import numpy as np

import kits
import sim

FORMULA_TEXT = (
    "xl=(nivel-1)/29 ; S(x,k)=x(1+k)/(x+k) (saturacion, S(1)=1) ; cubo(A,x)=exp(A*S(x))\n"
    "dps = U * (1+Llin*xl) * exp(Lgeo*xl) * ROT^ks*MEDIA^(1-ks) [rotacion real de las 6 skills por clase, normalizada por la media de clases] * 1/(1-cdr)ish [enfriamiento: del kit con cdr]\n"
    "      * id_clase * tilt_dano^pow * exp(A_st*S(dano_stat)) * exp(A_ar*S(arbol_ofensa)) * exp(A_it*S(items_poder)) * exp(A_pm*S(mascota_ataque)) * exp(A_bf*S(Expansion+GreaterDamage+Berserker))\n"
    "      * (1+p_crit*(cm-1)) ; p_crit=Pc*S(AGI+arbol_critico+items_crit+IncCrit) ; + dano verdadero = dps*(IgnoreDefense+Reflection)\n"
    "vida = H * (1+Hlin*xl) * exp(Hgeo*xl) * tilt_vida^pow * id_vida * exp(A_h*S(VIT+arbol_vida+items_vida+mascota_vital+SwellLife+IncreaseHealth+Berserker))\n"
    "mit = Mmax*S(VIT+STR+arbol_defensa+items_mit+GreaterDefense) (la magia ignora una parte) ; dano_recibido = exp(-A_d*S(arbol_dur+items_dur+guardia+Defense+SoulBarrier+IncreaseBlock))\n"
    "escudo = vida*(s0+A_s*S(items+SoulBarrier+Defense)) ; regen = vida*R*S(items+Heal+Recovery+mascota_vital)"
)

PARAMS = {}


def _p(name, v, lo=None, hi=None):
    PARAMS[name] = (float(v), float(v * 0.25 if lo is None else lo), float(v * 3.0 if hi is None else hi))


NAMES_STAT = sim.STAT_NAMES
NAMES_SLOT = sim.SLOT_NAMES
NAMES_BUFF = sim.BUFF_NAMES
NAMES_TREE = sim.TREE_NAMES
NAMES_PET = sim.PET_NAMES

# ---- niveles, clase y escala global ------------------------------------------------------------------------------------
_p("dps_unit", 1.0, 0.3, 4.0)
_p("dps_lvl_lin", 2.0, 0.0, 8.0)
_p("dps_lvl_geo", 0.9, 0.0, 3.0)
_p("hp_unit", 1800.0, 800.0, 5000.0)
_p("hp_lvl_lin", 2.0, 0.0, 8.0)
_p("hp_lvl_geo", 1.9, 0.0, 4.0)
_p("kit_strength", 0.8, 0.0, 1.5)
_p("dmg_tilt_pow", 1.0, 0.0, 3.0)
_p("hp_tilt_pow", 1.0, 0.0, 3.0)
for _c in sim.CLASS_NAMES:
    _p("id_dps_" + _c, 1.0, 0.6, 1.6)
    _p("id_hp_" + _c, 1.0, 0.6, 1.6)

# ---- stats (5 perillas de dano + roles propios de cada stat) -----------------------------------------------------------
for _n in NAMES_STAT:
    _p("stat_dmg_" + _n, 1.0, 0.2, 2.5)           # peso del stat en el cubo de dano (si es el principal de la clase; el secundario cuenta sec_w)
_p("sec_w", 0.35, 0.0, 1.0)
_p("A_stat_dmg", 0.50, 0.1, 1.5)
_p("k_stat_dmg", 1.5, 0.3, 6.0)
_p("w_agi_crit", 0.8, 0.1, 2.5)
_p("w_ene_mana", 0.9, 0.1, 2.5)
_p("w_agi_cdr", 0.25, 0.0, 1.0)
_p("cmd_amp", 0.35, 0.0, 1.2)                     # el Mando amplifica buffs y mascotas
_p("k_cmd", 1.0, 0.3, 4.0)
_p("w_vit_hp", 0.8, 0.1, 2.5)
_p("w_vit_mit", 1.0, 0.1, 2.5)
_p("w_str_mit", 0.35, 0.0, 1.5)
_p("w_vit_reg", 0.2, 0.0, 1.0)

# ---- arbol (6 perillas: una por nodo) ----------------------------------------------------------------------------------
_p("tr_ofensa", 0.22, 0.05, 0.8)                  # ganancia log del cubo de dano
_p("tr_defensa", 1.0, 0.2, 3.0)                   # peso en la mitigacion
_p("tr_critico", 1.0, 0.2, 3.0)                   # peso en el critico
_p("tr_durabilidad", 0.6, 0.1, 2.5)               # peso en el dano recibido
_p("tr_vida", 1.0, 0.2, 3.0)                      # peso en la vida
_p("tr_mana", 1.0, 0.2, 3.0)                      # peso en mana (sostenido) y enfriamiento
_p("k_tree", 1.5, 0.3, 6.0)

# ---- items: 11 perillas (valor de la ranura) + canales -----------------------------------------------------------------
for _n in NAMES_SLOT:
    _p("it_" + _n, 1.0, 0.3, 5.0)
_p("role_base", 0.3, 0.05, 0.8)                   # cuanto aporta una pieza fuera de su rol
_p("k_item", 1.5, 0.3, 6.0)
_p("A_it_pow", 0.55, 0.1, 1.5)
_p("w_it_crit", 1.0, 0.1, 3.0)
_p("w_it_mit", 1.0, 0.1, 3.0)
_p("w_it_dur", 1.0, 0.1, 3.0)
_p("w_it_reg", 1.0, 0.1, 3.0)
_p("w_it_cdr", 1.0, 0.1, 3.0)
_p("w_it_sh", 1.0, 0.1, 3.0)
_p("w_it_mana", 0.5, 0.0, 2.0)

# ---- mascotas (4 perillas) ---------------------------------------------------------------------------------------------
_p("pet_ataque", 0.10, 0.02, 0.5)                 # ganancia log del cubo de dano a poder 1
_p("pet_guardia", 0.8, 0.1, 3.0)                  # peso en dano recibido
_p("pet_vital", 0.8, 0.1, 3.0)                    # peso en vida (y regeneracion)
_p("pet_veloz", 1.0, 0.1, 3.0)                    # peso en enfriamiento
_p("k_pet", 1.0, 0.3, 4.0)

# ---- buffs (15 perillas) -----------------------------------------------------------------------------------------------
_BUFF_INIT = {"SoulBarrier": 0.6, "Expansion": 0.6, "Defense": 0.7, "SwellLife": 0.9, "Heal": 0.8, "GreaterDefense": 0.7, "GreaterDamage": 0.6,
              "InfinityArrow": 0.5, "IncCrit": 0.7, "Reflection": 0.07, "Berserker": 0.6, "IgnoreDefense": 0.12, "IncreaseHealth": 0.9, "IncreaseBlock": 0.6, "Recovery": 0.8}
for _n in NAMES_BUFF:
    _p("bf_" + _n, _BUFF_INIT[_n], 0.01, max(2.0, 4 * _BUFF_INIT[_n]))
_p("k_buff", 0.8, 0.2, 4.0)
_p("A_buff_dmg", 0.35, 0.05, 1.2)

# ---- cubos de dano restantes -------------------------------------------------------------------------------------------
_p("Pc_max", 0.5, 0.1, 0.9)
_p("cm0", 1.6, 1.2, 2.2)
_p("cm_buff", 0.4, 0.0, 1.0)
_p("k_crit", 1.0, 0.3, 4.0)
_p("cdr_max", 0.45, 0.1, 0.7)
_p("k_cdr", 1.2, 0.3, 5.0)
_p("crit_true", 0.25, 0.0, 0.8)                    # fraccion del dano de los criticos que ignora la mitigacion (el tanque nota al rival critico)
_p("mag_bypass", 0.25, 0.0, 0.7)                  # la magia ignora esta fraccion de la mitigacion (el tanque nota al rival magico)
_p("ls_max", 0.15, 0.02, 0.4)
_p("k_ls", 1.5, 0.3, 5.0)
_p("infinity_basic", 0.5, 0.05, 2.0)

# ---- defensa: formas -----------------------------------------------------------------------------------------------------
_p("A_hp", 0.7, 0.1, 2.0)
_p("k_hp", 1.5, 0.3, 6.0)
_p("Mmax", 0.70, 0.2, 0.84)
_p("k_mit", 1.2, 0.3, 5.0)
_p("A_dt", 0.45, 0.05, 1.3)
_p("k_dt", 1.2, 0.3, 5.0)
_p("sh0", 0.12, 0.0, 0.5)
_p("A_sh", 0.5, 0.05, 1.5)
_p("k_sh", 1.5, 0.3, 5.0)
_p("R_max", 0.02, 0.002, 0.08)
_p("k_reg", 1.5, 0.3, 5.0)
_p("w_it_hp_in", 1.0, 0.1, 3.0)

# >>> TUNED (valores iniciales finales tras el ajuste; no cambian rangos)
_TUNED = {
    "dps_unit": 0.95236,
    "dps_lvl_lin": 3.2006,
    "dps_lvl_geo": 0.85131,
    "hp_unit": 4000.0,
    "hp_lvl_lin": 0.06934,
    "hp_lvl_geo": 0.8,
    "kit_strength": 1.43371,
    "dmg_tilt_pow": 1.78683,
    "hp_tilt_pow": 0.42438,
    "id_dps_BK": 0.99421,
    "id_hp_BK": 0.9663,
    "id_dps_RF": 0.88833,
    "id_hp_RF": 1.05753,
    "id_dps_DL": 1.21425,
    "id_hp_DL": 1.00823,
    "id_dps_MG": 1.50224,
    "id_hp_MG": 1.0841,
    "id_dps_FE": 1.16,
    "id_hp_FE": 1.07913,
    "id_dps_GM": 0.79578,
    "id_hp_GM": 1.58112,
    "id_dps_SUM": 1.11281,
    "id_hp_SUM": 0.93407,
    "stat_dmg_STR": 0.837,
    "stat_dmg_AGI": 0.80513,
    "stat_dmg_ENE": 0.50061,
    "stat_dmg_VIT": 0.6439,
    "stat_dmg_CMD": 1.14244,
    "sec_w": 0.35,
    "A_stat_dmg": 0.55933,
    "k_stat_dmg": 0.3,
    "w_agi_crit": 0.8,
    "w_ene_mana": 0.9,
    "w_agi_cdr": 0.25,
    "cmd_amp": 0.35,
    "k_cmd": 1.0,
    "w_vit_hp": 0.41394,
    "w_vit_mit": 0.97241,
    "w_str_mit": 0.35,
    "w_vit_reg": 0.2,
    "tr_ofensa": 0.72449,
    "tr_defensa": 0.94212,
    "tr_critico": 2.73637,
    "tr_durabilidad": 0.8,
    "tr_vida": 0.2,
    "tr_mana": 1.18967,
    "k_tree": 0.3,
    "it_arma": 2.75196,
    "it_escudo": 1.69069,
    "it_casco": 2.06613,
    "it_armadura": 3.0415,
    "it_pantalon": 1.3323,
    "it_guantes": 1.85452,
    "it_botas": 4.25096,
    "it_alas": 2.71181,
    "it_colgante": 2.44696,
    "it_anillo1": 2.37468,
    "it_anillo2": 3.94117,
    "role_base": 0.3,
    "k_item": 1.20292,
    "A_it_pow": 0.93213,
    "w_it_crit": 1.0,
    "w_it_mit": 1.0,
    "w_it_dur": 1.0,
    "w_it_reg": 1.0,
    "w_it_cdr": 1.0,
    "w_it_sh": 1.0,
    "w_it_mana": 0.5,
    "pet_ataque": 0.01207,
    "pet_guardia": 0.73148,
    "pet_vital": 0.09587,
    "pet_veloz": 1.32217,
    "k_pet": 1.66353,
    "bf_SoulBarrier": 0.0499,
    "bf_Expansion": 0.04237,
    "bf_Defense": 0.02833,
    "bf_SwellLife": 0.0369,
    "bf_Heal": 0.23125,
    "bf_GreaterDefense": 0.39913,
    "bf_GreaterDamage": 0.12821,
    "bf_InfinityArrow": 0.52232,
    "bf_IncCrit": 0.16645,
    "bf_Reflection": 0.04954,
    "bf_Berserker": 0.05079,
    "bf_IgnoreDefense": 0.02041,
    "bf_IncreaseHealth": 0.03,
    "bf_IncreaseBlock": 0.10825,
    "bf_Recovery": 0.12863,
    "k_buff": 1.9241,
    "A_buff_dmg": 0.35641,
    "Pc_max": 0.53768,
    "cm0": 1.6,
    "cm_buff": 0.4,
    "k_crit": 0.81742,
    "cdr_max": 0.34368,
    "k_cdr": 0.35844,
    "crit_true": 0.24681,
    "mag_bypass": 0.10609,
    "ls_max": 0.15,
    "k_ls": 1.5,
    "infinity_basic": 0.5,
    "A_hp": 1.28171,
    "k_hp": 1.35617,
    "Mmax": 0.43053,
    "k_mit": 1.10388,
    "A_dt": 0.49693,
    "k_dt": 2.44285,
    "sh0": 0.12,
    "A_sh": 0.5,
    "k_sh": 1.5,
    "R_max": 0.02,
    "k_reg": 1.5,
    "w_it_hp_in": 1.0,
}
for _k, _v in _TUNED.items():
    if _k in PARAMS:
        PARAMS[_k] = (float(_v), min(PARAMS[_k][1], float(_v)), max(PARAMS[_k][2], float(_v)))
# <<< TUNED

# ------------------------------------------------------------------------------------------------------------------------
# Canales de los items
# ------------------------------------------------------------------------------------------------------------------------
CH = ["P", "C", "H", "M", "D", "R", "U", "Sh"]
_AFF = np.array([
    #  P    C    H    M    D    R    U    Sh
    [1.5, 0.5, 0.2, 0.0, 0.0, 0.3, 0.2, 0.0],   # arma
    [0.3, 0.0, 0.2, 0.8, 0.6, 0.0, 0.5, 1.0],   # escudo
    [0.3, 0.0, 1.0, 0.4, 0.2, 0.2, 0.2, 0.3],   # casco
    [0.5, 0.0, 0.6, 1.0, 1.0, 0.2, 0.0, 0.3],   # armadura
    [0.5, 0.0, 0.6, 0.8, 0.8, 0.3, 0.0, 0.3],   # pantalon
    [0.3, 0.8, 0.1, 0.2, 0.1, 0.6, 0.5, 0.0],   # guantes
    [0.2, 0.6, 0.2, 0.2, 0.1, 0.6, 0.6, 0.0],   # botas
    [0.5, 0.2, 0.6, 0.2, 0.2, 0.2, 0.4, 0.3],   # alas
    [0.4, 0.3, 0.4, 0.2, 0.2, 0.4, 0.3, 0.2],   # colgante
    [0.5, 0.4, 0.2, 0.1, 0.3, 0.3, 0.3, 0.1],   # anillo1
    [0.5, 0.4, 0.2, 0.1, 0.3, 0.3, 0.3, 0.1],   # anillo2
])
_NORM = (sim.SLOT_COST_WEIGHT[:, None] * _AFF).sum(axis=0)
_GROUPS = {0: ["P", "C"], 1: ["H", "M", "D", "Sh"], 2: ["R"], 3: ["U"]}   # ATAQUE, DEFENSA, RECURSOS, UTILIDAD


def S(x, k):
    """Saturacion normalizada: S(0)=0, S(1)=1, creciente y concava (sin tope duro). Para ganancias log."""
    return x * (1.0 + k) / (x + k)


def SB(x, k):
    """Saturacion acotada: x/(x+k) en [0,1). Para fracciones (mitigacion, critico, enfriamiento...): su maximo es la perilla *_max."""
    return x / (x + k)


def _items(b, p):
    t = b.tier / 3.0
    W = np.array([p["it_" + n] for n in NAMES_SLOT]) * sim.SLOT_COST_WEIGHT
    rm = np.full((4, len(CH)), p["role_base"])
    for r, chs in _GROUPS.items():
        for c in chs:
            rm[r, CH.index(c)] = 1.0
    out = np.empty((b.n, len(CH)))
    for c in range(len(CH)):
        out[:, c] = (t * rm[b.role, c] * (W * _AFF[:, c])[None, :]).sum(axis=1) / _NORM[c]
    return out


_CACHE = {"b": None, "p": None, "v": None}


# Tabla del kit SIN reduccion de enfriamiento por (nivel, clase, rango de skills): se calcula una sola vez con kits.rotation y se interpola (evita 8 llamadas por evaluacion)
_RGRID = np.arange(1.0, 5.25, 0.1)


def _build_table():
    import types
    nL, nR = 30, len(_RGRID)
    LL, RR = np.meshgrid(np.arange(1, nL + 1), _RGRID, indexing="ij")
    tab = np.zeros((7, nL, nR))
    for c in range(7):
        fake = types.SimpleNamespace(n=LL.size, cls=np.full(LL.size, c), rank=RR.ravel(), L=LL.ravel())
        tab[c] = kits.rotation(fake, 0.0)["steady"].reshape(nL, nR)
    return tab


_TAB = _build_table()
_TAB_MEAN = _TAB.mean(axis=0)


def _steady_table(b, tab_cls=True):
    """steady (sin cdr) de la clase propia y media de las 7 clases al mismo nivel y rango (interpolacion lineal en el rango)."""
    u = (b.rank - 1.0) / 0.1
    i0 = np.floor(u).astype(np.int64)
    w = u - i0
    li = b.L.astype(np.int64) - 1
    own = _TAB[b.cls, li, i0] * (1 - w) + _TAB[b.cls, li, i0 + 1] * w
    mean = _TAB_MEAN[li, i0] * (1 - w) + _TAB_MEAN[li, i0 + 1] * w
    return own, mean


def _rot(b, cdr, cls=None, rank=None):
    bb = dataclasses.replace(b, cls=b.cls if cls is None else np.full(b.n, cls), rank=b.rank if rank is None else rank)
    return kits.rotation(bb, 0.0, cdr=cdr)


def _calc(b, p):
    if _CACHE["b"] is b and _CACHE["p"] is p:
        return _CACHE["v"]
    n = b.n
    ar = np.arange(n)
    xl = (b.L.astype(float) - 1.0) / 29.0
    x = b.stats / 30000.0
    t = b.tree / 100.0
    cls = b.cls
    xi = _items(b, p)
    cmd = 1.0 + p["cmd_amp"] * S(x[:, 4], p["k_cmd"])
    bf = S(b.buff / 5.0, p["k_buff"]) * cmd[:, None]
    for i, nm in enumerate(NAMES_BUFF):
        bf[:, i] = bf[:, i] * p["bf_" + nm]
    pt = S(b.pet, p["k_pet"]) * cmd[:, None]
    ci, cj = sim.CLS_PRIMARY[cls], sim.CLS_SECONDARY[cls]
    v = {}

    # ---------------- enfriamiento (cdr): items utilidad, mascota veloz, arbol mana, AGI -----------------------------
    x_cdr = p["w_it_cdr"] * xi[:, 6] + p["pet_veloz"] * pt[:, 3] + p["tr_mana"] * t[:, 5] + p["w_agi_cdr"] * x[:, 1]
    cdr = p["cdr_max"] * SB(x_cdr, p["k_cdr"])
    own0, mean_s = _steady_table(b)
    rot = _rot(b, cdr)
    ks = p["kit_strength"]
    rotation = (own0 ** ks) * (mean_s ** (1.0 - ks)) / 100.0
    haste = rot["steady"] / np.maximum(own0, 1e-9)

    # ---------------- nivel ---------------------------------------------------------------------------------------------
    lv_lin = 1.0 + p["dps_lvl_lin"] * xl
    lv_geo = np.exp(p["dps_lvl_geo"] * xl)

    # ---------------- identidad de clase ------------------------------------------------------------------------------
    idf_all = np.array([p["id_dps_" + c] * sim.CLS_DMG_TILT[i] ** p["dmg_tilt_pow"] for i, c in enumerate(sim.CLASS_NAMES)])
    ident = (idf_all / idf_all.min())[cls]

    # ---------------- cubos de dano ------------------------------------------------------------------------------------
    dw = np.array([p["stat_dmg_" + nm] for nm in NAMES_STAT])
    u_dmg = dw[ci] * x[ar, ci] + p["sec_w"] * dw[cj] * x[ar, cj]
    c_stats = np.exp(p["A_stat_dmg"] * S(u_dmg, p["k_stat_dmg"]))
    c_tree = np.exp(p["tr_ofensa"] * S(t[:, 0], p["k_tree"]))
    c_items = np.exp(p["A_it_pow"] * S(xi[:, 0], p["k_item"]))
    c_pet = np.exp(p["pet_ataque"] * S(b.pet[:, 0], p["k_pet"]) * cmd)
    x_bd = bf[:, 1] + bf[:, 6] + bf[:, 10]
    c_buff = np.exp(p["A_buff_dmg"] * S(x_bd, 1.0))
    basic_share = rot["basic_dps"] / np.maximum(rot["steady"], 1e-9)
    c_inf = 1.0 + p["infinity_basic"] * bf[:, 7] * basic_share
    # critico
    x_c = p["w_agi_crit"] * x[:, 1] + p["tr_critico"] * t[:, 2] + p["w_it_crit"] * xi[:, 1] + bf[:, 8]
    pc = p["Pc_max"] * SB(x_c, p["k_crit"])
    cm = p["cm0"] + p["cm_buff"] * bf[:, 8]
    c_crit = 1.0 + pc * (cm - 1.0)
    # dano verdadero (ignora defensa + espinas)
    true_frac = bf[:, 11] + bf[:, 9]
    f_shift = p["crit_true"] * pc                     # parte del dano que pasa directo (los criticos ignoran mitigacion): se desplaza, no se suma

    unit = p["dps_unit"]
    dps_main = unit * lv_lin * lv_geo * rotation * 100.0 * haste * ident * c_stats * c_tree * c_items * c_pet * c_buff * c_inf * c_crit
    # el dmg tilt ya esta dentro de ident (se divide por el minimo; constante global absorbida por unit)
    own_r1 = _TAB[cls, b.L.astype(np.int64) - 1, 0]
    rank_gain = own0 / own_r1
    v["factors"] = {
        "nivel_base": lv_lin * lv_geo, "rango_de_skills": rank_gain, "kit_de_skills": rotation / rank_gain, "identidad_de_clase": ident,
        "enfriamiento": haste, "cubo_stats": c_stats, "cubo_arbol": c_tree, "cubo_items": c_items,
        "cubo_mascota+buffs+verdadero": c_pet * c_buff * c_inf * (1.0 + true_frac), "critico": c_crit,
    }
    sus_x = p["w_ene_mana"] * x[:, 2] + p["tr_mana"] * t[:, 5] + p["w_it_mana"] * xi[:, 5]
    sustain = rot["sustain"] + (1.0 - rot["sustain"]) * SB(sus_x, 1.5)
    ls_x = xi[:, 5] + 0.5 * xi[:, 7]
    v["attack"] = {
        "dps_phys": dps_main * (1.0 - f_shift) * (1.0 - b.mag), "dps_mag": dps_main * (1.0 - f_shift) * b.mag, "true_dps": dps_main * (true_frac + f_shift),
        "lifesteal": p["ls_max"] * SB(ls_x, p["k_ls"]),
        "burst_mult": rot["burst_mult"], "sustain": sustain, "aoe_share": rot["aoe_share"],
    }

    # ---------------- defensa ------------------------------------------------------------------------------------------
    hid_all = np.array([p["id_hp_" + c] * sim.CLS_HP_TILT[i] ** p["hp_tilt_pow"] for i, c in enumerate(sim.CLASS_NAMES)])
    base_hp = p["hp_unit"] * (1.0 + p["hp_lvl_lin"] * xl) * np.exp(p["hp_lvl_geo"] * xl) * hid_all[cls]
    x_hp = (p["w_vit_hp"] * x[:, 3] + p["tr_vida"] * t[:, 4] + p["w_it_hp_in"] * xi[:, 2] + p["pet_vital"] * pt[:, 2]
            + bf[:, 3] + bf[:, 12] + bf[:, 10])
    hp = base_hp * np.exp(p["A_hp"] * S(x_hp, p["k_hp"]))
    x_m = p["w_vit_mit"] * x[:, 3] + p["w_str_mit"] * x[:, 0] + p["tr_defensa"] * t[:, 1] + p["w_it_mit"] * xi[:, 3] + bf[:, 5]
    mit = p["Mmax"] * SB(x_m, p["k_mit"])
    x_dt = p["tr_durabilidad"] * t[:, 3] + p["w_it_dur"] * xi[:, 4] + p["pet_guardia"] * pt[:, 1] + bf[:, 2] + bf[:, 0] + bf[:, 13]
    dt = np.exp(-p["A_dt"] * S(x_dt, p["k_dt"]))
    x_sh = p["w_it_sh"] * xi[:, 7] + bf[:, 0] + bf[:, 2]
    shield = hp * (p["sh0"] + p["A_sh"] * S(x_sh, p["k_sh"]))
    x_r = p["w_it_reg"] * xi[:, 5] + bf[:, 4] + bf[:, 14] + 0.5 * pt[:, 2] + p["w_vit_reg"] * x[:, 3]
    regen = hp * p["R_max"] * S(x_r, p["k_reg"])
    v["defense"] = {"hp": hp, "shield": shield, "mit_phys": mit, "mit_mag": mit * (1.0 - p["mag_bypass"]), "dmg_taken_mult": dt, "regen": regen}
    _CACHE.update(b=b, p=p, v=v)
    return v


def attack(b, p):
    return _calc(b, p)["attack"]


def defense(b, p):
    return _calc(b, p)["defense"]


def decompose(b, p):
    return {"mode": "mult", "factors": _calc(b, p)["factors"]}
