import sys, json
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import sim
import f07_saturacion as F
mod = sim.load_formula("f07_saturacion")
p = sim.default_params(mod)
if len(sys.argv) > 1:
    a1 = sys.argv[1]; d = json.loads(Path(a1).read_text(encoding="utf-8") if Path(a1).exists() else a1); p.update(d.get("params", d))
rng = np.random.default_rng(5)
LV = [8, 16, 30]
traj = sim.Trajectory(20000, LV, rng)
for k, L in enumerate(LV):
    A = traj.at(k); ao, do = sim.outputs(mod, A, p)
    op, ep = sim.off_power(ao, L), sim.ehp(A, do)
    kk = F._get(A, p)
    print("L", L)
    for lab, nm in ((0, "dano"), (1, "tanque"), (2, "mixto")):
        m = A.label == lab
        print("  %-6s n=%5d dps %.0f ehp %.0f prod %.3g | POT %.2f CRI %.2f VID %.2f MIT %.2f RED %.2f SOS %.2f" % (nm, m.sum(), np.median(op[m]), np.median(ep[m]), np.median(op[m]) * np.median(ep[m]),
              *[np.median(kk["c"][c][m]) for c in ("POT", "CRI", "VID", "MIT", "RED", "SOS")]))
    m = A.label == 1
    print("  tank stats", np.round(np.median(A.stats[m], axis=0) / 3e4, 2), "dano stats", np.round(np.median(A.stats[A.label == 0], axis=0) / 3e4, 2), "mixto", np.round(np.median(A.stats[A.label == 2], axis=0) / 3e4, 2))
