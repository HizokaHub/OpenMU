"""Ajuste de parametros de una formula con una estrategia evolutiva simple.
Uso: python -I -X utf8 tune.py <modulo> [--iters 120] [--pop 10] [--n 25000] [--out mejores.json] [--start params.json]
Cada evaluacion usa la MISMA semilla (comparacion justa). Al terminar guarda los mejores parametros y su puntuacion."""
import argparse, json, sys, time
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
import sim

ap = argparse.ArgumentParser()
ap.add_argument("modulo")
ap.add_argument("--iters", type=int, default=40)
ap.add_argument("--pop", type=int, default=8)
ap.add_argument("--n", type=int, default=12_000)
ap.add_argument("--out", default=None)
ap.add_argument("--start", default=None)
ap.add_argument("--seed", type=int, default=7)
a = ap.parse_args()
mod = sim.load_formula(a.modulo)
keys = list(mod.PARAMS.keys())
lo = np.array([mod.PARAMS[k][1] for k in keys], float)
hi = np.array([mod.PARAMS[k][2] for k in keys], float)
x = np.array([mod.PARAMS[k][0] for k in keys], float)
if a.start:
    st = json.loads(Path(a.start).read_text(encoding="utf-8"))
    x = np.array([st.get(k, x[i]) for i, k in enumerate(keys)], float)
rng = np.random.default_rng(a.seed)


def score(v):
    p = dict(zip(keys, map(float, v)))
    try:
        return sim.evaluate(mod, p, n=a.n, seed=11, detail=False, quick=True)["score"]
    except Exception as e:  # formula rota para esos parametros
        return -1.0


best, best_s = x.copy(), score(x)
sigma = 0.25
t0 = time.time()
print(f"inicio score={best_s:.2f}", flush=True)
for it in range(a.iters):
    cands = []
    for _ in range(a.pop):
        step = rng.normal(0, sigma, len(keys)) * (hi - lo)
        mask = rng.random(len(keys)) < max(0.3, 3.0 / len(keys))
        c = np.clip(best + step * mask, lo, hi)
        cands.append(c)
    scores = [score(c) for c in cands]
    j = int(np.argmax(scores))
    if scores[j] > best_s:
        best, best_s = cands[j], scores[j]
        sigma = min(0.4, sigma * 1.15)
    else:
        sigma = max(0.02, sigma * 0.93)
    if it % 10 == 0:
        print(f"it {it} score={best_s:.2f} sigma={sigma:.3f} t={time.time()-t0:.0f}s", flush=True)
res = {"score_rapido": round(best_s, 2), "params": dict(zip(keys, map(float, best)))}
if a.out:
    Path(a.out).write_text(json.dumps(res, indent=1), encoding="utf-8")
print(json.dumps(res, indent=1))
