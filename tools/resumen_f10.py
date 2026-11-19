"""Resume el ultimo duelos_<pasada>_*.json: winrate por clase contra el campo (sin espejos), duracion y matriz 7x7.
Uso: python -I -X utf8 tools/resumen_f10.py [pasada=sin_items] [archivo.json]"""
import glob, json, sys
from pathlib import Path
RAIZ = Path(__file__).resolve().parent.parent
AL = {3: "GM", 6: "BK", 11: "FE", 12: "MG", 16: "DL", 20: "SUM", 24: "RF"}
pasada = sys.argv[1] if len(sys.argv) > 1 else "sin_items"
arch = sys.argv[2] if len(sys.argv) > 2 else sorted(glob.glob(str(RAIZ / "src/Startup/bin/Debug" / f"duelos_{pasada}_*.json")))[-1]
d = json.load(open(arch, encoding="utf-8"))
print(Path(arch).name, {k: v for k, v in d.items() if k != "pairs"})
W, T, N, left = {}, {}, {}, {}
for p in d["pairs"]:
    a, b = p["a"], p["b"]
    for x in p["duels"]:
        w = {"a": 1.0, "b": 0.0}.get(x["winner"], 0.5)
        W[(a, b)] = W.get((a, b), 0) + w; N[(a, b)] = N.get((a, b), 0) + 1; T[(a, b)] = T.get((a, b), 0) + x["seconds"]
        left[(a, b)] = left.get((a, b), 0) + (x["aLeft"] if w == 1 else x["bLeft"] if w == 0 else 0)
cls = sorted(AL)
print("matriz (victorias de la fila / duelos):")
print("      " + " ".join(f"{AL[c]:>6}" for c in cls))
for a in cls:
    row = []
    for b in cls:
        if (a, b) in N: row.append(f"{W[(a,b)]/N[(a,b)]:6.2f}")
        elif (b, a) in N: row.append(f"{1-W[(b,a)]/N[(b,a)]:6.2f}")
        else: row.append("     -")
    print(f"{AL[a]:>5} " + " ".join(row))
tot = {}
for (a, b), n in N.items():
    if a == b: continue
    for c, wins in ((a, W[(a, b)]), (b, n - W[(a, b)])):
        t = tot.setdefault(c, [0, 0]); t[0] += wins; t[1] += n
print("winrate contra el campo:", {AL[c]: round(v[0] / v[1], 2) for c, v in sorted(tot.items())})
dur = sum(T.values()) / max(1, sum(N.values()))
print(f"duracion media {dur:.1f} s; vida media del ganador {sum(left.values())/max(1,sum(N.values())):.2f}")
cru = [(k, W[k] / N[k]) for k in N if k[0] != k[1]]
print("cruces en 40-60 %:", sum(1 for _, w in cru if .4 <= w <= .6), "de", len(cru), "| extremos (<25/>75):", sum(1 for _, w in cru if w < .25 or w > .75))
