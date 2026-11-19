"""Prueba de estres de personajes individuales: python -I -X utf8 stress.py <modulo> <params.json> [n]
Cada personaje (con su historia) pelea contra 100 rivales al azar de su mismo nivel; se mide su winrate. Meta: casi nadie imbatible (>80 %) ni inutil (<20 %),
y los extremos son builds raros/mal repartidos, no arquetipos normales."""
import sys, json
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import numpy as np, sim
mod = sim.load_formula(sys.argv[1]); p = sim.default_params(mod)
if sys.argv[2] != "-":
    d = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8")); p.update(d.get("params", d))
n = int(sys.argv[3]) if len(sys.argv) > 3 else 6000
rng = np.random.default_rng(21)
levels = [3, 8, 16, 24, 30]
tr = sim.Trajectory(n, levels, rng)
for k, L in enumerate(levels):
    A = tr.at(k); ao, do = sim.outputs(mod, A, p)
    R = 60
    wins = np.zeros(n)
    for r in range(R):
        ib = rng.integers(0, n, n)
        f = lambda dd, ix: {kk: v[ix] for kk, v in dd.items()}
        wins += sim.fight(ao, do, f(ao, ib), f(do, ib), A.cls, A.cls[ib], rng)["a_wins"]
    wr = wins / R
    norm = A.label >= 0
    print(f"nivel {L:2d}: winrate medio {wr.mean():.3f} | <20%: {np.mean(wr<.2)*100:4.1f}% (normales {np.mean(wr[norm]<.2)*100:4.1f}%) | >80%: {np.mean(wr>.8)*100:4.1f}% (normales {np.mean(wr[norm]>.8)*100:4.1f}%) | p5 {np.percentile(wr,5):.2f} p95 {np.percentile(wr,95):.2f}")
