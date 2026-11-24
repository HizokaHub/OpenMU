"""Prueba variantes: python -I -X utf8 resultados/f07_prueba.py base.json '{"k":v}' '{"k":v}' ..."""
import sys, json
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import sim
mod = sim.load_formula("f07_saturacion")
p0 = sim.default_params(mod)
d = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8")); p0.update(d.get("params", d))
for v in ["{}"] + sys.argv[2:]:
    p = dict(p0); p.update(json.loads(v))
    r = sim.evaluate(mod, p, n=12000, seed=11, quick=True, detail=True)
    dd = r["detalle"]
    print(v[:70].ljust(70), r["score"], "C1 %.2f C2 %.2f C3 %.2f C4 %.2f C5 %.2f C6 %.2f C8 %.2f C9 %.2f C10 %.2f C11 %.2f" % tuple(r[k] for k in ("C1_cobertura", "C2_duelos_40_60", "C3_arquetipos", "C4_ninguna_celda_manda", "C5_duracion", "C6_niveles", "C8_transparencia", "C9_paridad_items", "C10_nivel_y_oro", "C11_equipos_3v3")),
          "| ehp %.2f dps %.2f tvd %.2f" % (dd["ehp_tanque_sobre_dano"], dd["dps_dano_sobre_tanque"], dd["tanque_vs_dano_winrate"]), flush=True)
