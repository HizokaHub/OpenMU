"""Ayuda de f07: evalua y muestra un resumen compacto. Uso: python -I -X utf8 resultados/f07_ver.py [n] [params.json] [--full]"""
import sys, json, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import sim
n = int(sys.argv[1]) if len(sys.argv) > 1 else 12000
mod = sim.load_formula("f07_saturacion")
p = sim.default_params(mod)
full = "--full" in sys.argv
args = [a for a in sys.argv[2:] if a != "--full"]
if args:
    d = json.loads(Path(args[0]).read_text(encoding="utf-8") if Path(args[0]).exists() else args[0])
    p.update(d.get("params", d))
t0 = time.time()
r = sim.evaluate(mod, p, n=n, seed=11, quick=not full)
d = r["detalle"]
print("score", r["score"], {k: v for k, v in r.items() if k.startswith("C")}, "t=%.0fs" % (time.time() - t0))
print("band%", d["porcentaje_de_cruces_en_40_60_por_nivel"])
print("ext%", d["porcentaje_de_cruces_extremos_(<25_o_>75)_por_nivel"])
print("ttk", d["ttk_mediano_s_por_nivel"])
print("ehp", d["ehp_tanque_sobre_dano"], "dps", d["dps_dano_sobre_tanque"], "t-vs-d", d["tanque_vs_dano_winrate"])
print("nivel/oro", d["diferencia_de_nivel_y_oro"], "3v3", d["equipos_3v3"], "clips", d["recortes_del_simulador_%"], d["extremos_ultimo_nivel"])
print("muertos", d["elementos_muertos(<0.5%)"])
print("delta", d["delta_por_elemento_%(ofensa,defensa)"])
print("golpe%", d["participacion_en_el_golpe_nivel_max_%"])
cc = d["winrate_clase_vs_clase_nivel16"]
if cc:
    print("clase vs clase nv16 (promedio por clase):", {k: round(sum(v.values()) / 7, 2) for k, v in cc.items()})
