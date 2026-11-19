"""Calibra f_real contra las matrices de duelos REALES de INFORME_DUELOS.md (nivel 30, rango 5, 10 duelos por cruce, sin items y con T3).
Ajusta un factor de kit por clase (y el ruido del duelo) para que el simulador reproduzca las victorias medidas. Uso: python -I -X utf8 calibrar.py [--fit]"""
import json, re, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
import sim

REPORT_ORDER = ["GM", "BK", "FE", "MG", "DL", "SUM", "RF"]       # orden del informe real
MY = sim.CLASS_NAMES                                              # BK RF DL MG FE GM SUM


def parse_real(path):
    txt = Path(path).read_text(encoding="utf-8")
    out = {}
    for name, key in (("sin_items", "## Sin ítems"), ("t3", "## Con set completo T3")):
        sec = txt[txt.index(key):]
        rows = [l for l in sec.splitlines() if l.startswith("| **")][:7]
        W = np.full((7, 7), np.nan)
        for i, line in enumerate(rows):
            cells = [c.strip() for c in line.strip().strip("|").split("|")][1:]
            for j, c in enumerate(cells):
                m = re.match(r"(\d+)-(\d+)(?:-(\d+)e)?", c)
                if not m:
                    continue
                w, l, e = int(m.group(1)), int(m.group(2)), int(m.group(3) or 0)
                W[i, j] = (w + e / 2) / max(1, w + l + e)
        # promedia (a,b) con 1-(b,a); espejos quedan en 0.5
        P = np.full((7, 7), 0.5)
        for i in range(7):
            for j in range(7):
                if i == j:
                    continue
                vals = [v for v in (W[i, j], 1 - W[j, i]) if not np.isnan(v)]
                P[i, j] = float(np.mean(vals)) if vals else 0.5
        # reordena al orden del simulador
        idx = [REPORT_ORDER.index(c) for c in MY]
        out[name] = P[np.ix_(idx, idx)]
    return out


def parse_durations(path):
    """Duracion media (s) de los duelos por clase y pasada, de la tabla 'Totales por clase' del informe real."""
    txt = Path(path).read_text(encoding="utf-8")
    out = {}
    for name, key in (("sin_items", "## Sin ítems"), ("t3", "## Con set completo T3")):
        sec = txt[txt.index(key):]
        d = {}
        for line in sec.splitlines():
            m = re.match(r"\|\s*[A-Za-z ]+\((\w+)\)\s*\|\s*\d+\s*\|\s*\d+ %\s*\|\s*(\d+)\s*\|", line)
            if m and m.group(1) in MY:
                d[m.group(1)] = float(m.group(2))
            if len(d) == 7:
                break
        out[name] = np.array([d[c_] for c_ in MY])
    return out


def harness_builds(cls, passname, reps):
    n = reps
    L = 30
    base = sim.CLS_STAT_TOTAL[cls]
    prim = sim.CLS_PRIMARY[cls]
    stats = np.zeros((n, 5))
    stats[:, prim] = min(30000.0, 0.35 * base)
    stats[:, 3] = min(30000.0, 0.35 * base)
    stats[:, 1] = min(30000.0, 0.30 * base)
    tree = np.zeros((n, 6))
    offcap = [100, 64, 100, 100, 80, 80, 100][cls]
    tree[:, 0] = offcap
    tree[:, 4] = (145 - offcap) / 2
    tree[:, 5] = (145 - offcap) / 2
    tier = np.zeros((n, 11), dtype=np.int64)
    role = np.zeros((n, 11), dtype=np.int64)
    if passname == "t3":
        tier[:, 0] = 3
        if cls not in (4, 1):
            tier[:, 1] = 3
        for s in (2, 3, 4, 5, 6):
            tier[:, s] = 3
        if cls == 3:
            tier[:, 2] = 0
        if cls == 1:
            tier[:, 5] = 0
        tier[:, 9], tier[:, 10], tier[:, 8] = 3, 3, 3
        role[:, [2, 3, 6]] = 1
        role[:, 9], role[:, 10], role[:, 8] = 0, 1, 2
        role[:, 1] = 1
    arr = lambda v, dt=float: np.full(n, v, dtype=dt)
    return sim.Builds(L=arr(L, np.int64), cls=arr(cls, np.int64), stats=stats, stat_budget=arr(base), tree=tree, tree_budget=arr(145.0), tier=tier, role=role,
                      pet=np.zeros((n, 4)), buff=np.zeros((n, 15), dtype=np.int64), rank=arr(5.0), a=np.tile([0.4, 0.4, 0.2], (n, 1)),
                      label=arr(2, np.int64), gold=arr(60000.0))


