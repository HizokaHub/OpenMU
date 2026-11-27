"""Ajusta el bono de dos armas (MobaDualWield) de BK y MG con duelos espejo: BK contra BK y MG contra MG, con /mobaf10 manos uno
(en cada cruce solo el primero lleva dos armas y el segundo arma y escudo; los lados se alternan). Meta: gana el de dos armas entre 45 y 55 %.

El bono va por familia y por el tier del arma MAS DEBIL de las dos. Que tier toca en cada nivel se lee del registro [MOBA-BUILD] (lo que compro el bot),
asi que cada (familia, tier) se ajusta con los niveles que caen en el. Los grupos son independientes: cada tanda solo depende del bono de su grupo.
El comando no admite bonos negativos: si con bono 0 las dos armas ya ganan de mas, el grupo queda marcado y no se toca.

Es reanudable (cada tanda medida queda en tools/f10_dos_armas.json), se detiene con tools/f10_parar.flag y al terminar cierra juego y servidor.
Los duelos corren sin cliente (no usa mouse ni teclado): nadie debe usar el PC.
Uso: python -I -X utf8 tools/calibrar_dos_armas_f10.py ["10 15 20 25 30"] [duelos=100] [--resumen]"""
import glob, json, math, os, re, statistics, subprocess, sys, time
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
opciones = [a for a in sys.argv[1:] if not a.startswith("--")]
niveles = [int(x) for x in (opciones[0] if opciones else "10 15 20 25 30").split()]
duelos = int(opciones[1]) if len(opciones) > 1 else 100
SALIDA = RAIZ / "tools/f10_dos_armas.json"
FAMILIAS = {"kn": ("bk", "Knight", "Blade Knight", "BK"), "mg": ("mg", "MagicGladiator", "Magic Gladiator", "MG")}
BANDA, MINIMO = 0.05, 0.20     # meta 45-55 %; minimo aceptable 30-70 %
MAX_PASOS, ANCHO_MIN, BONO_MAX = 9, 0.006, 1.5
TANDAS_POR_REINICIO = 90       # el servidor pierde ids de objeto tras muchas pasadas

# manejo del juego (reinicio, chat comprobado en el log, IP) de la medicion de los 30 niveles, sin ejecutar su programa principal
codigo = (RAIZ / "tools/medir_30_niveles_f10.py").read_text(encoding="utf-8")
base = {"__file__": str(RAIZ / "tools/medir_30_niveles_f10.py"), "__name__": "base"}
argv, sys.argv = sys.argv, ["base"]
exec(compile(codigo, "medir_30_niveles_f10.py", "exec"), base)
sys.argv = argv
avisar, Detenida, LOG = base["avisar"], base["Detenida"], base["LOG"]

estado = json.load(open(SALIDA, encoding="utf-8")) if SALIDA.exists() else {"tiers": {}, "tandas": {}, "bonos": {}}
sesion = {"tandas": TANDAS_POR_REINICIO, "puestos": {}}


def guardar():
    json.dump(estado, open(SALIDA, "w", encoding="utf-8"), indent=1)


def tiers_de_armas():
    """(familia, nombre del arma) -> tier, sacado del catalogo de la tienda y de los nombres de la inicializacion."""
    nombres = {}
    for archivo in (RAIZ / "src/Persistence/Initialization").glob("**/*.cs"):
        for g, n, nombre in re.findall(r'CreateWeapon\(\s*(\d+),\s*(\d+),[^"\n]*"([^"]+)"', archivo.read_text(encoding="utf-8-sig")):
            nombres.setdefault((int(g), int(n)), nombre)
    catalogo = (RAIZ / "src/GameLogic/PlugIns/Moba/MobaShopCatalog.cs").read_text(encoding="utf-8-sig")
    tabla = {}
    for fam, g, n, t in re.findall(r"Weapon\(MobaFamily\.(\w+),\s*(\d+),\s*(\d+),\s*MobaShopTier\.T(\d)", catalogo):
        if (int(g), int(n)) in nombres:
            tabla[(fam, nombres[(int(g), int(n))])] = int(t)
    return tabla


ARMAS = tiers_de_armas()


def tier_en_registro(texto, fam, nivel):
    """Tier del arma mas debil que el bot de dos armas lleva a ese nivel; 0 si no lleva dos armas; None si el registro no lo dice."""
    _, enum, clase, _ = FAMILIAS[fam]
    for linea in texto.splitlines():
        if f"[MOBA-BUILD] level {nivel} class " not in linea or clase not in linea:
            continue
        if not re.search(r"two weapons True", linea, re.I):
            continue
        piezas = dict(re.findall(r'(?:"|\| )([01]):(.+?)\+\d+ \d+', linea))
        tiers = [ARMAS.get((enum, piezas[r])) for r in ("0", "1") if r in piezas]
        return min(tiers) if len(tiers) == 2 and all(tiers) else 0
    return None


