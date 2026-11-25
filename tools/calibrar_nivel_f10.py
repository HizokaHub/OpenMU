"""Calibra el balance 1v1 de F10 de UN nivel con la build REAL de ese nivel (ranks de skills y equipo del tier que corresponde), con duelos reales (/mobaduel ... real 60 <nivel>).
Tres fases, cada una con verificacion de los comandos en el log del servidor:
  1. DURACION: ajusta el multiplicador de dano del nivel hasta que el duelo medio dure `dur_min`-`dur_max` s (10-15).
  2. PSI: ajusta el psi de cada raza en ese nivel hasta que ninguna gane mas de 0.5 +- `tol_raza` contra el campo.
  3. CRUCES: ajusta la tabla de cruces del nivel (raza A contra raza B, sin tocar el resto) hasta que cada cruce quede en 0.5 +- `tol_cruce`.
Estado en tools/f10_calibracion.json (lo lee generar_tablas_f10.py para escribir los valores por defecto del servidor).
Requiere: servidor arriba con F10 activo, Main.exe en la arena MOBA con la cuenta GM, nadie tocando el PC.
Uso: python -I -X utf8 tools/calibrar_nivel_f10.py <nivel> [duelos_psi=20] [duelos_cruces=60] [tol_raza=0.06] [tol_cruce=0.10]"""
import glob, json, math, subprocess, sys, time
from pathlib import Path
RAIZ = Path(__file__).resolve().parent.parent
PILOTO = RAIZ / "tools/bot-pruebas/piloto.py"
DEBUG = RAIZ / "src/Startup/bin/Debug"
LOG = RAIZ / "logs/servidor.log"
ESTADO = RAIZ / "tools/f10_calibracion.json"
FAM = {3: "wiz", 6: "kn", 11: "elf", 12: "mg", 16: "dl", 20: "sum", 24: "rf"}
NOM = {3: "GM", 6: "BK", 11: "FE", 12: "MG", 16: "DL", 20: "SUM", 24: "RF"}
nivel = int(sys.argv[1])
d_psi = int(sys.argv[2]) if len(sys.argv) > 2 else 20
d_cruce = int(sys.argv[3]) if len(sys.argv) > 3 else 40
tol_raza = float(sys.argv[4]) if len(sys.argv) > 4 else 0.12
tol_cruce = float(sys.argv[5]) if len(sys.argv) > 5 else 0.20
DUR_MIN, DUR_MAX, DUR_META = 10.0, 16.0, 12.5
nombre_json = f"nivel{nivel}"

# estado: por nivel -> {"psi": {clase: v}, "pares": {"a-b": v}, "dano": mult, "vida": mult}
estado = json.load(open(ESTADO)) if ESTADO.exists() else {}
ini_psi = {3: 0.575, 6: -0.006, 11: -0.173, 12: 0.353, 16: 0.354, 20: -0.386, 24: -0.718}
e = estado.setdefault(str(nivel), {"psi": {str(c): v for c, v in ini_psi.items()}, "pares": {}, "dano": 1.0, "vida": 1.0, "registro": []})
psi = {int(k): v for k, v in e["psi"].items()}
pares = {tuple(int(x) for x in k.split("-")): v for k, v in e["pares"].items()}


def guardar():
    e["psi"] = {str(c): v for c, v in psi.items()}
    e["pares"] = {f"{a}-{b}": v for (a, b), v in pares.items()}
    json.dump(estado, open(ESTADO, "w"), indent=1)


def chat(texto):
    subprocess.run([sys.executable, "-I", str(PILOTO), "chat", texto], check=True, capture_output=True, timeout=120)
    time.sleep(2.2)


def poner(comando, clave, valor, etiqueta):
    """Manda un comando /mobaf10 y comprueba en el log que el valor llego completo (el chat puede perder un digito); reintenta hasta 4 veces."""
    for intento in range(4):
        tam = LOG.stat().st_size
        chat(comando)
        with open(LOG, "rb") as f:
            f.seek(tam)
            nuevo = f.read().decode("utf-8", errors="replace")
        for linea in nuevo.splitlines():
            if "[MOBA-F10]" in linea and etiqueta in linea and "=" in linea:
                try:
                    if abs(float(linea.rsplit("=", 1)[1].strip().strip('"')) - valor) < 0.0006 + 0.0006 * abs(valor):
                        return
                except ValueError:
                    pass
        print(f"  (aviso) {clave} no llego bien (intento {intento + 1}); lo repito", flush=True)
    raise SystemExit(f"no consegui fijar {clave}")


