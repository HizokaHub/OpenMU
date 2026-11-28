"""Mide el balance 1v1 "real" (ranks, equipo y pociones del nivel) de los 30 niveles con las tablas que el servidor tiene compiladas, y resume cruces y duracion.
NO ajusta nada. Los niveles que fallen se calibran despues con tools/calibrar_nivel_f10.py.

Hace todo solo: reinicia el servidor en modo de pedidos, mide, resume y al terminar (o al cortarse) lo apaga. Los duelos corren SIN CLIENTE:
no usa el mouse ni el teclado. Para detenerlo al terminar el nivel en curso: crear tools/f10_parar.flag.
Es reanudable: los niveles ya medidos con la misma cantidad de duelos se saltan (--rehacer los repite).

Meta por nivel: duelo medio de 10 a 17 s; cruces ideales en 45-55 %, minimo 30-70 %, ninguno sobre 80 %.
  FALLA   = algun cruce sobre 80 % (o bajo 20 %) o duracion fuera de 10-17 s
  REVISAR = algun cruce fuera de 30-70 %
  OK      = todos los cruces en 30-70 % y duracion en rango
Un cruce PAREJO POR VIDA no cuenta como fuera de rango aunque las victorias lo esten (criterio del usuario, 2026-11-26: gana uno, pero por muy poco):
la vida + SD que le queda al ganador, con signo segun quien gana y promediada en el cruce, es de 10 % o menos (--margen; 0 lo desactiva).

--manos elige que llevan BK y MG: escudo (por defecto), doble (dos armas) o uno (solo el primero de cada cruce lleva dos armas).

Uso: python -I -X utf8 tools/medir_30_niveles_f10.py [--duelos 40] [--niveles "1 2 3"] [--manos escudo|doble|uno] [--rehacer] [--resumen]"""
import argparse, ctypes, json, os, subprocess, sys, time
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(RAIZ / "tools"))
import f10_servidor as servidor
DEBUG = RAIZ / "src/Startup/bin/Debug"
LOG = RAIZ / "logs/servidor.log"
PARAR = RAIZ / "tools/f10_parar.flag"
NOM = {3: "GM", 6: "BK", 11: "FE", 12: "MG", 16: "DL", 20: "SUM", 24: "RF"}
DUR_MIN, DUR_MAX = 10.0, 17.0   # hasta 17 s el duelo es valido (usuario, 2026-11-27)
NIVELES_POR_REINICIO = 4    # el servidor se atrasa con las pasadas (2026-11-27, con las mascotas de mochila: 316-486 ms desde la 5a pasada de un nivel alto) y pierde ids de objeto tras ~35
ESPERA_TANDA_S = 1800
RETRASO_MAX_MS = 320        # separacion media entre acciones de los bots (ideal 280)
# los 21 cruces sin los 7 espejos (no dicen nada del balance y son un cuarto del tiempo); "he" es la High Elf ("fe" es la elfa inicial)
_ALIAS = ["gm", "bk", "he", "mg", "dl", "sum", "rf"]
SOLO_CRUCES = ",".join(f"{x}-{y}" for i, x in enumerate(_ALIAS) for y in _ALIAS[i + 1:])

ap = argparse.ArgumentParser()
ap.add_argument("--duelos", type=int, default=40)
ap.add_argument("--niveles", default=" ".join(str(n) for n in range(1, 31)))
ap.add_argument("--manos", choices=("escudo", "doble", "uno"), default="escudo")
ap.add_argument("--rehacer", action="store_true")
ap.add_argument("--margen", type=float, default=0.10, help="vida del ganador (con signo, promedio del cruce) con la que un cruce cuenta como parejo; 0 = no usar")
ap.add_argument("--resumen", action="store_true", help="solo imprime el resumen de lo ya medido")
ap.add_argument("--reinicio", type=int, default=NIVELES_POR_REINICIO, help="niveles entre reinicios del servidor y el cliente")
args = ap.parse_args()
NIVELES_POR_REINICIO = max(1, args.reinicio)
niveles = [int(x) for x in args.niveles.split()]
SALIDA = RAIZ / f"tools/f10_medicion_{args.duelos}{'' if args.manos == 'escudo' else '_' + args.manos}.json"
medidos = json.load(open(SALIDA, encoding="utf-8")) if SALIDA.exists() else {}


class Detenida(Exception):
    """La medicion no puede seguir."""


def avisar(texto):
    print(f"{time.strftime('%H:%M:%S')} {texto}", flush=True)


