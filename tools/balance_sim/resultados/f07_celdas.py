"""Diagnostico/ajuste de f07: win rate de cada celda (clase x arquetipo) contra el campo en varios niveles.
Uso: python -I -X utf8 resultados/f07_celdas.py [n] [params.json] [--fit iteraciones salida.json]"""
import sys, json
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import sim
mod = sim.load_formula("f07_saturacion")
p = sim.default_params(mod)
argv = sys.argv[1:]
fit = 0; out = None
if "--fit" in argv:
    i = argv.index("--fit"); fit = int(argv[i + 1]); out = argv[i + 2]; argv = argv[:i]
n = int(argv[0]) if argv else 12000
if len(argv) > 1:
    d = json.loads(Path(argv[1]).read_text(encoding="utf-8") if Path(argv[1]).exists() else argv[1]); p.update(d.get("params", d))
LV = [4, 8, 12, 16, 20, 25, 30]
rng = np.random.default_rng(5)
traj = sim.Trajectory(n, LV, rng)
cells_all = sim.cell_of(traj.cls, traj.label)
members = [np.where(cells_all == c)[0] for c in range(21)]
def run(p, verbose):
    cm = np.zeros((len(LV), 21)); fm = []
    r2 = np.random.default_rng(9)
    for k, L in enumerate(LV):
        A = traj.at(k); ao, do = sim.outputs(mod, A, p)
        IA, IB, CI, CJ = [], [], [], []
        for i in range(21):
            for j in range(21):
                IA.append(r2.choice(members[i], 150)); IB.append(r2.choice(members[j], 150)); CI.append(np.full(150, i)); CJ.append(np.full(150, j))
        IA, IB, CI, CJ = map(np.concatenate, (IA, IB, CI, CJ))
        res = sim.fight({kk: v[IA] for kk, v in ao.items()}, {kk: v[IA] for kk, v in do.items()}, {kk: v[IB] for kk, v in ao.items()}, {kk: v[IB] for kk, v in do.items()}, A.cls[IA], A.cls[IB], r2)
        W = np.bincount(CI * 21 + CJ, weights=res["a_wins"], minlength=441).reshape(21, 21) / 150.0
        cm[k] = W.mean(axis=1)
        off = ~np.eye(21, dtype=bool); v = W[off]
        fm.append((np.mean((v >= .4) & (v <= .6)), np.mean((v < .25) | (v > .75))))
        if verbose:
            print("L%2d band %.2f ext %.2f | celdas(dano,tanque,mixto) por clase:" % (L, fm[-1][0], fm[-1][1]), " ".join("%s:%s" % (sim.CLASS_NAMES[c], "/".join("%.2f" % cm[k, c * 3 + a] for a in range(3))) for c in range(7)))
    return cm, fm
if not fit:
    run(p, True)
else:
    for it in range(fit):
        cm, fm = run(p, False)
        crow = cm.reshape(len(LV), 7, 3).mean(axis=(0, 2))       # promedio por clase
        arch = cm.reshape(len(LV), 7, 3).mean(axis=(0, 1))       # por arquetipo
        print("it", it, "clase", np.round(crow, 3), "arq", np.round(arch, 3), "band", np.round(np.mean([f[0] for f in fm]), 3), flush=True)
        for c, nm in enumerate(sim.CLASS_NAMES):
            f = (0.5 / crow[c]) ** 0.5
            p["dmg_" + nm] = float(p["dmg_" + nm] * f ** 0.6); p["hp_" + nm] = float(p["hp_" + nm] * f ** 0.4)
    Path(out).write_text(json.dumps(p, indent=1), encoding="utf-8")
    run(p, True)
