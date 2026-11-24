"""Escala todos los techos de un json base: f07_scale.py base.json s out.json [extra json]"""
import sys, json
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import sim
mod = sim.load_formula("f07_saturacion"); p = sim.default_params(mod)
if sys.argv[1] != "-": 
    d=json.loads(Path(sys.argv[1]).read_text()); p.update(d.get("params",d))
s=float(sys.argv[2])
for k in p:
    if k.startswith("techo_"): p[k]=p[k]*s
if len(sys.argv)>4: p.update(json.loads(sys.argv[4]))
Path(sys.argv[3]).write_text(json.dumps(p,indent=1))