def margen_de(duelo):
    """Vida + SD (fraccion) que le queda al ganador, positiva si gana el primero del cruce y negativa si gana el segundo."""
    return duelo["aLeft"] if duelo["winner"] == "a" else -duelo["bLeft"] if duelo["winner"] == "b" else 0.0


def analizar(arch):
    d = json.load(open(arch, encoding="utf-8"))
    tot, cru, dur, n, sin_ganador = {c: [0.0, 0] for c in NOM}, {}, 0.0, 0, 0
    margenes = {}
    for p in d["pairs"]:
        a, b = p["a"], p["b"]
        suma = 0.0
        for x in p["duels"]:
            dur += x["seconds"]; n += 1
            sin_ganador += x["winner"] not in ("a", "b")
            w = {"a": 1.0, "b": 0.0}.get(x["winner"], 0.5)
            suma += w
            if a != b:
                tot[a][0] += w; tot[a][1] += 1; tot[b][0] += 1 - w; tot[b][1] += 1
        if a != b:
            cru[f"{NOM[a]}-{NOM[b]}"] = round(suma / len(p["duels"]), 3)
            margenes[f"{NOM[a]}-{NOM[b]}"] = round(sum(margen_de(x) for x in p["duels"]) / len(p["duels"]), 3)
    return {"razas": {NOM[c]: round(v[0] / max(v[1], 1), 3) for c, v in tot.items()}, "cruces": cru, "margenes": margenes, "duracion": round(dur / max(n, 1), 1),
            "total_duelos": n, "sin_ganador": sin_ganador, "informe": Path(arch).name}


def parejo(r, cruce):
    """Si en el cruce se gana por muy poco (las mediciones antiguas no guardaban el margen)."""
    return args.margen > 0 and abs(r.get("margenes", {}).get(cruce, 1.0)) <= args.margen + 1e-9


def fuera(r, banda):
    return sum(1 for k, v in r["cruces"].items() if abs(v - 0.5) > banda + 1e-9 and not parejo(r, k))


def veredicto(r):
    if fuera(r, 0.30) or not DUR_MIN <= r["duracion"] <= DUR_MAX:
        return "FALLA"
    return "REVISAR" if fuera(r, 0.20) else "OK"


def peor(r):
    """El cruce mas desigual, escrito desde el lado del que gana."""
    nombre, v = max(r["cruces"].items(), key=lambda kv: abs(kv[1] - 0.5))
    a, b = nombre.split("-")
    return f"{a}>{b} {v:.0%}" if v >= 0.5 else f"{b}>{a} {1 - v:.0%}"


def fila(nivel, r):
    total = len(r["cruces"])
    razas = r["razas"]
    mejor_raza, peor_raza = max(razas, key=razas.get), min(razas, key=razas.get)
    return (f"nivel {nivel:>2} | {r['duracion']:5.1f} s | en 45-55: {total - fuera(r, 0.05):>2}/{total} | parejos por vida: {sum(parejo(r, k) for k in r['cruces']):>2} | fuera 30-70: {fuera(r, 0.20):>2} | sobre 80: {fuera(r, 0.30):>2} | "
            f"peor {peor(r):<12} | razas {peor_raza} {razas[peor_raza]:.0%} a {mejor_raza} {razas[mejor_raza]:.0%} | {veredicto(r)}"
            + (f" | empates: {r['sin_ganador']}" if r.get("sin_ganador") else "")
            + (f" | retraso {r['retraso_ms']} ms" if r.get("retraso_ms") else ""))


def resumen():
    hechos = sorted(int(n) for n in medidos)
    lineas = [f"Medicion F10 por nivel, {args.duelos} duelos por cruce: {len(hechos)} niveles"]
    lineas += [fila(n, medidos[str(n)]) for n in hechos]
    for v in ("OK", "REVISAR", "FALLA"):
        grupo = [n for n in hechos if veredicto(medidos[str(n)]) == v]
        lineas.append(f"{v}: {len(grupo)} -> {' '.join(map(str, grupo)) or '-'}")
    lineas.append("duracion fuera de 10-17 s: " + (" ".join(str(n) for n in hechos if not DUR_MIN <= medidos[str(n)]["duracion"] <= DUR_MAX) or "-"))
    texto = "\n".join(lineas)
    SALIDA.with_suffix(".txt").write_text(texto + "\n", encoding="utf-8")
    return texto


