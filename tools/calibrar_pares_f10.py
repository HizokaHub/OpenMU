"""Calibra la TABLA DE CRUCES de F10 (raza A contra raza B) con duelos reales (/mobaduel), sin tocar el psi de cada raza.
Cada cruce es independiente: bajar un cruce extremo no mueve los demas. Regla: un cruce por encima de `techo` (80 % por defecto) se baja a 70 %, y por debajo de
1 - `techo` se sube a 30 %; los cruces dentro de la banda no se tocan (los contrapicks se quedan).
Requiere: servidor arriba con F10 activo, Main.exe en la arena MOBA con la cuenta GM, nadie tocando el PC.
Uso: python -I -X utf8 tools/calibrar_pares_f10.py [iteraciones=8] [duelos_por_cruce=40] [pasada=plain] [ganancia=0.15] [techo=0.80]"""
import glob, json, math, subprocess, sys, time
from pathlib import Path
RAIZ = Path(__file__).resolve().parent.parent
PILOTO = RAIZ / "tools/bot-pruebas/piloto.py"
DEBUG = RAIZ / "src/Startup/bin/Debug"
LOG = RAIZ / "logs/servidor.log"
ARCH = RAIZ / "tools/resultados_f10_pares.json"
FAM = {3: "wiz", 6: "kn", 11: "elf", 12: "mg", 16: "dl", 20: "sum", 24: "rf"}
NOM = {3: "GM", 6: "BK", 11: "FE", 12: "MG", 16: "DL", 20: "SUM", 24: "RF"}
iters = int(sys.argv[1]) if len(sys.argv) > 1 else 8
duelos = int(sys.argv[2]) if len(sys.argv) > 2 else 40
pasada = sys.argv[3] if len(sys.argv) > 3 else "plain"
gain = float(sys.argv[4]) if len(sys.argv) > 4 else 0.15
techo = float(sys.argv[5]) if len(sys.argv) > 5 else 0.80
nombre_json = "sin_items" if pasada == "plain" else "t3"
pares = {}   # (atacante, defensor) -> psi del cruce
if ARCH.exists():
    pares = {tuple(int(x) for x in k.split("-")): v for k, v in json.load(open(ARCH)).get("pares", {}).items()}


def chat(texto):
    subprocess.run([sys.executable, "-I", str(PILOTO), "chat", texto], check=True, capture_output=True, timeout=120)
    time.sleep(2.5)


def poner_cruce(a, b, valor):
    """Manda /mobaf10 pair a-b valor y comprueba en el log que llego completo (el chat puede perder un digito)."""
    for intento in range(4):
        tam = LOG.stat().st_size
        chat(f"/mobaf10 pair {FAM[a]}-{FAM[b]} {valor:.3f}")
        with open(LOG, "rb") as f:
            f.seek(tam)
            nuevo = f.read().decode("utf-8", errors="replace")
        for linea in nuevo.splitlines():
            if "[MOBA-F10]" in linea and "cruce " in linea and "=" in linea:
                try:
                    if abs(float(linea.rsplit("=", 1)[1].strip().strip('"')) - valor) < 0.0006:
                        return
                except ValueError:
                    pass
        print(f"  (aviso) cruce {NOM[a]}>{NOM[b]} {valor:.3f} no llego bien (intento {intento + 1})", flush=True)
    raise SystemExit(f"no consegui fijar el cruce {NOM[a]}>{NOM[b]}")


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


def cruces(arch):
    """(a, b) con a < b en la lista -> victorias de a / duelos (los espejos no cuentan)."""
    d = json.load(open(arch, encoding="utf-8"))
    out, dur, n = {}, 0.0, 0
    for p in d["pairs"]:
        a, b = p["a"], p["b"]
        for x in p["duels"]:
            dur += x["seconds"]; n += 1
        if a == b:
            continue
        w = sum({"a": 1.0, "b": 0.0}.get(x["winner"], 0.5) for x in p["duels"]) / len(p["duels"])
        out[(a, b)] = w
    return out, dur / max(n, 1)


def logit(x):
    x = min(0.97, max(0.03, x))
    return math.log(x / (1 - x))


enviado = {}
chat("/mobaf10 pair clear")
for (a, b), v in pares.items():
    if abs(v) > 1e-9:
        poner_cruce(a, b, v)
        enviado[(a, b)] = v
registro = []
for it in range(iters):
    arch = correr()
    w, dur = cruces(arch)
    fuera = {k: v for k, v in w.items() if v > techo or v < 1 - techo}
    print(f"iter {it}: duracion {dur:.1f} s | cruces fuera de {1 - techo:.0%}-{techo:.0%}: {len(fuera)} de {len(w)} | " +
          " ".join(f"{NOM[a]}-{NOM[b]} {v:.2f}" for (a, b), v in sorted(fuera.items())), flush=True)
    registro.append({"iter": it, "fuera": {f"{NOM[a]}-{NOM[b]}": v for (a, b), v in fuera.items()}, "duracion": dur, "archivo": Path(arch).name})
    json.dump({"pares": {f"{a}-{b}": v for (a, b), v in pares.items()}, "registro": registro}, open(ARCH, "w"), indent=1)
    if not fuera:
        break
    for (a, b), v in fuera.items():
        objetivo = 0.70 if v > techo else 0.30
        paso = max(-0.15, min(0.15, gain * (logit(v) - logit(objetivo))))
        # a gana demasiado -> baja su dano contra b y sube el de b contra a (a la mitad cada uno)
        pares[(a, b)] = max(-1.5, min(1.5, pares.get((a, b), 0.0) - paso))
        pares[(b, a)] = max(-1.5, min(1.5, pares.get((b, a), 0.0) + paso))
    for k, v in pares.items():
        if abs(v - enviado.get(k, 0.0)) > 1e-9:
            poner_cruce(k[0], k[1], v)
            enviado[k] = v
    json.dump({"pares": {f"{a}-{b}": v for (a, b), v in pares.items()}, "registro": registro}, open(ARCH, "w"), indent=1)
print("fin; cruces:", {f"{NOM[a]}>{NOM[b]}": round(v, 3) for (a, b), v in pares.items() if abs(v) > 1e-9})
