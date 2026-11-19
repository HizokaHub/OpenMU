"""Analisis de celdas clase x arquetipo en un nivel: python -I -X utf8 cells.py <modulo> <params.json> <nivel> [n]"""
import sys, json
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import numpy as np, sim
mod = sim.load_formula(sys.argv[1]); p = sim.default_params(mod)
if sys.argv[2] != "-":
    d = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8")); p.update(d.get("params", d))
L = int(sys.argv[3]); n = int(sys.argv[4]) if len(sys.argv) > 4 else 30000
rng = np.random.default_rng(3)
tr = sim.Trajectory(n, [L], rng); A = tr.at(0)
cells = sim.cell_of(A.cls, A.label)
members = [np.where(cells == c)[0] for c in range(21)]
ao, do = sim.outputs(mod, A, p)
W = np.full((21, 21), np.nan); S = np.zeros((21, 21)); R = np.zeros((21, 21))
m = 600
for i in range(21):
    for j in range(21):
        ia, ib = rng.choice(members[i], m), rng.choice(members[j], m)
        f = lambda dd, ix: {k: v[ix] for k, v in dd.items()}
        res = sim.fight(f(ao, ia), f(do, ia), f(ao, ib), f(do, ib), A.cls[ia], A.cls[ib], rng)
        W[i, j] = res["a_wins"].mean()
names = [f"{sim.CLASS_NAMES[c]}-{'DTM'[a]}" for c in range(7) for a in range(3)]
np.set_printoptions(linewidth=250, precision=2, suppress=True)
off = ~np.eye(21, dtype=bool)
print("en banda 40-60:", np.mean(((W >= .4) & (W <= .6))[off]), " 30-70:", np.mean(((W >= .3) & (W <= .7))[off]))
arch = np.nanmean(W.reshape(7, 3, 7, 3), axis=(0, 2))
print("arquetipo (filas D,T,M) vs (D,T,M) promedio:\n", arch)
print("peores celdas (winrate medio contra el campo):", sorted(zip(np.round(np.nanmean(W, axis=1), 2), names))[:3], sorted(zip(np.round(np.nanmean(W, axis=1), 2), names))[-3:])
bad = [(round(W[i, j], 2), names[i], names[j]) for i in range(21) for j in range(21) if i < j and (W[i, j] < .3 or W[i, j] > .7)]
print("cruces fuera de 30-70:", len(bad)); print(sorted(bad)[:12], sorted(bad)[-12:])