# Los duelos corren SIN CLIENTE (tools/f10_servidor.py): no usan el mouse ni el teclado. piloto() y mandar() conservan su forma de antes porque
# otros scripts de medicion los usan.
def piloto(*orden, limite=240):
    """Antes manejaba el juego: ahora "chat" manda el comando al servidor y "cerrar"/"entrar" no hacen nada."""
    if orden and orden[0] == "chat":
        servidor.mandar([orden[1]], limite_s=limite)
    return True


def cerrar_todo():
    servidor.parar()


def escrito_desde(tam):
    return servidor.log_desde(tam)


def mandar(texto, senal, espera_s):
    """Manda un comando al servidor y dice si la senal aparecio en su log (sin esperar a que terminen los duelos)."""
    tam = LOG.stat().st_size
    servidor.encolar([texto])
    limite = time.time() + espera_s
    while time.time() < limite:
        if senal in escrito_desde(tam):
            return True
        time.sleep(1)
    return False


def reiniciar():
    avisar("reiniciando el servidor (1 min)")
    try:
        servidor.arrancar()
    except servidor.Detenida as error:
        raise Detenida(str(error))
    if args.manos != "escudo":
        servidor.mandar([f"/mobaf10 manos {args.manos}"], limite_s=60, senal="DOS ARMAS" if args.manos == "doble" else "lleva dos armas")


SALIDA_PILOTO = servidor.SALIDA_ARRANQUE


def poner_ip(vieja, nueva):
    """Sin cliente no hay que tocar la IP del juego (se conserva la funcion porque otros scripts la llaman)."""


def correr(nivel):
    try:
        return servidor.duelos(SOLO_CRUCES, args.duelos, nivel, limite_s=ESPERA_TANDA_S)
    except servidor.Detenida as error:
        raise Detenida(str(error))


def main():
    if args.resumen:
        print(resumen())
        return 0
    pendientes = [n for n in niveles if args.rehacer or str(n) not in medidos]
    if not pendientes:
        print(resumen())
        return 0
    avisar(f"INICIO: {len(pendientes)} niveles, {args.duelos} duelos por cruce")
    PARAR.unlink(missing_ok=True)
    codigo, en_sesion, inicio = 0, NIVELES_POR_REINICIO, time.time()
    if os.name == "nt":
        # mientras mide, ni el PC ni la pantalla se suspenden (no cambia ningun ajuste: se anula solo al terminar el proceso)
        ctypes.windll.kernel32.SetThreadExecutionState(0x80000000 | 0x00000001 | 0x00000002)
    try:
        for i, nivel in enumerate(pendientes):
            if PARAR.exists():
                PARAR.unlink()
                avisar("parada pedida: no se mide el siguiente nivel")
                break
            for intento in (1, 2):
                try:
                    if en_sesion >= NIVELES_POR_REINICIO:
                        reiniciar()
                        en_sesion = 0
                    arch, retraso = correr(nivel)
                    break
                except (Detenida, OSError, subprocess.SubprocessError) as error:
                    if intento == 2:
                        raise Detenida(f"nivel {nivel}: {error}")
                    avisar(f"AVISO nivel {nivel}: {error}; reinicio y reintento")
                    en_sesion = NIVELES_POR_REINICIO
            en_sesion += 1
            medidos[str(nivel)] = analizar(arch)
            medidos[str(nivel)]["retraso_ms"] = retraso
            if retraso is None or retraso > RETRASO_MAX_MS:
                # con el servidor atrasado los bots lanzan menos skills y el duelo no es el mismo (2026-11-26: 731 ms con las mascotas dibujadas en el cliente)
                avisar(f"AVISO nivel {nivel}: servidor atrasado o sin dato ({retraso} ms por accion de bot; lo normal es 280): esa medicion no es fiable")
            json.dump(medidos, open(SALIDA, "w", encoding="utf-8"), indent=1)
            resumen()
            restan = (time.time() - inicio) / (i + 1) * (len(pendientes) - i - 1) / 60
            avisar(f"[{i + 1}/{len(pendientes)}] {fila(nivel, medidos[str(nivel)])} | quedan ~{restan:.0f} min")
    except (Detenida, KeyboardInterrupt) as error:
        avisar(f"DETENIDA: {error}")
        codigo = 1
    finally:
        cerrar_todo()
    avisar(f"FIN ({'con error' if codigo else 'sin errores'})")
    print(resumen(), flush=True)
    return codigo


if __name__ == "__main__":
    sys.exit(main())
