"""Convierte los informes JSON de /mobaduel en INFORME_DUELOS.md (matriz 7x7 por pasada, totales por clase, cruces fuera de meta).

Uso: python -I -X utf8 tools/informe_duelos.py [carpeta_con_los_json]   (sin argumento: src/Startup/bin/Debug)

Por pasada (sin_items, t3) se juntan todos los duelos_<pasada>_*.json; para cada cruce vale el archivo MAS RECIENTE que lo contenga, asi un cruce
repetido a 30 duelos reemplaza al de 10. Meta del usuario: 4-6 victorias de 10 en cada cruce (40-60 %).
"""
import glob
import json
import statistics
import sys
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
ALIAS = {3: "GM", 6: "BK", 11: "FE", 12: "MG", 16: "DL", 20: "SUM", 24: "RF"}
NOMBRE_PASADA = {"sin_items": "Sin ítems (equipo inicial del clon)", "t3": "Con set completo T3 (set, arma, escudo y accesorios)"}
META = (0.40, 0.60)
NOMBRE_SKILL = {0: "básico"}


def cargar(carpeta, pasada):
    """Devuelve {(a,b): duelos} con el archivo mas reciente de cada cruce y los metadatos del mas reciente."""
    cruces, meta, usados = {}, {}, []
    for ruta in sorted(glob.glob(str(carpeta / f"duelos_{pasada}_*.json"))):
        datos = json.loads(Path(ruta).read_text(encoding="utf-8"))
        usados.append(Path(ruta).name)
        meta = {k: v for k, v in datos.items() if k != "pairs"}
        for p in datos["pairs"]:
            if p["duels"]:
                cruces[(p["a"], p["b"])] = p
    return cruces, meta, usados


def victorias(duelos):
    a = sum(d["winner"] == "a" for d in duelos)
    b = sum(d["winner"] == "b" for d in duelos)
    return a, b, len(duelos) - a - b


def celda(duelos, invertir):
    a, b, e = victorias(duelos)
    n = len(duelos)
    ganadas = b if invertir else a
    tasa = (ganadas + e / 2) / n
    txt = f"{ganadas}-{n - ganadas - e}" + (f"-{e}e" if e else "")
    if n != 10:
        txt += f" ({n})"
    marca = "" if META[0] <= tasa <= META[1] else " ⚠"
    return f"{txt}{marca}"


