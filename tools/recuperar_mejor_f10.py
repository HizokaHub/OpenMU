"""Reconstruye la mejor pasada de cruces de niveles calibrados ANTES de que calibrar_nivel_f10.py guardara los valores de cada pasada.
Parte de los valores finales y deshace, de la ultima pasada a la primera, el ajuste que cada una produjo (se recalcula desde su informe duelos_*.json).
Solo vale para la corrida del 2026-11-25 (/tmp/todos4.log): usa sus mismas reglas de ajuste. No toca f10_calibracion.json: escribe tools/f10_recuperado.json.
Uso: python -I -X utf8 tools/recuperar_mejor_f10.py <commit_con_los_valores_finales> <log_de_la_corrida>"""
import json, math, re, subprocess, sys
from pathlib import Path
RAIZ = Path(__file__).resolve().parent.parent
DEBUG = RAIZ / "src/Startup/bin/Debug"
FAM = [3, 6, 11, 12, 16, 20, 24]
NOM = {3: "GM", 6: "BK", 11: "FE", 12: "MG", 16: "DL", 20: "SUM", 24: "RF"}
finales = json.loads(subprocess.check_output(["git", "show", f"{sys.argv[1]}:tools/f10_calibracion.json"], cwd=RAIZ))
log = Path(sys.argv[2]).read_text(encoding="utf-8", errors="replace")


def analizar(arch):
    d = json.load(open(arch, encoding="utf-8"))
    tot, cru, dur, n = {c: [0.0, 0] for c in FAM}, {}, 0.0, 0
    for p in d["pairs"]:
        a, b, wsum = p["a"], p["b"], 0.0
        for x in p["duels"]:
            dur += x["seconds"]; n += 1
            w = {"a": 1.0, "b": 0.0}.get(x["winner"], 0.5)
            wsum += w
            if a != b:
                tot[a][0] += w; tot[a][1] += 1; tot[b][0] += 1 - w; tot[b][1] += 1
        if a != b:
            cru[(a, b)] = wsum / len(p["duels"])
    return {c: v[0] / max(v[1], 1) for c, v in tot.items()}, cru, dur / max(n, 1)


def logit(x):
    x = min(0.97, max(0.03, x))
    return math.log(x / (1 - x))


def fuera_de(cru, banda):
    return sum(1 for v in cru.values() if abs(v - 0.5) > banda + 1e-9)


salida = {}
for nivel in ("30", "20", "10"):
    bloque = log.split(f"=== nivel {nivel}:", 1)[1].split("=== nivel", 1)[0]
    lineas = re.findall(r"^cruces \+-(\d\.\d\d) it (\d): duracion ([\d.]+) s \| fuera: (\d+) de 21", bloque, re.M)
    # los informes de la corrida: los ultimos de ese nivel anteriores al cierre
    informes = sorted(DEBUG.glob(f"duelos_nivel{nivel}_20261125_*.json"))[-len(lineas):]
    pasadas = []
    for (etapa, it, dur_log, fuera_log), arch in zip(lineas, informes):
        wr, cru, dur = analizar(arch)
        # se cuenta igual que en la corrida (sin margen: un 80 % exacto contaba como fuera)
        assert sum(1 for v in cru.values() if abs(v - 0.5) > float(etapa)) == int(fuera_log) and abs(dur - float(dur_log)) < 0.06, f"el informe {arch.name} no corresponde a la linea {nivel} +-{etapa} it {it}"
        pasadas.append((float(etapa), int(it), wr, cru, dur, arch.name))
    e = finales[nivel]
    psi = {int(k): v for k, v in e["psi"].items()}
    pares = {tuple(int(x) for x in k.split("-")): v for k, v in e["pares"].items()}
    candidatas = []
    for etapa, it, wr, cru, dur, nombre in reversed(pasadas):
        fuera = {k: v for k, v in cru.items() if abs(v - 0.5) > etapa}
        if fuera:  # sin cruces fuera de la etapa no hubo ajuste
            for (a, b), v in fuera.items():
                borde = 0.5 + etapa * (1 if v > 0.5 else -1) * 0.8
                g, tope = (0.03, 0.08) if etapa >= 0.30 else (0.015, 0.04)
                paso = max(-tope, min(tope, g * (logit(v) - logit(borde))))
                assert abs(pares.get((a, b), 0.0)) < 1.4999, "un cruce toco el limite: no se puede deshacer"
                pares[(a, b)] = pares.get((a, b), 0.0) + paso
                pares[(b, a)] = pares.get((b, a), 0.0) - paso
            for c in FAM:
                if abs(wr[c] - 0.5) > 0.10:
                    psi[c] -= max(-0.10, min(0.10, -0.05 * logit(wr[c])))
        puntaje = (0 if 10.0 <= dur <= 16.0 else 1, fuera_de(cru, 0.30), fuera_de(cru, 0.20), round(max(abs(v - 0.5) for v in wr.values()), 3))
        candidatas.append((puntaje, f"cruces{etapa} it {it}", dict(psi), dict(pares), wr, cru, dur, nombre))
    primera = candidatas[-1]
    print(f"nivel {nivel}: {len(pasadas)} informes coinciden con el log | cruce mas grande al empezar los cruces: {max((abs(v) for v in primera[3].values()), default=0):.6f}")
    print(f"   psi al empezar los cruces: " + " ".join(f"{NOM[c]} {primera[2][c]:.4f}" for c in FAM))
    puntaje, cual, p, q, wr, cru, dur, nombre = min(candidatas, key=lambda x: x[0])
    salida[nivel] = {"psi": {str(c): v for c, v in p.items()}, "pares": {f"{a}-{b}": v for (a, b), v in q.items()}, "dano": e["dano"], "vida": e["vida"],
                     "mejor": {"pasada": cual, "informe": nombre, "sobre80": puntaje[1], "fuera3070": puntaje[2], "raza_desv": puntaje[3], "duracion": round(dur, 1),
                               "razas": {NOM[c]: round(v, 3) for c, v in wr.items()}}}
    peores = sorted(cru.items(), key=lambda kv: -abs(kv[1] - 0.5))[:3]
    print(f"   MEJOR {cual}: sobre 80 %: {puntaje[1]} | fuera de 30-70 %: {puntaje[2]} | {dur:.1f} s | " + " ".join(f"{NOM[c]} {wr[c]:.2f}" for c in FAM))
    print("   peores: " + ", ".join(f"{NOM[a]}-{NOM[b]} {max(v, 1 - v):.0%} gana {NOM[a if v > 0.5 else b]}" for (a, b), v in peores))
json.dump(salida, open(RAIZ / "tools/f10_recuperado.json", "w"), indent=1)
