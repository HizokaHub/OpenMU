"""Desglosa por clase POR QUE el kit real no es neutro para F10, con los campos nuevos de los duelos (aX / bX del JSON, 2026-11-20).
Mide por clase (sin espejos): daño por segundo y de qué canal viene (basico, skills, DoT/pasivas), golpes y tamaño, crítico, curación (vampirismo),
reflejo, lo que recortan la mitigación, el tope de golpe y el freno de ráfaga cuando la clase es la víctima, y cuánto tiempo está cerca, aturdida,
lenta o sin maná.
Uso: python -I -X utf8 tools/canales_f10.py [archivo.json]   (por defecto el último duelos_sin_items_*.json con campos aX)"""
import collections, glob, json, sys
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
AL = {3: "GM", 6: "BK", 11: "FE", 12: "MG", 16: "DL", 20: "SUM", 24: "RF"}
NOMBRE_SKILL = {0: "basico", 1: "Poison", 2: "Meteorite", 3: "Lightning", 4: "FireBall", 9: "EvilSpirit", 11: "PowerWave", 17: "EnergyBall", 19: "FallingSlash",
                20: "Lunge", 21: "Uppercut", 22: "Cyclone", 24: "TripleShot", 39: "IceStorm", 41: "TwistSlash", 42: "RagefulBlow", 43: "DeathStab",
                51: "IceArrow", 52: "Penetration", 61: "FireBurst", 62: "Earthshake", 65: "ElecSpike", 66: "ForceWave", 76: "?76", 214: "DrainLife",
                215: "ChainLightning", 230: "LightningShock", 232: "StrikeDestr", 235: "MultiShot", 236: "FlameStrike", 237: "GiganticStorm",
                260: "KillingBlow", 261: "BeastUppercut", 262: "ChainDrive", 263: "DarkSide"}


def nombre(k):
    k = int(k)
    base = k % 10000
    n = NOMBRE_SKILL.get(base, f"s{base}")
    return ("DoT:" if k >= 10000 else "") + n


def ultimo():
    for f in sorted(glob.glob(str(RAIZ / "src/Startup/bin/Debug/duelos_sin_items_*.json")), reverse=True):
        txt = Path(f).read_text(encoding="utf-8")
        if '"aX"' in txt:
            return f
    raise SystemExit("ningun JSON con campos aX")


arch = sys.argv[1] if len(sys.argv) > 1 else ultimo()
d = json.load(open(arch, encoding="utf-8"))
print(Path(arch).name, "duelos por cruce", d["duelsPerPair"], "nivel", d["level"])

C = {c: dict(n=0, win=0, sec=0.0, hits=collections.defaultdict(lambda: [0, 0, 0, 0, 0, 0]), casts=collections.Counter(), heal=collections.Counter(),
             reflect=0.0, stage=[0.0] * 5, samples=0, dist=[0] * 4, dis=0, slow=0, lowm=0) for c in AL}
for p in d["pairs"]:
    a, b = p["a"], p["b"]
    if a == b:
        continue
    for x in p["duels"]:
        for cls, st, w in ((a, x.get("aX"), x["winner"] == "a"), (b, x.get("bX"), x["winner"] == "b")):
            if not st:
                continue
            s = C[cls]
            s["n"] += 1; s["win"] += w; s["sec"] += x["seconds"]
            for k, v in st["hits"].items():
                for i in range(6):
                    s["hits"][k][i] += v[i]
            for k, v in st["casts"].items():
                s["casts"][k] += v
            for k, v in st["heal"].items():
                s["heal"][k] += v
            s["reflect"] += st["reflect"]
            for i in range(5):
                s["stage"][i] += st["stage"][i]
            s["samples"] += st["samples"]
            for i in range(4):
                s["dist"][i] += st["dist"][i]
            s["dis"] += st["disabled"]; s["slow"] += st["slowed"]; s["lowm"] += st["lowMana"]

print("\n=== 1. De dónde sale el daño (por segundo de duelo) ===")
print("clase  win%  dmg/s  golpes/s  dmg/golpe | basico  skills  DoT/pasivas | crit%  | vampirismo/s  reflejo/s")
for c, s in C.items():
    if not s["n"]:
        continue
    tot = sum(v[1] for v in s["hits"].values()); cnt = sum(v[0] for v in s["hits"].values())
    bas = s["hits"]["0"][1] if "0" in s["hits"] else 0
    dot = sum(v[1] for k, v in s["hits"].items() if int(k) >= 10000)
    sk = tot - bas - dot
    crit = sum(v[4] for v in s["hits"].values())
    heal = sum(s["heal"].values())
    print(f"{AL[c]:4} {100*s['win']/s['n']:5.0f} {tot/s['sec']:7.0f} {cnt/s['sec']:8.2f} {tot/max(cnt,1):9.0f} | {100*bas/max(tot,1):5.0f}% {100*sk/max(tot,1):5.0f}% {100*dot/max(tot,1):6.0f}%     | {100*crit/max(cnt,1):4.0f}% | {heal/s['sec']:9.0f}     {s['reflect']/s['sec']:7.0f}")

print("\n=== 2. Por skill: lanzamientos/duelo, golpes/duelo, daño final/golpe, daño antes de mitigar/golpe, crítico, distancia ===")
for c, s in C.items():
    if not s["n"]:
        continue
    print(f"-- {AL[c]} ({s['n']} duelos, {s['sec']/s['n']:.1f} s)")
    filas = sorted(s["hits"].items(), key=lambda kv: -kv[1][1])
    for k, v in filas[:9]:
        casts = s["casts"].get(k, 0) / s["n"] if int(k) < 10000 else 0
        raw = v[2] / v[3] if v[3] else 0
        print(f"   {nombre(k):15} casts {casts:5.1f}  golpes {v[0]/s['n']:5.1f}  final/golpe {v[1]/max(v[0],1):8.0f}  raw/golpe {raw:8.0f}  crit {100*v[4]/max(v[0],1):3.0f}%  dist {v[5]/10/max(v[0],1):4.1f}  = {100*v[1]/max(sum(w[1] for w in s['hits'].values()),1):3.0f}% del daño")
    if s["heal"]:
        print("   curacion/duelo:", {k: round(v / s["n"]) for k, v in s["heal"].items()})

print("\n=== 3. Como victima: que recortan la mitigacion, el tope por golpe (30 % vida) y el freno de rafaga ===")
print("clase  golpes/duelo  entra(raw)  tras mitigar+crit | tope recorta | freno recorta | queda  (suma de lo que entra = 100)")
for c, s in C.items():
    if not s["n"]:
        continue
    n, rin, mit, cap, brk = s["stage"]
    print(f"{AL[c]:4} {n/s['n']:12.1f} {rin/max(n,1):10.0f} {mit/max(n,1):15.0f}      | {100*(mit-cap)/max(mit,1):6.1f} %     | {100*(cap-brk)/max(cap,1):6.1f} %      | {100*brk/max(rin,1):5.1f} % de lo que entraba")

print("\n=== 4. Posicion y control (fraccion del tiempo de duelo) ===")
print("clase  a<=2  a<=4  a<=6  a>6 tiles | aturdida  lenta  sin mana(<15%)")
for c, s in C.items():
    if not s["samples"]:
        continue
    t = s["samples"]
    print(f"{AL[c]:4} {100*s['dist'][0]/t:5.0f} {100*s['dist'][1]/t:5.0f} {100*s['dist'][2]/t:5.0f} {100*s['dist'][3]/t:5.0f}      | {100*s['dis']/t:6.1f}%  {100*s['slow']/t:5.1f}%  {100*s['lowm']/t:6.1f}%")
