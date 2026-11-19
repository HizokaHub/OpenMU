"""Uso: python -I -X utf8 run.py <modulo_formula> [--n 100000] [--params '{"k":v}'] [--seed 1] [--out informe.json]"""
import argparse, json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import sim

ap = argparse.ArgumentParser()
ap.add_argument("modulo")
ap.add_argument("--n", type=int, default=100_000)
ap.add_argument("--params", default=None)
ap.add_argument("--seed", type=int, default=1)
ap.add_argument("--out", default=None)
ap.add_argument("--quick", action="store_true")
a = ap.parse_args()
mod = sim.load_formula(a.modulo)
p = sim.default_params(mod)
if a.params:
    src = Path(a.params).read_text(encoding="utf-8") if Path(a.params).exists() else a.params
    p.update(json.loads(src))
t0 = time.time()
rep = sim.evaluate(mod, p, n=a.n, seed=a.seed, quick=a.quick)
rep["segundos"] = round(time.time() - t0, 1)
rep["params"] = p
txt = json.dumps(rep, indent=1, ensure_ascii=False)
if a.out:
    Path(a.out).write_text(txt, encoding="utf-8")
print(txt)