def predicted(mod, p, passname, reps, rng):
    B = [harness_builds(c, passname, reps) for c in range(7)]
    O = [sim.outputs(mod, b, p) for b in B]
    P = np.full((7, 7), 0.5)
    TT = np.zeros((7, 7))
    for i in range(7):
        for j in range(7):
            if i == j:
                continue
            res = sim.fight(O[i][0], O[i][1], O[j][0], O[j][1], B[i].cls, B[j].cls, rng)
            P[i, j] = float(res["a_wins"].mean())
            tt = res["ttk"]
            TT[i, j] = float(np.mean(np.where(np.isfinite(tt), tt, sim.FIGHT_TMAX)))
    predicted.last_ttk = TT
    return P


def cost(mod, p, real, reps=300, seed=3):
    rng = np.random.default_rng(seed)
    tot, cnt, dur = 0.0, 0, 0.0
    for name in ("sin_items", "t3"):
        P = predicted(mod, p, name, reps, rng)
        m = ~np.eye(7, dtype=bool)
        tot += float(np.sum((P[m] - real[name][m]) ** 2))
        cnt += int(m.sum())
        pred_dur = np.array([np.mean(np.delete(predicted.last_ttk[i], i)) for i in range(7)])
        dur += float(np.mean(((pred_dur - REAL_DUR[name]) / REAL_DUR[name]) ** 2))
    return tot / cnt + 0.15 * dur / 2


if __name__ == "__main__":
    real = parse_real(Path(__file__).resolve().parent.parent.parent / "INFORME_DUELOS.md")
    REAL_DUR = parse_durations(Path(__file__).resolve().parent.parent.parent / "INFORME_DUELOS.md")
    mod = sim.load_formula("f_real")
    p = sim.default_params(mod)
    if "--fit" in sys.argv:
        sigma_grid = [0.04]
        best = (1e9, None, None)
        for sg in sigma_grid:
            sim.NOISE_SIGMA = sg
            q = dict(p)
            cur = cost(mod, q, real)
            for rnd in range(5):
                for k in ["hps"] + [f"kit_{c}" for c in sim.CLASS_NAMES]:
                    for f in (0.6, 0.8, 0.9, 1.1, 1.25, 1.6):
                        q2 = dict(q)
                        q2[k] = float(np.clip(q[k] * f, mod.PARAMS[k][1], mod.PARAMS[k][2]))
                        v = cost(mod, q2, real)
                        if v < cur:
                            cur, q = v, q2
            if cur < best[0]:
                best = (cur, sg, q)
            print(f"sigma={sg} coste={cur:.4f}", flush=True)
        sim.NOISE_SIGMA = best[1]
        p = best[2]
        Path("calibracion.json").write_text(json.dumps({"sigma": best[1], "params": p, "coste": best[0]}, indent=1), encoding="utf-8")
    elif Path("calibracion.json").exists():
        c = json.loads(Path("calibracion.json").read_text(encoding="utf-8"))
        sim.NOISE_SIGMA, p = c["sigma"], c["params"]
    rng = np.random.default_rng(9)
    for name in ("sin_items", "t3"):
        P = predicted(mod, p, name, 600, rng)
        m = ~np.eye(7, dtype=bool)
        r = np.corrcoef(P[m], real[name][m])[0, 1]
        sign = np.mean(((P[m] > 0.5) == (real[name][m] > 0.5)))
        print(f"\n== {name}: correlacion {r:.2f}, MAE {np.mean(np.abs(P[m]-real[name][m])):.3f}, acierta el ganador en {100*sign:.0f} % de los cruces")
        print("real (% victorias de la fila):")
        print("      " + " ".join(f"{c:>4}" for c in MY))
        for i, c in enumerate(MY):
            print(f"{c:>5} " + " ".join(f"{100*real[name][i,j]:4.0f}" for j in range(7)))
        print("simulado:")
        for i, c in enumerate(MY):
            print(f"{c:>5} " + " ".join(f"{100*P[i,j]:4.0f}" for j in range(7)))
        pd_ = [np.mean(np.delete(predicted.last_ttk[i], i)) for i in range(7)]
        print("duracion media por clase s (real | simulado):", {c_: (int(REAL_DUR[name][i]), round(float(pd_[i]))) for i, c_ in enumerate(MY)})
        print("% de victorias por clase (real | simulado):", {c: (round(100*np.mean(np.delete(real[name][i], i))), round(100*np.mean(np.delete(P[i], i)))) for i, c in enumerate(MY)})
