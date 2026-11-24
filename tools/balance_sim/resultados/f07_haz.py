import sys, json
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import sim, f07_saturacion as F
mod = sim.load_formula("f07_saturacion"); p = sim.default_params(mod)
if len(sys.argv)>1: d=json.loads(sys.argv[1]); p.update(d)
if len(sys.argv)>2: d=json.loads(Path(sys.argv[2]).read_text()); p.update(d.get("params",d))
rng = np.random.default_rng(5); LV=[8,16,30]
traj = sim.Trajectory(12000, LV, rng)
for k,L in enumerate(LV):
    A=traj.at(k); kk=F._get(A,p); H=kk["acc"].H
    print("L",L)
    for ch in ("POT","CRI","VID","MIT","RED"):
        ci=F.CI[ch]
        for lab,nm in ((0,"dano"),(1,"tanque")):
            m=A.label==lab
            print("  %s %-6s C=%.2f  "%(ch,nm,np.median(kk["c"][ch][m])), " ".join("%s=%.2f"%(g[:8],np.median(H[F.GI[g],ci][m])) for g in F.GROUPS))
