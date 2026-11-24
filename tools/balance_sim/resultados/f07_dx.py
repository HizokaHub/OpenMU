import sys, json
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import numpy as np, sim
mod = sim.load_formula("f07_saturacion")
p = sim.default_params(mod)
if len(sys.argv)>1: p.update(json.loads(sys.argv[1]))
rng = np.random.default_rng(3)
levels=[1,2,3,5,10,16,24,30]
traj = sim.Trajectory(4000, levels, rng)
for k,L in enumerate(levels):
    A = traj.at(k)
    ao, do = sim.outputs(mod, A, p)
    op = sim.off_power(ao, L); e = sim.ehp(A, do)
    m = (A.label==2)
    mit=0.5*do['mit_phys']+0.5*do['mit_mag']
    # tiempo para matar: ehp_mediana / dps mediano
    print(L, "dps %.0f hp %.0f ehp %.0f ratio ehp/dps %.1fs | mit %.2f dtm %.2f regen/s %.1f ls %.3f sh %.0f burst %.2f" % (np.median(op), np.median(do['hp']), np.median(e), np.median(e)/np.median(op), np.median(mit), np.median(do['dmg_taken_mult']), np.median(do['regen']), np.median(ao['lifesteal']), np.median(do['shield']), np.median(ao['burst_mult'])))