def set_psi(c):
    poner(f"/mobaf10 psi {FAM[c]} {psi[c]:.3f} {nivel}", f"psi {NOM[c]}", psi[c], "psi ")


def set_cruce(a, b):
    poner(f"/mobaf10 pair {FAM[a]}-{FAM[b]} {pares[(a, b)]:.3f} {nivel}", f"cruce {NOM[a]}>{NOM[b]}", pares[(a, b)], "cruce ")


def set_dano():
    # la escala de dano del nivel = global (1.5) * multiplicador: el comando lleva el multiplicador
    poner(f"/mobaf10 dmgk {nivel} {e['dano']:.3f}", "dano del nivel", 1.5 * e["dano"], "del nivel")


def set_vida():
    # la escala de vida del nivel = global (1.35) * multiplicador
    poner(f"/mobaf10 hpk {nivel} {e['vida']:.3f}", "vida del nivel", 1.35 * e["vida"], "vida del nivel")


def correr(duelos):
    antes = set(glob.glob(str(DEBUG / f"duelos_{nombre_json}_*.json")))
    chat(f"/mobaduel all {duelos} real 60 {nivel}")
    t0 = time.time()
    while time.time() - t0 < 2400:
        time.sleep(15)
        nuevos = set(glob.glob(str(DEBUG / f"duelos_{nombre_json}_*.json"))) - antes
        if nuevos:
            return sorted(nuevos)[-1]
    raise SystemExit("los duelos no terminaron en 40 min")


def analizar(arch):
    d = json.load(open(arch, encoding="utf-8"))
    tot, cru, dur, n = {c: [0.0, 0] for c in FAM}, {}, 0.0, 0
    for p in d["pairs"]:
        a, b = p["a"], p["b"]
        wsum = 0.0
        for x in p["duels"]:
            dur += x["seconds"]; n += 1
            w = {"a": 1.0, "b": 0.0}.get(x["winner"], 0.5)
            wsum += w
            if a == b:
                continue
            tot[a][0] += w; tot[a][1] += 1; tot[b][0] += 1 - w; tot[b][1] += 1
        if a != b:
            cru[(a, b)] = wsum / len(p["duels"])
    return {c: v[0] / max(v[1], 1) for c, v in tot.items()}, cru, dur / max(n, 1)


def logit(x):
    x = min(0.97, max(0.03, x))
    return math.log(x / (1 - x))


def fuera_de(cru, banda):
    return sum(1 for v in cru.values() if abs(v - 0.5) > banda + 1e-9)


def puntaje(wr, cru, dur):
    """Menor es mejor, en este orden: duracion fuera de rango, cruces sobre 80 %, cruces fuera de 30-70 %, raza mas desviada."""
    return (0 if DUR_MIN <= dur <= DUR_MAX else 1, fuera_de(cru, 0.30), fuera_de(cru, 0.20), round(max(abs(v - 0.5) for v in wr.values()), 3))


# pasadas de cruces de ESTA corrida (todas con la misma cantidad de duelos): (puntaje, nombre, valores con los que se midio)
candidatas = []


def registrar(fase, it, wr, cru, dur):
    # se llama antes de ajustar nada, asi que estos son los valores con los que se midio la pasada
    medidos = {"psi": {str(c): v for c, v in psi.items()}, "pares": {f"{a}-{b}": v for (a, b), v in pares.items()}, "dano": e["dano"], "vida": e["vida"]}
    e["registro"].append({"fase": fase, "it": it, "duracion": dur, "razas": {NOM[c]: round(v, 3) for c, v in wr.items()},
                          "peor_cruce": round(max(abs(v - 0.5) for v in cru.values()), 3),
                          "sobre80": fuera_de(cru, 0.30), "fuera3070": fuera_de(cru, 0.20), "valores": medidos})
    if fase.startswith("cruces"):
        candidatas.append((puntaje(wr, cru, dur), f"{fase} it {it}", medidos))
    guardar()


print(f"=== nivel {nivel}: duelos psi {d_psi}, cruces {d_cruce}", flush=True)
# se parte de un nivel limpio: psi del estado, cruces del estado, dano del estado
for c in FAM:
    set_psi(c)
chat(f"/mobaf10 pair clear x {nivel}")
for (a, b) in list(pares):
    set_cruce(a, b)
set_dano()
set_vida()