def seccion(carpeta, pasada):
    cruces, meta, usados = cargar(carpeta, pasada)
    if not cruces:
        return [f"## {NOMBRE_PASADA[pasada]}", "", "_Sin datos todavía._", ""]
    clases = sorted({c for par in cruces for c in par})
    nombres = {}
    for p in cruces.values():
        nombres[p["a"]], nombres[p["b"]] = p["aName"], p["bName"]
    etiqueta = lambda c: ALIAS.get(c, str(c))
    out = [f"## {NOMBRE_PASADA[pasada]}", ""]
    out.append(f"Nivel {meta.get('level')}, todas las skills a rango {meta.get('rank')}, tiempo máximo {meta.get('timeoutSeconds')} s por duelo. Archivos: {', '.join(usados)}.")
    out.append("")
    out.append("Cada celda: **victorias de la clase de la fila - derrotas** (e = empates por tiempo; entre paréntesis, duelos si no son 10). ⚠ = fuera de 40-60 %.")
    out.append("")
    out.append("| vs → | " + " | ".join(etiqueta(c) for c in clases) + " |")
    out.append("|---|" + "---|" * len(clases))
    totales = {c: [0.0, 0] for c in clases}  # puntos, duelos
    duraciones, vida_ganador = {c: [] for c in clases}, {c: [] for c in clases}
    for fila in clases:
        celdas = []
        for col in clases:
            if (fila, col) in cruces:
                celdas.append(celda(cruces[(fila, col)]["duels"], False))
            elif (col, fila) in cruces:
                celdas.append(celda(cruces[(col, fila)]["duels"], True))
            else:
                celdas.append("·")
        out.append(f"| **{etiqueta(fila)}** | " + " | ".join(celdas) + " |")
    out.append("")

    fuera = []
    for (a, b), p in sorted(cruces.items()):
        d = p["duels"]
        wa, wb, e = victorias(d)
        n = len(d)
        totales[a][0] += wa + e / 2
        totales[a][1] += n
        if a != b:
            totales[b][0] += wb + e / 2
            totales[b][1] += n
        for x in d:
            ganador = a if x["winner"] == "a" else b if x["winner"] == "b" else None
            for c in {a, b}:
                duraciones[c].append(x["seconds"])
            if ganador is not None:
                vida_ganador[ganador].append(x["aLeft"] if x["winner"] == "a" else x["bLeft"])
        tasa = (wa + e / 2) / n
        if a != b and not (META[0] <= tasa <= META[1]):
            fuera.append((abs(tasa - 0.5), a, b, wa, wb, e, n))

    out.append("### Totales por clase (sin contar espejos)")
    out.append("")
    out.append("| Clase | Duelos | % de victorias | Duración media (s) | Vida media que le queda al ganar |")
    out.append("|---|---|---|---|---|")
    for c in clases:
        pts, n = totales[c]
        # sin espejos: se recalcula
        pts, n = 0.0, 0
        for (a, b), p in cruces.items():
            if a == b:
                continue
            wa, wb, e = victorias(p["duels"])
            if a == c:
                pts += wa + e / 2
                n += len(p["duels"])
            elif b == c:
                pts += wb + e / 2
                n += len(p["duels"])
        pct = f"{100 * pts / n:.0f} %" if n else "-"
        dur = f"{statistics.mean(duraciones[c]):.0f}" if duraciones[c] else "-"
        vida = f"{100 * statistics.mean(vida_ganador[c]):.0f} %" if vida_ganador[c] else "-"
        out.append(f"| {nombres.get(c, c)} ({etiqueta(c)}) | {n} | {pct} | {dur} | {vida} |")
    out.append("")

    espejos = [(a, p) for (a, b), p in cruces.items() if a == b]
    if espejos:
        out.append("**Espejos** (gana el lado azul/norte en los duelos pares, el rojo/sur en los impares; un espejo sano da ~50 %): " + ", ".join(
            f"{etiqueta(a)} {victorias(p['duels'])[0]}-{victorias(p['duels'])[1]}" + (f"-{victorias(p['duels'])[2]}e" if victorias(p['duels'])[2] else "") for a, p in sorted(espejos)))
        out.append("")

    out.append("### Cruces fuera de 40-60 % (a repetir a 30 duelos: `/mobaduel a-b 30 " + pasada.replace("sin_items", "plain") + "`)")
    out.append("")
    if fuera:
        for _, a, b, wa, wb, e, n in sorted(fuera, reverse=True):
            out.append(f"- {etiqueta(a)} {wa} - {wb} {etiqueta(b)}" + (f" ({e} empates)" if e else "") + f" de {n}  → `/mobaduel {etiqueta(a).lower()}-{etiqueta(b).lower()} 30 {'plain' if pasada == 'sin_items' else 't3'}`")
    else:
        out.append("_Ninguno: todos los cruces están dentro de la meta._")
    out.append("")

    out.append("### De dónde sale el daño (promedio por duelo, todas las skills y el básico)")
    out.append("")
    por_clase = {c: {} for c in clases}
    duelos_clase = {c: 0 for c in clases}
    for (a, b), p in cruces.items():
        for x in p["duels"]:
            for lado, c in (("aDamage", a), ("bDamage", b)):
                duelos_clase[c] += 1
                for s, v in x[lado].items():
                    por_clase[c][int(s)] = por_clase[c].get(int(s), 0) + v
    for c in clases:
        n = max(1, duelos_clase[c])
        orden = sorted(por_clase[c].items(), key=lambda kv: -kv[1])[:6]
        total = sum(por_clase[c].values()) or 1
        out.append(f"- **{etiqueta(c)}**: " + ", ".join(f"{'básico' if s == 0 else '#' + str(s)} {100 * v / total:.0f} %" for s, v in orden) + f" · daño medio por duelo {total / n:,.0f}".replace(",", "."))
    out.append("")
    return out


def main():
    carpeta = Path(sys.argv[1]) if len(sys.argv) > 1 else RAIZ / "src/Startup/bin/Debug"
    out = ["# INFORME DE DUELOS 1v1", "",
           "Generado por `tools/informe_duelos.py` a partir de `/mobaduel` (servidor, sin cliente, IA real de `MobaBotPlayer`, nivel 30, skills a rango 5).",
           "Meta del balance: **4-6 victorias de 10** en cada cruce.", ""]
    for pasada in ("sin_items", "t3"):
        out += seccion(carpeta, pasada)
    destino = RAIZ / "INFORME_DUELOS.md"
    destino.write_text("\n".join(out) + "\n", encoding="utf-8")
    print(f"escrito {destino}")


if __name__ == "__main__":
    main()
