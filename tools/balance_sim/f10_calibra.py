"""Lazo cerrado de f10 (calibracion de las curvas, NO del diseno).
Ajusta tres cosas, todas como curvas suaves de 3-6 numeros:
  psi_clase(L)  para que cada clase gane ~50 % contra el campo;
  e1(L), e2(L)  para que los tanques, los mixtos y los de dano (por su parte ofensiva omega) ganen ~50 % entre si (compensa el bono de repartir la inversion).
Uso: python -I -X utf8 f10_calibra.py [--iters 14] [--n 20000] [--start params.json] [--out resultados/f10_params.json]"""
import argparse, json, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
import sim

ap = argparse.ArgumentParser()
ap.add_argument("--modulo", default="f10_conservacion")
ap.add_argument("--iters", type=int, default=14)
ap.add_argument("--n", type=int, default=20000)
ap.add_argument("--start", default=None)
ap.add_argument("--out", default="resultados/f10_params.json")
ap.add_argument("--gain", type=float, default=0.8)
ap.add_argument("--sl", type=float, default=0.0, help="actualiza la pendiente por clase slc (experimental: no converge, apagada)")
a = ap.parse_args()
mod = sim.load_formula(a.modulo)
p = sim.default_params(mod)
if a.start:
    d = json.loads(Path(a.start).read_text(encoding="utf-8"))
    p.update(d.get("params", d))
CLS = sim.CLASS_NAMES
levels = sim.LEVELS
Lv = np.array(levels, float)
xl = (Lv - 1.0) / 29.0
rng = np.random.default_rng(5)
traj = sim.Trajectory(a.n, levels, rng)
BASIS = np.stack([np.ones_like(xl), xl, xl ** 2, np.exp(-(Lv - 1.0) / 2.5), np.exp(-(Lv - 1.0) / 8.0), np.exp(-(Lv - 1.0) / 1.2)], axis=1)
BAS3 = np.stack([np.ones_like(xl), xl, np.exp(-(Lv - 1.0) / 2.5)], axis=1)


def measure(p):
    """winrate de cada clase y de cada tercil de omega (bajo=tanque, medio, alto=cristal) contra el campo, por nivel"""
    wc = np.zeros((len(levels), 7))
    wt = np.zeros((len(levels), 3))
    ws = np.zeros((len(levels), 7))
    for k, L in enumerate(levels):
        A = traj.at(k)
        ao, do = sim.outputs(mod, A, p)
        terc = np.array([2, 0, 1])[np.clip(A.label, 0, 2)]  # etiqueta -> columna: 0 tanque, 1 mixto, 2 dano
        valid = A.label >= 0
        omk = mod.omega(A, p)
        hi = np.zeros(A.n, bool)
        for c_ in range(7):
            mc = A.cls == c_
            hi[mc] = omk[mc] > np.median(omk[mc])
        M = 60000
        ia, ib = rng.integers(0, A.n, M), rng.integers(0, A.n, M)
        f = lambda dd, ix: {kk: v[ix] for kk, v in dd.items()}
        res = sim.fight(f(ao, ia), f(do, ia), f(ao, ib), f(do, ib), A.cls[ia], A.cls[ib], rng)
        w = res["a_wins"]
        for c in range(7):
            ma, mb = A.cls[ia] == c, A.cls[ib] == c
            wc[k, c] = (w[ma].sum() + (1 - w[mb]).sum()) / max(ma.sum() + mb.sum(), 1)
        for c in range(7):
            ra, rb = (A.cls[ia] == c), (A.cls[ib] == c)
            h_a, h_b = ra & hi[ia], rb & hi[ib]
            l_a, l_b = ra & ~hi[ia], rb & ~hi[ib]
            ws[k, c] = (w[h_a].sum() + (1 - w[h_b]).sum()) / max(h_a.sum() + h_b.sum(), 1) - (w[l_a].sum() + (1 - w[l_b]).sum()) / max(l_a.sum() + l_b.sum(), 1)
        for t in range(3):
            ma, mb = (terc[ia] == t) & valid[ia], (terc[ib] == t) & valid[ib]
            wt[k, t] = (w[ma].sum() + (1 - w[mb]).sum()) / max(ma.sum() + mb.sum(), 1)
    return wc, wt, ws


psi_pt = np.zeros((len(levels), 7))
e1_pt = np.zeros(len(levels))
e2_pt = np.zeros(len(levels))
BAS3 = np.stack([np.ones_like(xl), xl, np.exp(-(Lv - 1.0) / 2.5)], axis=1)
sl_pt = np.zeros((len(levels), 7))
slope_s = (np.where(Lv < 4, 1.7, np.where(Lv < 10, 1.0, 0.6)) * 1.6)[:, None] * np.ones((1, 7))
last_s = None
for i, c in enumerate(CLS):   # arrancar desde los parametros dados
    psi_pt[:, i] = sum(p[f"psi_{s}_{c}"] * BASIS[:, j] for j, s in enumerate("abcdef"))