def fijar_bono(fam, tier, bono):
    if sesion["puestos"].get((fam, tier)) == bono:
        return
    _, enum, _, _ = FAMILIAS[fam]
    for intento in range(6):
        if intento == 3:
            reabrir()
        tam = LOG.stat().st_size
        try:
            base["piloto"]("chat", f"/mobaf10 doblebono {fam} {bono:.3f} {tier}", limite=120)
        except subprocess.SubprocessError:
            continue
        limite = time.time() + 8
        while time.time() < limite:
            for linea in base["escrito_desde"](tam).splitlines():
                if f"bono de dos armas {enum} (arma T{tier}" in linea:
                    try:
                        if abs(float(linea.rsplit("=", 1)[1].strip().strip('"')) - bono) < 0.0006:
                            sesion["puestos"][(fam, tier)] = bono
                            return
                    except ValueError:
                        pass
            time.sleep(1)
    raise Detenida(f"no pude fijar el bono {fam} T{tier} = {bono:.3f}")


def modo_uno():
    if not any(base["mandar"]("/mobaf10 manos uno", "lleva dos armas", 6) for _ in range(4)):
        raise Detenida("no pude elegir 'manos uno'")


def reabrir():
    # el servidor conserva sus valores: solo se reabre el cliente
    avisar("aviso: reabro el cliente")
    base["piloto"]("cerrar", limite=60)
    time.sleep(3)
    base["piloto"]("entrar")
    time.sleep(8)


def reiniciar():
    base["reiniciar"]()
    modo_uno()
    sesion["puestos"], sesion["tandas"] = {}, 0
    # los bonos viven en memoria: tras reiniciar se parte de 0 y se vuelve a poner el de cada grupo antes de medirlo


def correr(fam, nivel):
    """Una tanda espejo; devuelve (fraccion que gana el de dos armas, duracion media, informe, tier leido del registro)."""
    corto = FAMILIAS[fam][0]
    patron = str(base["DEBUG"] / f"duelos_nivel{nivel}_*.json")
    antes = set(glob.glob(patron))
    inicio_log = LOG.stat().st_size
    if not any(base["mandar"](f"/mobaduel {corto}-{corto} {duelos} real 60 {nivel}", "[MOBA-DUEL]", 40) for _ in range(3)):
        raise Detenida(f"la tanda {fam} del nivel {nivel} no empezo")
    limite = time.time() + 900
    while time.time() < limite:
        time.sleep(4)
        nuevos = set(glob.glob(patron)) - antes
        if nuevos:
            time.sleep(2)
            arch = max(nuevos, key=os.path.getmtime)
            d = json.load(open(arch, encoding="utf-8"))
            if d.get("hands") != "OnlyFirst":
                raise Detenida(f"la tanda salio con manos = {d.get('hands')}")
            filas = [x for p in d["pairs"] for x in p["duels"]]
            gana = statistics.mean({"a": 1.0, "b": 0.0}.get(x["winner"], 0.5) for x in filas)
            sesion["tandas"] += 1
            return (round(gana, 3), round(statistics.mean(x["seconds"] for x in filas), 1), Path(arch).name,
                    tier_en_registro(base["escrito_desde"](inicio_log), fam, nivel))
    raise Detenida(f"la tanda {fam} del nivel {nivel} no termino en 15 min")


def medir(fam, nivel, tier, bono):
    """Mide (o toma de lo ya medido) una tanda con el bono del grupo puesto."""
    clave = f"{fam} {nivel} {bono:.3f}"
    if clave in estado["tandas"]:
        return estado["tandas"][clave]
    if base["PARAR"].exists():
        base["PARAR"].unlink()
        raise Detenida("parada pedida")
    for intento in (1, 2):
        try:
            if sesion["tandas"] >= TANDAS_POR_REINICIO:
                reiniciar()
            if tier:
                fijar_bono(fam, tier, bono)
            gana, seg, informe, leido = correr(fam, nivel)
            break
        except (Detenida, OSError, subprocess.SubprocessError) as error:
            if intento == 2:
                raise Detenida(f"{fam} nivel {nivel}: {error}")
            avisar(f"AVISO {fam} nivel {nivel}: {error}; reinicio y reintento")
            sesion["tandas"] = TANDAS_POR_REINICIO
    if leido is not None:
        estado["tiers"].setdefault(f"{fam} {nivel}", leido)
    estado["tandas"][clave] = {"gana_dos_armas": gana, "duracion": seg, "informe": informe}
    guardar()
    return estado["tandas"][clave]


def logit(v):
    v = min(0.97, max(0.03, v))
    return math.log(v / (1 - v))


