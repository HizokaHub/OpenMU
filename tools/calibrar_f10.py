"""Calibra en lazo cerrado el psi por clase de F10 con duelos REALES (/mobaduel) usando el bot de pruebas.
Requiere: servidor arriba, Main.exe en la arena MOBA con la cuenta GM, nadie tocando el PC.
Uso: python -I -X utf8 tools/calibrar_f10.py [iteraciones=4] [duelos_por_cruce=4] [pasada=plain] [ganancia=0.45]"""
import glob, json, math, subprocess, sys, time
from pathlib import Path
RAIZ = Path(__file__).resolve().parent.parent
PILOTO = RAIZ / "tools/bot-pruebas/piloto.py"
DEBUG = RAIZ / "src/Startup/bin/Debug"
FAM = {3: "wiz", 6: "kn", 11: "elf", 12: "mg", 16: "dl", 20: "sum", 24: "rf"}
NOM = {3: "GM", 6: "BK", 11: "FE", 12: "MG", 16: "DL", 20: "SUM", 24: "RF"}
iters = int(sys.argv[1]) if len(sys.argv) > 1 else 4
duelos = int(sys.argv[2]) if len(sys.argv) > 2 else 4
pasada = sys.argv[3] if len(sys.argv) > 3 else "plain"
nombre_json = "sin_items" if pasada == "plain" else "t3"
psi = {c: 0.0 for c in FAM}
psit = {c: 0.0 for c in FAM}
if Path(RAIZ / "tools/resultados_f10_psi.json").exists():
    _j = json.load(open(RAIZ / "tools/resultados_f10_psi.json"))
    psi.update({int(k): v for k, v in _j["psi"].items()})
    psit.update({int(k): v for k, v in _j.get("psit", {}).items()})
# con la pasada plain se calibra el psi del build sin piezas; con t3 se calibra el psi con piezas (psit), que se suma al anterior segun las piezas puestas
objetivo, comando = (psi, "psi") if pasada == "plain" else (psit, "psit")


def poner(comando, nombre, valor):
    """Manda /mobaf10 psi|psit y comprueba en el log del servidor que el valor llego completo (el chat puede perder un digito); reintenta hasta 4 veces."""
    log = RAIZ / "logs/servidor.log"
    for intento in range(4):
        tam = log.stat().st_size
        chat(f"/mobaf10 {comando} {nombre} {valor:.3f}")
        with open(log, "rb") as f:
            f.seek(tam)
            nuevo = f.read().decode("utf-8", errors="replace")
        etiqueta = "psi con" if comando == "psit" else "psi "
        for linea in nuevo.splitlines():
            if "[MOBA-F10]" in linea and etiqueta in linea and "=" in linea:
                try:
                    recibido = float(linea.rsplit("=", 1)[1].strip().strip("\"").replace(",", "."))
                except ValueError:
                    continue
                if abs(recibido - valor) < 0.0006:
                    return
        print(f"  (aviso) {comando} {nombre} {valor:.3f} no llego bien (intento {intento + 1}); lo repito", flush=True)
    raise SystemExit(f"no consegui fijar {comando} {nombre} {valor:.3f}")


def chat(texto):
    subprocess.run([sys.executable, "-I", str(PILOTO), "chat", texto], check=True, capture_output=True, timeout=120)
    time.sleep(2.5)


def correr():
    antes = set(glob.glob(str(DEBUG / f"duelos_{nombre_json}_*.json")))
    chat(f"/mobaduel all {duelos} {pasada} 60")
    t0 = time.time()
    while time.time() - t0 < 1800:
        time.sleep(15)
        nuevos = set(glob.glob(str(DEBUG / f"duelos_{nombre_json}_*.json"))) - antes
        if nuevos:
            return sorted(nuevos)[-1]
    raise SystemExit("los duelos no terminaron en 30 min")


def analizar(arch):
    d = json.load(open(arch, encoding="utf-8"))
    tot, dur, n = {c: [0.0, 0] for c in FAM}, 0.0, 0
    for p in d["pairs"]:
        a, b = p["a"], p["b"]
        for x in p["duels"]:
            dur += x["seconds"]; n += 1
            if a == b: continue
            w = {"a": 1.0, "b": 0.0}.get(x["winner"], 0.5)
            tot[a][0] += w; tot[a][1] += 1; tot[b][0] += 1 - w; tot[b][1] += 1
    return {c: v[0] / max(v[1], 1) for c, v in tot.items()}, dur / max(n, 1)


gain = float(sys.argv[4]) if len(sys.argv) > 4 else 0.45
registro = []
for it in range(iters):
    for c, name in FAM.items():
        poner("psi", name, psi[c])
        poner("psit", name, psit[c])
    arch = correr()
    wr, dur = analizar(arch)
    desv = max(abs(v - 0.5) for v in wr.values())
    print(f"iter {it}: winrate " + " ".join(f"{NOM[c]} {wr[c]:.2f}" for c in FAM) + f" | duracion {dur:.1f} s | desviacion maxima {desv:.2f} | " + comando + " " + " ".join(f"{NOM[c]} {objetivo[c]:+.2f}" for c in FAM), flush=True)
    registro.append({"iter": it, "winrate": {NOM[c]: wr[c] for c in FAM}, "duracion": dur, "psi": {NOM[c]: objetivo[c] for c in FAM}, "archivo": Path(arch).name})
    json.dump({"psi": psi, "psit": psit, "registro": registro}, open(RAIZ / "tools/resultados_f10_psi.json", "w"), indent=1)
    if desv < 0.15:
        break
    for c in FAM:
        x = min(0.96, max(0.04, wr[c]))
        objetivo[c] += max(-0.25, min(0.25, -gain * math.log(x / (1 - x))))
    m = sum(objetivo.values()) / len(objetivo)
    for c in objetivo: objetivo[c] -= m
    gain *= 0.85
    json.dump({"psi": psi, "psit": psit, "registro": registro}, open(RAIZ / "tools/resultados_f10_psi.json", "w"), indent=1)
print("fin;", comando, "final:", {NOM[c]: round(objetivo[c], 3) for c in FAM})