for i, c_ in enumerate(CLS):
    sl_pt[:, i] = sum(p[f"slc_{s}_{c_}"] * BAS3[:, j] for j, s in enumerate("abd"))
for nm, arr in (("e1", e1_pt), ("e2", e2_pt)):
    arr[:] = sum(p[f"{nm}_{s}"] * BASIS[:, j] for j, s in enumerate("abcdef"))
base_slope = np.where(Lv < 4, 1.7, np.where(Lv < 10, 1.0, 0.6))
slope_c = base_slope[:, None] * np.ones((1, 7))
slope_1 = base_slope.copy() * 0.3
slope_2 = base_slope.copy() * 1.0
last = None
best = (9.0, None)


def pack():
    for c, name in enumerate(CLS):
        coef = np.linalg.lstsq(BAS3, sl_pt[:, c], rcond=None)[0]
        for j, s in enumerate("abd"):
            p[f"slc_{s}_{name}"] = float(coef[j])
    for c, name in enumerate(CLS):
        coef = np.linalg.lstsq(BASIS, psi_pt[:, c], rcond=None)[0]
        for j, s in enumerate("abcdef"):
            p[f"psi_{s}_{name}"] = float(coef[j])
    for nm, arr in (("e1", e1_pt), ("e2", e2_pt)):
        coef = np.linalg.lstsq(BASIS, arr, rcond=None)[0]
        for j, s in enumerate("abcdef"):
            p[f"{nm}_{s}"] = float(coef[j])


for it in range(a.iters):
    pack()
    wc, wt, ws = measure(p)
    r_class = wc - 0.5
    r1 = wt[:, 2] - wt[:, 0]                      # cristales menos tanques
    r2 = wt[:, 1] - 0.5 * (wt[:, 0] + wt[:, 2])   # mixtos menos extremos
    dev = float(np.abs(r_class).mean() + 0.5 * np.abs(wt - wt.mean(axis=1, keepdims=True)).mean() + 0.3 * np.abs(ws).mean())
    print(f"pasada {it}: clases {np.abs(r_class).mean():.4f} (max {np.abs(r_class).max():.3f}) | terciles {np.abs(wt - wt.mean(axis=1, keepdims=True)).mean():.4f} | alto-bajo por clase {np.abs(ws).mean():.4f} (tanque/mixto/dano en nv30: {wt[-1].round(2)})", flush=True)
    if dev < best[0]:
        best = (dev, dict(p))
    if last is not None:
        for (cur, old_, sl) in ((psi_pt, last[0], slope_c), (e1_pt, last[1], slope_1), (e2_pt, last[2], slope_2)):
            pass
        dpsi, dwr = psi_pt - last[0], wc - last[3]
        ok = np.abs(dpsi) > 0.02
        slope_c = np.clip(np.where(ok, 0.5 * slope_c + 0.5 * dwr / np.where(ok, dpsi, 1.0), slope_c), 0.15, 3.5)
        for arr, olda, rn, oldr, nm in ((e1_pt, last[1], r1, last[4], 1), (e2_pt, last[2], r2, last[5], 2)):
            de, dr = arr - olda, rn - oldr
            okk = np.abs(de) > 0.02
            sl = slope_1 if nm == 1 else slope_2
            sl = np.clip(np.where(okk, 0.5 * sl + 0.5 * np.abs(dr / np.where(okk, de, 1.0)), sl), 0.15, 8.0)
            if nm == 1:
                slope_1 = sl
            else:
                slope_2 = sl
    if last_s is not None:
        dd_, dr_ = sl_pt - last_s[0], ws - last_s[1]
        ok_ = np.abs(dd_) > 0.02
        slope_s = np.clip(np.where(ok_, 0.5 * slope_s + 0.5 * np.abs(dr_ / np.where(ok_, dd_, 1.0)), slope_s), 0.2, 8.0)
    last_s = (sl_pt.copy(), ws.copy())
    last = (psi_pt.copy(), e1_pt.copy(), e2_pt.copy(), wc.copy(), r1.copy(), r2.copy())
    g = a.gain * (0.85 ** it)
    psi_pt = psi_pt + g * (0.5 - wc) / slope_c
    psi_pt -= psi_pt.mean(axis=1, keepdims=True)
    e1_pt = e1_pt + g * r1 / slope_1
    e2_pt = e2_pt + g * r2 / slope_2
    sl_pt = sl_pt - a.sl * g * ws / slope_s
pack()
if best[1] is not None:
    p.update(best[1])
    print("se usa la mejor pasada, desviacion", round(best[0], 4))
Path(a.out).parent.mkdir(exist_ok=True)
Path(a.out).write_text(json.dumps({k: p[k] for k in p}, indent=1), encoding="utf-8")
print("guardado", a.out)