# ---- 1. DURACION ---- (con la VIDA del nivel: el dano casi no acorta el duelo, porque el tope por golpe y el freno de rafaga lo limitan y ambos son relativos a la vida)
for it in range(4):
    arch = correr(d_psi)
    wr, cru, dur = analizar(arch)
    print(f"dur {it}: duracion {dur:.1f} s | vida x{e['vida']:.2f}", flush=True)
    registrar("duracion", it, wr, cru, dur)
    if DUR_MIN <= dur <= DUR_MAX:
        break
    e["vida"] = max(0.15, min(6.0, e["vida"] * DUR_META / dur))
    set_vida()

# ---- 2. PSI ----
for it in range(10):
    arch = correr(d_psi)
    wr, cru, dur = analizar(arch)
    desv = max(abs(v - 0.5) for v in wr.values())
    print(f"psi {it}: " + " ".join(f"{NOM[c]} {wr[c]:.2f}" for c in FAM) + f" | duracion {dur:.1f} s | desviacion {desv:.2f}", flush=True)
    registrar("psi", it, wr, cru, dur)
    if desv <= tol_raza:
        break
    # ganancia adaptativa: con las builds equipadas los psi parten lejos (RF 0.02, BK 0.97) y 0.06 tardaba demasiado; cerca del objetivo vuelve a ser pequena (los duelos son muy empinados)
    gain = 0.16 if desv > 0.30 else 0.10 if desv > 0.18 else 0.05
    for c in FAM:
        psi[c] += max(-0.4, min(0.4, -gain * logit(wr[c])))
        set_psi(c)

# ---- 3. CRUCES ---- (por etapas: primero solo los extremos (+-0.30), luego la banda 30-70 % (+-0.20) y al final la pedida (+-tol_cruce); ajustar los 20 cruces a la vez
# descontrolaba el sistema: cada paso es pequeno y solo toca los cruces que estan fuera de la etapa)
for etapa in [t for t in (0.30, 0.20) if t > tol_cruce] + [tol_cruce]:
    for it in range(5):
        arch = correr(d_cruce)
        wr, cru, dur = analizar(arch)
        fuera = {k: v for k, v in cru.items() if abs(v - 0.5) > etapa}
        print(f"cruces +-{etapa:.2f} it {it}: duracion {dur:.1f} s | fuera: {len(fuera)} de {len(cru)} | razas " + " ".join(f"{NOM[c]} {wr[c]:.2f}" for c in FAM), flush=True)
        registrar(f"cruces{etapa}", it, wr, cru, dur)
        if not fuera:
            break
        for (a, b), v in fuera.items():
            # el paso crece con el error pero esta acotado; se corrige solo la parte que sobra sobre la etapa (hasta el borde de la banda)
            borde = 0.5 + etapa * (1 if v > 0.5 else -1) * 0.8
            # etapa fina (banda 30-70 %): pasos mas pequenos, porque el ruido de 40 duelos (+-0.08) hacia divergir el ajuste
            g, tope = (0.03, 0.08) if etapa >= 0.30 else (0.015, 0.04)
            paso = max(-tope, min(tope, g * (logit(v) - logit(borde))))
            pares[(a, b)] = max(-1.5, min(1.5, pares.get((a, b), 0.0) - paso))
            pares[(b, a)] = max(-1.5, min(1.5, pares.get((b, a), 0.0) + paso))
            set_cruce(a, b)
            set_cruce(b, a)
        # las razas se mantienen centradas con el psi (los cruces son antisimetricos y no pueden corregir una raza entera: RF se quedaba en 0.75)
        for c in FAM:
            if abs(wr[c] - 0.5) > 0.10:
                psi[c] += max(-0.10, min(0.10, -0.05 * logit(wr[c])))
                set_psi(c)
        guardar()
# se guarda la MEJOR pasada medida, no los ultimos valores (que ademas llevaban un ajuste sin medir)
mejor, cual, valores = min(candidatas, key=lambda x: x[0])
psi.update({int(c): v for c, v in valores["psi"].items()})
pares.clear()
pares.update({tuple(int(x) for x in k.split("-")): v for k, v in valores["pares"].items()})
e["dano"], e["vida"] = valores["dano"], valores["vida"]
e["mejor"] = {"pasada": cual, "sobre80": mejor[1], "fuera3070": mejor[2], "raza_desv": mejor[3]}
guardar()
print(f"mejor pasada del nivel {nivel}: {cual} | sobre 80 %: {mejor[1]} | fuera de 30-70 %: {mejor[2]} de 21 | raza mas desviada {mejor[3]:.2f}", flush=True)
print(f"fin nivel {nivel}: psi", {NOM[c]: round(v, 3) for c, v in psi.items()}, "dano x", round(e["dano"], 3), "vida x", round(e["vida"], 3), flush=True)
