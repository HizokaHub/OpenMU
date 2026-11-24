"""Calibra los techos (techo_*) de f07 para que cada elemento mueva el dano o la vida efectiva un objetivo (C1) y los items sean proporcionales a su coste (C9).
Uso: python -I -X utf8 resultados/f07_calibra.py [iteraciones] [salida.json] [--start params.json]"""
import sys, json
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import sim

mod = sim.load_formula("f07_saturacion")
p = sim.default_params(mod)
it = int(sys.argv[1]) if len(sys.argv) > 1 else 6
out = sys.argv[2] if len(sys.argv) > 2 else "resultados/f07_calibrado.json"
if "--start" in sys.argv:
    d = json.loads(Path(sys.argv[sys.argv.index("--start") + 1]).read_text(encoding="utf-8"))
    p.update(d.get("params", d))
TARGET = {}
for e in sim.ELEMENTS:
    kind, i, name = e
    if kind == "stat":
        TARGET[name] = 3.4
    elif kind == "tree":
        TARGET[name] = 2.8 if name != "arbol_mana" else 2.4
    elif kind == "item":
        TARGET[name] = 2.9 * float(sim.SLOT_COST_WEIGHT[i])
    elif kind == "pet":
        TARGET[name] = 3.0
    else:
        TARGET[name] = 3.0
START = dict(p)
rng = np.random.default_rng(3)
Rt = sim.Trajectory(12000, sim.COVER_LEVELS, rng, reference=True)
for r in range(it):
    dd = {e[2]: [] for e in sim.ELEMENTS}
    for k, L in enumerate(sim.COVER_LEVELS):
        R = Rt.at(k)
        bo0, bd0 = sim.outputs(mod, R, p)
        b_off, b_def = sim.off_power(bo0, L), sim.ehp(R, bd0)
        for e in sim.ELEMENTS:
            Rb = sim.apply_bump(R, e)
            bo1, bd1 = sim.outputs(mod, Rb, p)
            ro = sim.off_power(bo1, L) / np.maximum(b_off, 1e-9) - 1.0
            rd = sim.ehp(Rb, bd1) / np.maximum(b_def, 1e-9) - 1.0
            po = [float(np.median(ro[R.cls == c])) for c in range(7)]
            pd = [float(np.median(rd[R.cls == c])) for c in range(7)]
            dd[e[2]].append(max(max(abs(x) for x in po), max(abs(x) for x in pd)))
    worst = 0
    for e in sim.ELEMENTS:
        name = e[2]
        m = 100 * float(np.median(dd[name]))
        key = "techo_" + name
        if e[0] == "item":
            tgt = 2.7 * float(sim.SLOT_COST_WEIGHT[e[1]])
        elif m < 2.2:
            tgt = 2.8
        elif m > 6.5:
            tgt = 5.5
        else:
            continue
        ratio = tgt / max(m, 0.05)
        worst = max(worst, abs(np.log(ratio)))
        lo, hi = START[key] * 0.4, min(0.9, START[key] * 3.5)
        p[key] = float(np.clip(p[key] * ratio ** 0.5, lo, hi))
        print("   ajuste", name, "medido %.2f -> objetivo %.2f techo %.3f" % (m, tgt, p[key]))
    print("iter", r, "peor log-ratio %.3f" % worst, flush=True)
Path(out).write_text(json.dumps(p, indent=1), encoding="utf-8")
print("guardado", out)
