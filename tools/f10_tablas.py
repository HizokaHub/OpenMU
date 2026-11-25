"""Lo comun de las tablas de F10 por nivel: que niveles estan calibrados y que valores le tocan a los demas (interpolacion lineal entre los calibrados mas cercanos)."""
import json
from pathlib import Path
RAIZ = Path(__file__).resolve().parent.parent
ESTADO = RAIZ / "tools/f10_calibracion.json"
RECUPERADO = RAIZ / "tools/f10_recuperado.json"
NIVELES = range(1, 31)


def calibrados():
    """Niveles con valores medidos. La mejor pasada reconstruida (recuperar_mejor_f10.py) manda salvo que haya una calibracion nueva con "mejor"."""
    estado = json.load(open(ESTADO)) if ESTADO.exists() else {}
    if RECUPERADO.exists():
        for nivel, e in json.load(open(RECUPERADO)).items():
            if "mejor" not in estado.get(nivel, {}):
                estado[nivel] = e
    return estado


def interpolar(estado, nivel):
    """Los valores de un nivel: los suyos si esta calibrado; si no, el punto intermedio entre los calibrados de abajo y arriba (o el mas cercano en un extremo)."""
    if str(nivel) in estado:
        return estado[str(nivel)]
    hechos = sorted(int(n) for n in estado)
    bajo = max((n for n in hechos if n < nivel), default=hechos[0])
    alto = min((n for n in hechos if n > nivel), default=hechos[-1])
    x, y = estado[str(bajo)], estado[str(alto)]
    t = 0.0 if alto == bajo else (nivel - bajo) / (alto - bajo)

    def mezcla(a, b):
        return a + (b - a) * t
    return {
        "psi": {c: mezcla(x["psi"][c], y["psi"][c]) for c in x["psi"]},
        "pares": {k: mezcla(x["pares"].get(k, 0.0), y["pares"].get(k, 0.0)) for k in {*x["pares"], *y["pares"]}},
        "dano": mezcla(x["dano"], y["dano"]),
        "vida": mezcla(x.get("vida", 1.0), y.get("vida", 1.0)),
    }