def ajustar(fam, tier, del_grupo):
    nombre = f"{FAMILIAS[fam][3]} segunda arma T{tier} (niveles {' '.join(map(str, del_grupo))})"
    pasos, bajo, alto, bono = [], None, None, 0.0   # bajo / alto: (bono, puntaje) con puntaje < 0 y > 0
    for it in range(MAX_PASOS):
        ganan = {n: medir(fam, n, tier, bono)["gana_dos_armas"] for n in del_grupo}
        puntaje = statistics.mean(logit(v) for v in ganan.values())
        peor = max(abs(v - 0.5) for v in ganan.values())
        fuera = sum(abs(v - 0.5) > MINIMO + 1e-9 for v in ganan.values())
        pasos.append({"bono": bono, "ganan": ganan, "fuera3070": fuera, "peor": round(peor, 3), "puntaje": round(puntaje, 3)})
        avisar(f"{nombre}: bono {bono:.3f} -> " + " | ".join(f"nivel {n} {v:.0%}" for n, v in ganan.items()))
        if peor <= BANDA + 1e-9 or abs(puntaje) <= 0.10:
            break
        if puntaje > 0 and bono <= 0:
            break
        if puntaje < 0:
            bajo = (bono, puntaje)
        else:
            alto = (bono, puntaje)
        if bajo and alto:
            if alto[0] - bajo[0] <= ANCHO_MIN:
                break
            parte = min(0.75, max(0.25, -bajo[1] / (alto[1] - bajo[1])))
            nuevo = bajo[0] + parte * (alto[0] - bajo[0])
        elif bajo:
            # la pendiente medida antes es de unas 14 unidades de logit por unidad de log(dano); el paso crece si aun no cruza
            paso = min(0.25, max(0.02, -bajo[1] / 14 * (1 + it * 0.6)))
            nuevo = bajo[0] + paso
        else:
            nuevo = alto[0] / 2
        nuevo = round(min(BONO_MAX, max(0.0, nuevo)), 3)
        if nuevo == bono:
            break
        bono = nuevo
    mejor = min(pasos, key=lambda p: (p["fuera3070"], p["peor"], abs(p["puntaje"])))
    estado["bonos"][f"{fam} T{tier}"] = {**mejor, "niveles": del_grupo, "pasos": len(pasos),
                                         "gana_de_mas_con_cero": mejor["bono"] == 0 and mejor["puntaje"] > 0.2}
    guardar()
    avisar(f"LISTO {nombre}: bono {mejor['bono']:.3f} | " + " | ".join(f"nivel {n} {v:.0%}" for n, v in mejor["ganan"].items())
           + (f" | AVISO: {mejor['fuera3070']} nivel(es) fuera de 30-70 %" if mejor["fuera3070"] else "")
           + (" | con bono 0 las dos armas ya ganan de mas" if estado["bonos"][f"{fam} T{tier}"]["gana_de_mas_con_cero"] else ""))


def resumen():
    for grupo, r in sorted(estado["bonos"].items()):
        print(f"{grupo}: bono {r['bono']:.3f} | " + " | ".join(f"nivel {n} {v:.0%}" for n, v in r["ganan"].items()))


def main():
    if "--resumen" in sys.argv:
        resumen()
        return 0
    base["PARAR"].unlink(missing_ok=True)
    base["SALIDA_PILOTO"].parent.mkdir(exist_ok=True)
    base["poner_ip"]("127.127.127.127", "127.0.0.1")
    salida = 0
    try:
        avisar(f"INICIO: dos armas contra escudo, niveles {niveles}, {duelos} duelos por tanda")
        # fase 1: con bono 0 en todos los niveles; de paso se lee con que tier pelea la segunda arma en cada uno
        for nivel in niveles:
            for fam in FAMILIAS:
                r = medir(fam, nivel, 0, 0.0)
                tier = estado["tiers"].get(f"{fam} {nivel}")
                avisar(f"bono 0 | {FAMILIAS[fam][3]} nivel {nivel}: gana el de dos armas {r['gana_dos_armas']:.0%} | {r['duracion']} s | segunda arma T{tier}")
        sin_tier = [f"{f} {n}" for n in niveles for f in FAMILIAS if estado["tiers"].get(f"{f} {n}") is None]
        if sin_tier:
            raise Detenida(f"no pude leer el equipo de: {', '.join(sin_tier)}")
        grupos = {}
        for nivel in niveles:
            for fam in FAMILIAS:
                tier = estado["tiers"][f"{fam} {nivel}"]
                if tier:
                    grupos.setdefault((fam, tier), []).append(nivel)
        avisar("grupos a ajustar: " + ", ".join(f"{FAMILIAS[f][3]} T{t} ({' '.join(map(str, ns))})" for (f, t), ns in sorted(grupos.items())))
        for (fam, tier), del_grupo in sorted(grupos.items()):
            ajustar(fam, tier, del_grupo)
    except (Detenida, KeyboardInterrupt) as error:
        avisar(f"DETENIDA: {error}")
        salida = 1
    finally:
        try:
            base["cerrar_todo"]()
        finally:
            base["poner_ip"]("127.0.0.1", "127.127.127.127")
    avisar(f"FIN ({'con error' if salida else 'sin errores'}); juego cerrado")
    resumen()
    return salida


if __name__ == "__main__":
    sys.exit(main())
