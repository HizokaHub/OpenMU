"""Mide lo que valen el Dark Horse y el Dark Raven frente a gastar ese oro en equipo: duelos DL contra DL, un lado compra las mascotas y el otro no.
Tres comparaciones por nivel (contra un DL sin mascotas): las dos, solo el caballo y solo el cuervo. Ademas cuenta los golpes sin skill de cada lado
(ataque basico + cuervo) para ver si el cuervo ataca de verdad.
Limite: los duelistas pelean con sus 4 skills elegidas, asi que NO lanzan Terremoto (la skill del caballo).
Uso: python -I -X utf8 tools/medir_mascotas_f10.py ["20 25 30"] [duelos=100]
Abre el juego y al final lo cierra. MUEVE EL MOUSE Y EL TECLADO. Guarda en tools/f10_mascotas.json."""
import glob, json, os, statistics, subprocess, sys, time
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
niveles = [int(x) for x in (sys.argv[1] if len(sys.argv) > 1 else "20 25 30").split()]
duelos = int(sys.argv[2]) if len(sys.argv) > 2 else 100
SALIDA = RAIZ / "tools/f10_mascotas.json"
CASOS = [("todas", "las dos"), ("caballo", "solo caballo"), ("cuervo", "solo cuervo")]

# se reutiliza el manejo del juego de la medicion de los 30 niveles (reinicio, chat comprobado en el log, IP), sin ejecutar su programa principal
codigo = (RAIZ / "tools/medir_30_niveles_f10.py").read_text(encoding="utf-8").replace("sys.exit(main())", "")
base = {"__file__": str(RAIZ / "tools/medir_30_niveles_f10.py"), "__name__": "base"}
argv, sys.argv = sys.argv, ["base", "--duelos", str(duelos)]
exec(compile(codigo, "medir_30_niveles_f10.py", "exec"), base)
sys.argv = argv


def tanda(nivel):
    patron = str(base["DEBUG"] / f"duelos_nivel{nivel}_*.json")
    antes = set(glob.glob(patron))
    if not any(base["mandar"](f"/mobaduel dl-dl {duelos} real 60 {nivel}", "[MOBA-DUEL]", 40) for _ in range(3)):
        raise base["Detenida"](f"la tanda del nivel {nivel} no empezo")
    limite = time.time() + 900
    while time.time() < limite:
        time.sleep(5)
        nuevos = set(glob.glob(patron)) - antes
        if nuevos:
            time.sleep(2)
            return max(nuevos, key=os.path.getmtime)
    raise base["Detenida"](f"la tanda del nivel {nivel} no termino en 15 min")


def analizar(arch):
    d = json.load(open(arch, encoding="utf-8"))
    filas = [x for p in d["pairs"] if p["a"] == p["b"] for x in p["duels"]]
    gana = statistics.mean({"a": 1.0, "b": 0.0}.get(x["winner"], 0.5) for x in filas)

    def por_segundo(lado, que):
        total = 0.0
        for x in filas:
            golpe = x[lado + "X"]["hits"].get("0")   # [golpes, dano aplicado, ...]
            total += (golpe[que] if golpe else 0) / max(x["seconds"], 1)
        return total / len(filas)

    def dano(lado):
        return statistics.mean(sum(x[lado + "Damage"].values()) / max(x["seconds"], 1) for x in filas)

    return {"duelos": len(filas), "gana_con_mascotas": round(gana, 3), "duracion": round(statistics.mean(x["seconds"] for x in filas), 1),
            "dano_por_s": [round(dano("a")), round(dano("b"))], "golpes_sin_skill_por_s": [round(por_segundo("a", 0), 2), round(por_segundo("b", 0), 2)],
            "piezas": [filas[0]["aPieces"], filas[0]["bPieces"]], "informe": Path(arch).name}


resultados = json.load(open(SALIDA, encoding="utf-8")) if SALIDA.exists() else {}
base["PARAR"].unlink(missing_ok=True)
base["SALIDA_PILOTO"].parent.mkdir(exist_ok=True)
base["poner_ip"]("127.127.127.127", "127.0.0.1")
codigo_salida = 0
try:
    base["reiniciar"]()
    for plan, nombre in CASOS:
        if not any(base["mandar"](f"/mobaf10 mascotas {plan} ninguna", "mascotas del DL", 6) for _ in range(4)):
            raise base["Detenida"](f"no pude fijar las mascotas ({plan})")
        for nivel in niveles:
            r = analizar(tanda(nivel))
            resultados.setdefault(str(nivel), {})[plan] = r
            json.dump(resultados, open(SALIDA, "w", encoding="utf-8"), indent=1)
            base["avisar"](f"nivel {nivel}, {nombre}: gana el que compra mascotas {r['gana_con_mascotas']:.0%} | dano por segundo {r['dano_por_s'][0]} contra {r['dano_por_s'][1]}"
                           f" | golpes sin skill por segundo {r['golpes_sin_skill_por_s'][0]} contra {r['golpes_sin_skill_por_s'][1]} | piezas {r['piezas'][0]} contra {r['piezas'][1]}")
except (base["Detenida"], OSError, subprocess.SubprocessError) as error:
    base["avisar"](f"DETENIDA: {error}")
    codigo_salida = 1
finally:
    try:
        base["cerrar_todo"]()
    finally:
        base["poner_ip"]("127.0.0.1", "127.127.127.127")
base["avisar"](f"FIN ({'con error' if codigo_salida else 'sin errores'}); juego cerrado")
sys.exit(codigo_salida)
