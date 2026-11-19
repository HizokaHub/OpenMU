"""Prueba de PERILLAS: sube y baja cada parametro de la formula un 15 % y mide (a) cuanto cambia la puntuacion y (b) cuanto cambia el dano y la vida efectiva
(medianas, nivel 30). Una buena formula tiene perillas que mueven poco el puntaje (robusta) pero SI mueven el dano o la vida (sirven para ajustar).
Uso: python -I -X utf8 robust.py <modulo> [--params archivo.json] [--n 10000] [--out perillas.json]"""
import argparse, json, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
import sim

ap = argparse.ArgumentParser()
ap.add_argument("modulo")
ap.add_argument("--params", default=None)
ap.add_argument("--n", type=int, default=10_000)
ap.add_argument("--out", default=None)
ap.add_argument("--pct", type=float, default=0.15)
a = ap.parse_args()
mod = sim.load_formula(a.modulo)
p0 = sim.default_params(mod)
if a.params:
    src = Path(a.params).read_text(encoding="utf-8") if Path(a.params).exists() else a.params
    d = json.loads(src)
    p0.update(d.get("params", d))
rng = np.random.default_rng(5)
traj = sim.Trajectory(6000, [30], rng)
B = traj.at(0)


def power(p):
    ao, do = sim.outputs(mod, B, p)
    o, e = sim.off_power(ao, 30), sim.ehp(B, do)
    return np.array([np.median(o[B.cls == c]) for c in range(7)]), np.array([np.median(e[B.cls == c]) for c in range(7)])


base_score = sim.evaluate(mod, p0, n=a.n, seed=21, detail=False, quick=True)["score"]
base_off, base_ehp = power(p0)
rows = {}
for k, (init, lo, hi) in mod.PARAMS.items():
    row = {}
    for sign, tag in ((1, "+"), (-1, "-")):
        p = dict(p0)
        v = p0[k] * (1 + sign * a.pct) if abs(p0[k]) > 1e-9 else sign * a.pct * (hi - lo) * 0.1
        p[k] = float(np.clip(v, lo, hi))
        sc = sim.evaluate(mod, p, n=a.n, seed=21, detail=False, quick=True)["score"]
        o, e = power(p)
        do_, de_ = 100 * (o / base_off - 1), 100 * (e / base_ehp - 1)
        row[tag] = {"d_score": round(sc - base_score, 2), "d_dano_%_clase_mas_afectada": round(float(do_[np.argmax(np.abs(do_))]), 2), "d_vidaefectiva_%_clase_mas_afectada": round(float(de_[np.argmax(np.abs(de_))]), 2)}
    rows[k] = row
drops = [min(r["+"]["d_score"], r["-"]["d_score"]) for r in rows.values()]
moves = [max(abs(r["+"]["d_dano_%_clase_mas_afectada"]), abs(r["+"]["d_vidaefectiva_%_clase_mas_afectada"])) for r in rows.values()]
res = {"puntaje_base": base_score, "peor_caida_de_puntaje": round(min(drops), 2), "caida_media": round(float(np.mean(np.minimum(drops, 0))), 2),
       "perillas_que_no_mueven_nada(<0.1%)": [k for k, m in zip(rows, moves) if m < 0.1], "perillas": rows}
txt = json.dumps(res, indent=1, ensure_ascii=False)
if a.out:
    Path(a.out).write_text(txt, encoding="utf-8")
print(json.dumps({k: v for k, v in res.items() if k != "perillas"}, indent=1, ensure_ascii=False))
