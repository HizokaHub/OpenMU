"""Mide cuanto alargan las pociones los duelos: para cada nivel corre la tanda SIN pociones y con cada escala de curacion pedida (mismo equipo).
Uso: python -I -X utf8 tools/medir_pociones_f10.py "10 20 30" "off 0.03 0.08" [duelos_por_cruce=20]
Abre el juego y al final lo cierra (nadie debe tocar el PC). Guarda y acumula en tools/f10_pociones.json."""
import glob, json, re, shutil, statistics, subprocess, sys, time
from pathlib import Path
RAIZ = Path(__file__).resolve().parent.parent
PILOTO = RAIZ / "tools/bot-pruebas/piloto.py"
DEBUG = RAIZ / "src/Startup/bin/Debug"
LOG = RAIZ / "logs/servidor.log"
INI = RAIZ.parent / "mu-main/out/build/windows-x64/src/Release/config.ini"
SALIDA = RAIZ / "tools/f10_pociones.json"
niveles = [int(x) for x in sys.argv[1].split()]
modos = sys.argv[2].split()
duelos = int(sys.argv[3]) if len(sys.argv) > 3 else 20
cruces = sys.argv[4] if len(sys.argv) > 4 else "all"   # "espejo" = cada raza contra si misma


def ip(vieja, nueva):
    texto = INI.read_bytes()
    INI.write_bytes(texto.replace(f"ServerIP={vieja}".encode(), f"ServerIP={nueva}".encode()))


def piloto(*args, espera=180):
    return subprocess.run([sys.executable, "-I", str(PILOTO), *args], capture_output=True, timeout=espera)


def desde(tam):
    with open(LOG, "rb") as f:
        f.seek(tam)
        return f.read().decode("utf-8", errors="replace")


def reabrir():
    print("  (aviso) reabro el cliente", flush=True)
    piloto("cerrar")
    time.sleep(3)
    piloto("entrar")
    time.sleep(8)


def enviar(comando, senal, espera=12):
    """Manda un comando y no sigue hasta ver en el log la senal de que llego."""
    for intento in range(6):
        if intento in (2, 4):
            reabrir()
        tam = LOG.stat().st_size
        piloto("chat", comando)
        limite = time.time() + espera
        while time.time() < limite:
            nuevo = desde(tam)
            if re.search(senal, nuevo):
                return
            time.sleep(1)
        print(f"  (aviso) no llego '{comando}' (intento {intento + 1})", flush=True)
    raise SystemExit(f"no consegui mandar {comando}")


def tanda(nivel):
    antes = set(glob.glob(str(DEBUG / f"duelos_nivel{nivel}_*.json")))
    enviar(f"/mobaduel {cruces} {duelos} real 60 {nivel}", r"\[MOBA-DUEL\]", 40)
    limite = time.time() + 2400
    while time.time() < limite:
        time.sleep(5)
        nuevos = set(glob.glob(str(DEBUG / f"duelos_{'nivel'}{nivel}_*.json"))) - antes
        if nuevos:
            time.sleep(2)
            return sorted(nuevos)[-1]
    raise SystemExit("la tanda no termino en 40 min")


def resumen(arch):
    d = json.load(open(arch, encoding="utf-8"))
    filas = [x for p in d["pairs"] for x in p["duels"]]
    seg = [x["seconds"] for x in filas]
    gana_primero = {p["aName"]: round(statistics.mean({"a": 1.0, "b": 0.0}.get(x["winner"], 0.5) for x in p["duels"]), 3) for p in d["pairs"] if p["a"] == p["b"]}
    vida = [x[k][0] for x in filas for k in ("aPotions", "bPotions")]
    mana = [x[k][1] for x in filas for k in ("aPotions", "bPotions")]
    return {"duelos": len(filas), "media": round(statistics.mean(seg), 2), "mediana": round(statistics.median(seg), 2),
            "sin_fin": sum(1 for s in seg if s >= d["timeoutSeconds"] - 1), "pociones_vida": round(statistics.mean(vida), 1),
            "pociones_mana": round(statistics.mean(mana), 1), "gana_primero_en_espejo": gana_primero, "informe": Path(arch).name}


resultados = json.load(open(SALIDA)) if SALIDA.exists() else {}
ip("127.127.127.127", "127.0.0.1")
try:
    bash = shutil.which("bash") or r"C:\Program Files\Git\bin\bash.exe"
    with open(RAIZ / "logs/reinicio_pociones.log", "w") as reg:
        if subprocess.run([bash, "tools/reiniciar_para_calibrar.sh"], cwd=RAIZ, stdout=reg, stderr=reg, timeout=600).returncode:
            raise SystemExit("no pude abrir el juego (ver logs/reinicio_pociones.log)")
    for modo in modos:
        if modo == "off":
            enviar("/mobaf10 beber off", r"MOBA-F10.*SIN pociones")
        elif modo == "uno":
            enviar("/mobaf10 beber uno", r"MOBA-F10.*SOLO el primero")
        else:
            enviar("/mobaf10 beber on", r"MOBA-F10.*CON pociones")
            enviar(f"/mobaf10 pocion {modo}", rf"MOBA-F10.*= {float(modo):g}")
        for nivel in niveles:
            r = resumen(tanda(nivel))
            resultados.setdefault(str(nivel), {})[modo if cruces == "all" else f"{modo}-{cruces}"] = r
            json.dump(resultados, open(SALIDA, "w"), indent=1)
            if modo == "uno" and cruces == "espejo":
                print(f"espejo nivel {nivel} (gana quien bebe): " + " | ".join(f"{k} {v:.0%}" for k, v in r["gana_primero_en_espejo"].items()), flush=True)
            print(f"modo {modo} nivel {nivel}: media {r['media']} s | mediana {r['mediana']} s | sin fin {r['sin_fin']} | pociones por duelista: vida {r['pociones_vida']}, mana {r['pociones_mana']}", flush=True)
finally:
    piloto("cerrar")
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "tools/iniciar-todo.ps1", "-Parar", "-Forzar"], cwd=RAIZ, capture_output=True, timeout=300)
    ip("127.0.0.1", "127.127.127.127")
    print("juego y servidor cerrados", flush=True)
