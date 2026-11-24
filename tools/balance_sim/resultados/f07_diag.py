import sys, json
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import sim
import f07_saturacion as F
mod = sim.load_formula("f07_saturacion")
p = sim.default_params(mod)
if len(sys.argv) > 1:
    d = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8")); p.update(d.get("params", d))
rng = np.random.default_rng(3)
Rt = sim.Trajectory(6000, sim.COVER_LEVELS, rng, reference=True)
for k, L in enumerate(sim.COVER_LEVELS):
    R = Rt.at(k)
    c = F._get(R, p)["c"]
    print("L", L, {kk: round(float(np.median(v)), 2) for kk, v in c.items()}, "stats", np.round(np.median(R.stats, axis=0)/3e4, 2), "tier med", np.median(R.tier, axis=0))
    bo0, bd0 = sim.outputs(mod, R, p)
    b_off, b_def = sim.off_power(bo0, L), sim.ehp(R, bd0)
    for e in sim.ELEMENTS[:5] + sim.ELEMENTS[11:13]:
        Rb = sim.apply_bump(R, e)
        bo1, bd1 = sim.outputs(mod, Rb, p)
        ro = sim.off_power(bo1, L) / b_off - 1; rd = sim.ehp(Rb, bd1) / b_def - 1
        print("   ", e[2], [round(100*float(np.median(ro[R.cls == c_])), 1) for c_ in range(7)], [round(100*float(np.median(rd[R.cls == c_])), 1) for c_ in range(7)])
