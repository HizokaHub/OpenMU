"""Kits reales por clase (las 6 skills por defecto del MOBA, de kits.json) y rotacion con enfriamientos y mana.

rotation(b, frac, cdr=0.0, mana_regen_bonus=0.0, basic_aps=BASIC_APS) -> dict de arrays (N,):
  skill_dps   dano por segundo de las skills en rotacion continua (sin escala de nivel / tilt / crit; ya con la escala por stat: dano x (1 + 2,5 x frac x escala_skill))
  basic_dps   dano por segundo del ataque basico en los huecos (45 + 4(L-1)) x 1,5 x (1 + 0,6 x 2,5 x frac) x aps x (1 - tiempo lanzando)
  burst_mult  dano de los primeros 6 s dividido entre el sostenido (se lanzan todas las skills al empezar)
  sustain     fraccion del dano que el mana permite sostener (1 = sin limite): regen / gasto, con el regen real del servidor
  aoe_share   parte del dano de skills que sale de skills de area (para el 3v3)
  mana_cost_ps, regen_ps, busy
`frac` es la fraccion de la mezcla (0..1) que usa la formula (en el juego real 0,45 stats + 0,20 arbol + 0,35 items); `cdr` reduccion de enfriamiento 0..0,9.
Supuestos MIOS (no medidos): 1,25 basicos por segundo y 0,5 s de animacion por skill lanzada."""
import json
from pathlib import Path
import numpy as np
import sim

CLS = sim.CLASS_NAMES
BASIC_APS = 1.25
CAST_TIME = 0.5
_K = json.loads((Path(__file__).resolve().parent / "kits.json").read_text(encoding="utf-8"))
NS = 6


def _table():
    f = lambda key: np.array([[s[key] for s in _K[c]] + [0.0] * (NS - len(_K[c])) for c in CLS], dtype=float)
    t = {k: f(k) for k in ("dano_r1", "dano_r5", "escala", "cd_r1", "cd_r5", "mana")}
    t["area"] = np.array([[float(s["area"]) for s in _K[c]] + [0.0] * (NS - len(_K[c])) for c in CLS])
    t["valid"] = np.array([[1.0] * len(_K[c]) + [0.0] * (NS - len(_K[c])) for c in CLS])
    return t


T = _table()


def rotation(b, frac, cdr=0.0, mana_regen_bonus=0.0, basic_aps=BASIC_APS):
    n = b.n
    c = b.cls
    r = np.clip(b.rank, 1.0, 5.0)[:, None]
    u = (r - 1.0) / 4.0
    d = T["dano_r1"][c] + (T["dano_r5"][c] - T["dano_r1"][c]) * u
    cd = (T["cd_r1"][c] + (T["cd_r5"][c] - T["cd_r1"][c]) * u) * (1.0 - np.asarray(cdr)[..., None] if np.ndim(cdr) else 1.0 - cdr)
    cd = np.maximum(cd, 0.3)
    sc = T["escala"][c] * (1.0 + 0.6 * u)
    valid = T["valid"][c]
    fr = np.asarray(frac, dtype=float)
    fr = fr[:, None] if fr.ndim else fr
    d_eff = d * (1.0 + 2.5 * fr * sc) * valid
    rate = valid / cd                                       # lanzamientos por segundo de cada skill
    skill_dps = (d_eff * rate).sum(axis=1)
    busy = np.minimum(0.9, (rate * CAST_TIME).sum(axis=1))
    basic_hit = (45.0 + 4.0 * (np.clip(b.L, 1, 30) - 1)) * 1.5 * (1.0 + 0.6 * 2.5 * fr.reshape(-1))
    basic_dps = basic_hit * basic_aps * (1.0 - busy)
    steady = skill_dps + basic_dps
    # primeros 6 s: cada skill se lanza al empezar y otra vez cada cd
    burst_casts = (1.0 + np.floor(6.0 / cd)) * valid
    burst = ((d_eff * burst_casts).sum(axis=1) + basic_dps * 6.0) / 6.0
    cost_ps = (T["mana"][c] * rate).sum(axis=1)
    pool = sim.ref_mana(b.L)
    max_cost = (T["mana"][c] * valid).max(axis=1)
    regen = np.clip(max_cost / 5.0, pool * 0.04, pool * 0.12) * (1.0 + mana_regen_bonus)
    sustain = np.clip(regen / np.maximum(cost_ps, 1e-9), 0.0, 1.0)
    # el mana que falta recorta solo la parte de skills
    sus_total = (skill_dps * sustain + basic_dps) / np.maximum(steady, 1e-9)
    aoe = (d_eff * rate * T["area"][c]).sum(axis=1) / np.maximum(skill_dps, 1e-9)
    return {"skill_dps": skill_dps, "basic_dps": basic_dps, "steady": steady, "burst_mult": burst / np.maximum(steady, 1e-9), "sustain": sus_total,
            "aoe_share": aoe * skill_dps / np.maximum(steady, 1e-9), "mana_cost_ps": cost_ps, "regen_ps": regen, "busy": busy}
