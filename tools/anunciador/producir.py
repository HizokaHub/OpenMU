"""Anunciador v2: voz con capas y caracter + diseno sonoro por eventos (sintesis propia, sin muestras de terceros).

Uso: python -I producir2.py <carpeta_voz_cruda> <carpeta_salida> [clave ...]
"""
import sys
import wave
from pathlib import Path

import numpy as np
from scipy import signal

SR = 44100
PEAK = 0.92
TARGET_RMS_DB = -17.0
DURACION_MAX_DEFECTO = 3.4          # segundos; las voces de partida o de objetivo gordo pueden durar mas
DURACION_MAX = {'victory': 6.0, 'defeat': 5.2, 'match_start': 5.0, 'ace': 4.6, 'legendary': 4.2, 'penta': 4.2, 'golden_flyover': 5.2, 'golden_invaders': 4.8, 'dragon_ally': 4.6, 'dragon_enemy': 4.4}
RNG = np.random.default_rng(5252)


# ------------------------------------------------------------------ utilidades

def tt(seg):
    return np.arange(int(SR * seg)) / SR


def pad(x, n):
    if len(x) >= n:
        return x[:n]
    return np.concatenate([x, np.zeros(n - len(x))])


def place(base, x, t, gain=1.0):
    i = int(t * SR)
    fin = i + len(x)
    if fin > len(base):
        base = np.concatenate([base, np.zeros(fin - len(base))])
    base[i:fin] += x * gain
    return base


def sos(x, kind, f, order=2):
    fn = f / (SR / 2) if np.isscalar(f) else [v / (SR / 2) for v in f]
    s = signal.butter(order, fn, btype=kind, output="sos")
    return signal.sosfilt(s, x)


def lp(x, f, order=2):
    return sos(x, "low", min(f, SR / 2 - 100), order)


def hp(x, f, order=2):
    return sos(x, "high", f, order)


def bp(x, lo, hi, order=2):
    return sos(x, "band", [lo, min(hi, SR / 2 - 100)], order)


def env(t, ataque, caida):
    return np.minimum(t / max(ataque, 1e-4), 1.0) * np.exp(-t / caida)


def norm(x, pico=PEAK):
    m = np.max(np.abs(x))
    return x * (pico / m) if m > 0 else x


def trim(x, umbral=0.004):
    idx = np.where(np.abs(x) > umbral)[0]
    return x[idx[0]:idx[-1] + 1] if len(idx) else x


def resample(x, factor):
    # factor < 1: mas grave y mas lento
    return signal.resample_poly(x, int(round(SR / factor)), SR)


def soft(x, drive):
    return np.tanh(x * drive) / np.tanh(drive)


def hall(x, seg=2.6, wet=0.25, predelay=0.03, tone=3800):
    n = int(SR * seg)
    t = np.arange(n) / SR
    ir = RNG.standard_normal(n) * np.exp(-t * (6.5 / seg) * 1.6)
    ir = lp(ir, tone)
    ir = hp(ir, 180)
    # reflexiones tempranas
    for d, g in [(0.011, .6), (0.019, .5), (0.029, .45), (0.041, .35), (0.057, .3)]:
        ir[int(d * SR)] += g * np.max(np.abs(ir)) * 3
    ir /= np.max(np.abs(ir))
    pre = int(predelay * SR)
    total = len(x) + n + pre
    seco = pad(x, total)
    conv = signal.fftconvolve(x, ir)
    mojado = np.zeros(total)
    mojado[pre:pre + len(conv)] = conv[:total - pre]
    mojado = norm(mojado, np.max(np.abs(x)) + 1e-9)
    return (1 - wet) * seco + wet * mojado


def compress(x, umbral=0.25, ratio=3.2, ataque=0.004, relajo=0.12):
    # compresor de avance con seguidor de envolvente (por bloques)
    a_a = np.exp(-1 / (ataque * SR))
    a_r = np.exp(-1 / (relajo * SR))
    absx = np.abs(x)
    nivel = np.zeros_like(x)
    prev = 0.0
    paso = 64
    for i in range(0, len(x), paso):
        pico = absx[i:i + paso].max()
        coef = a_a if pico > prev else a_r
        prev = coef * prev + (1 - coef) * pico
        nivel[i:i + paso] = prev
    ganancia = np.ones_like(x)
    sobre = nivel > umbral
    ganancia[sobre] = (umbral + (nivel[sobre] - umbral) / ratio) / nivel[sobre]
    return x * ganancia


def limit(x, techo=PEAK):
    return np.clip(soft(x, 1.0) * (techo / np.tanh(1.0)) * 0.98, -techo, techo)


# ------------------------------------------------------------------ instrumentos

def sub_drop(seg=1.0, f0=130.0, f1=34.0, caida=0.4):
    t = tt(seg)
    f = f1 + (f0 - f1) * np.exp(-t * 9)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * env(t, 0.002, caida)


def timpani(f=70.0, seg=0.9):
    t = tt(seg)
    f_t = f * (1 + 0.6 * np.exp(-t * 35))
    cuerpo = np.sin(2 * np.pi * np.cumsum(f_t) / SR) * env(t, 0.001, 0.28)
    golpe = lp(RNG.standard_normal(len(t)), 700) * env(t, 0.0005, 0.03)
    resonancia = np.sin(2 * np.pi * f * 1.5 * t) * env(t, 0.002, 0.18) * 0.25
    return cuerpo + 0.7 * golpe + resonancia


def roll(f, seg, golpes, ritmo_fin=0.06):
    # redoble de timbal que acelera
    y = np.zeros(int(seg * SR))
    t = 0.0
    paso = seg / golpes
    for k in range(golpes):
        g = 0.35 + 0.65 * (k / max(golpes - 1, 1))
        y = place(y, timpani(f, 0.45), t, g)
        t += max(paso * (1 - 0.55 * k / golpes), ritmo_fin)
        if t >= seg:
            break
    return y


def crash(seg=1.6, caida=0.5):
    t = tt(seg)
    r = hp(RNG.standard_normal(len(t)), 2800)
    r = r * env(t, 0.002, caida)
    return r + 0.4 * bp(RNG.standard_normal(len(t)), 5000, 9500) * env(t, 0.004, caida * 1.4)


def clang(seg=0.7, base=1.0):
    t = tt(seg)
    y = np.zeros_like(t)
    for f, g, c in [(1517, 1.0, 0.22), (2437, .7, 0.17), (3797, .5, 0.12), (5233, .35, 0.09), (6811, .2, 0.06)]:
        y += g * np.sin(2 * np.pi * f * base * t + RNG.uniform(0, 6.28)) * env(t, 0.0006, c)
    return y * 0.5


def gong(f=75.0, seg=3.2):
    t = tt(seg)
    y = np.zeros_like(t)
    for ratio, g, c in [(1, 1.0, 2.2), (1.47, .6, 1.8), (2.09, .5, 1.5), (2.56, .38, 1.2), (3.12, .3, 1.0), (4.2, .2, .8), (5.4, .12, .6)]:
        batido = 1 + 0.0015 * RNG.standard_normal()
        y += g * np.sin(2 * np.pi * f * ratio * batido * t + RNG.uniform(0, 6.28)) * env(t, 0.004, c)
    golpe = hp(RNG.standard_normal(len(t)), 900) * env(t, 0.001, 0.25) * 0.25
    return (y + golpe) * 0.6


def saw(freq, seg, vib=0.0):
    t = tt(seg)
    mod = 1 + vib * np.sin(2 * np.pi * 5.3 * t + RNG.uniform(0, 6.28))
    fase = np.cumsum(freq * mod) / SR
    return 2 * (fase - np.floor(fase + 0.5))


def brass(freq, seg=0.9, ataque=0.05, brillo=0.7, caida=None):
    caida = caida or seg * 0.45
    t = tt(seg)
    v = sum(saw(freq * d, seg, 0.004) for d in (0.994, 1.0, 1.006)) / 3
    oscuro, claro = lp(v, 650), lp(v, 5200)
    barrido = np.clip(t / (ataque * 2.2), 0, 1) ** 0.7 * brillo
    y = oscuro * (1 - barrido) + claro * barrido
    return y * env(t, ataque, caida)


def chord(freqs, seg, **kw):
    return sum(brass(f, seg, **kw) for f in freqs) / np.sqrt(len(freqs))


def choir(freqs, seg=1.8, ataque=0.5, vocal="a"):
    formantes = {"a": [(730, 1.0), (1090, .55), (2440, .25)], "o": [(570, 1.0), (840, .5), (2410, .2)]}[vocal]
    t = tt(seg)
    y = np.zeros_like(t)
    for f in freqs:
        v = sum(saw(f * d, seg, 0.006) for d in (0.99, 1.0, 1.012)) / 3
        y += sum(g * bp(v, fc * 0.85, fc * 1.18) for fc, g in formantes)
    amp = np.minimum(t / ataque, 1.0) * np.minimum((seg - t) / (seg * 0.35), 1.0)
    return y * amp / np.sqrt(len(freqs))


def riser(seg=0.7, f0=180.0, f1=2600.0):
    t = tt(seg)
    ruido = RNG.standard_normal(len(t))
    y = np.zeros_like(t)
    paso = 441
    for i in range(0, len(t), paso):
        c = f0 * (f1 / f0) ** (i / len(t))
        y[i:i + paso] = bp(ruido[i:i + paso], c * 0.6, c * 1.5)
    tono = np.sin(2 * np.pi * np.cumsum(f0 * 2 * (f1 / f0) ** (t / seg)) / SR) * 0.25
    return (y * 2.2 + tono) * (t / seg) ** 1.8


def shatter(seg=0.9):
    t = tt(seg)
    y = np.zeros_like(t)
    for _ in range(46):
        ini = RNG.uniform(0, seg * 0.55)
        f = RNG.uniform(3500, 9500)
        i = int(ini * SR)
        n = int(0.06 * SR)
        y[i:i + n] += np.sin(2 * np.pi * f * tt(0.06)[:len(y[i:i + n])]) * np.exp(-tt(0.06)[:len(y[i:i + n])] * 60) * RNG.uniform(0.2, 1)
    return y * 0.6 + 0.3 * hp(RNG.standard_normal(len(t)), 5000) * env(t, 0.001, 0.12)


def roar(seg=1.6):
    t = tt(seg)
    f = 62 + 40 * np.sin(np.pi * np.minimum(t / seg, 1)) + 12 * lp(RNG.standard_normal(len(t)), 20) * 8
    f = np.clip(f, 35, 200)
    base = 2 * ((np.cumsum(f) / SR) % 1) - 1
    gruñido = base * (0.55 + 0.45 * np.sin(2 * np.pi * 27 * t))
    centro = 400 + 700 * np.sin(np.pi * t / seg)
    y = np.zeros_like(t)
    paso = 441
    for i in range(0, len(t), paso):
        c = centro[i]
        y[i:i + paso] = bp(gruñido[i:i + paso], c * 0.7, c * 1.6)
    y = soft(y * 3, 2.5) + 0.35 * lp(RNG.standard_normal(len(t)), 900)
    return y * np.minimum(t / 0.12, 1) * np.minimum((seg - t) / 0.5, 1)


def horn(seg, f=98.0, brillo=0.5, ataque=0.25):
    return brass(f, seg, ataque=ataque, brillo=brillo, caida=seg * 3) * 0.9


def alarm(pulsos=3):
    y = np.zeros(int(0.95 * SR))
    for k in range(pulsos):
        y = place(y, horn(0.22, 392 if k % 2 == 0 else 311, 0.9, 0.01), k * 0.3, 0.8)
        y = place(y, timpani(60, 0.3), k * 0.3, 0.6)
    return y


def shimmer(seg=2.0):
    t = tt(seg)
    y = np.zeros_like(t)
    for f in (1318, 1568, 1975, 2637, 3136):
        y += np.sin(2 * np.pi * f * t + RNG.uniform(0, 6.28)) * (0.5 + 0.5 * np.sin(2 * np.pi * RNG.uniform(3, 7) * t)) * env(t, 0.3, seg * 0.5) * 0.18
    return y


# ------------------------------------------------------------------ voz

def voz(ruta):
    with wave.open(str(ruta)) as w:
        datos = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64) / 32768.0
        tasa = w.getframerate()
        if w.getnchannels() > 1:
            datos = datos.reshape(-1, w.getnchannels()).mean(axis=1)
    if tasa != SR:
        datos = signal.resample_poly(datos, SR, tasa)
    x = norm(trim(datos), 0.9)

    principal = resample(x, 0.84)                       # cuerpo: voz grave de gigante
    sub = lp(resample(x, 0.60), 700) * 0.55             # capa de pecho, muy grave
    aire = hp(resample(x, 1.10), 2800) * 0.18           # presencia / aire
    n = max(len(principal), len(sub), len(aire))
    mezcla = pad(principal, n) + pad(sub, n) + pad(aire, n)

    # grano "demoniaco": copia aplastada (reduccion de muestreo + cuantizacion), muy baja
    grano = np.repeat(mezcla[::4], 4)[:n]
    grano = np.round(grano * 24) / 24
    mezcla += 0.06 * bp(grano, 350, 3000)

    # doble voz (chorus corto) para ancho y carácter
    doble = pad(resample(mezcla, 0.992), n) * 0.30
    mezcla = mezcla + np.concatenate([np.zeros(int(0.022 * SR)), doble])[:n]

    # ecualizacion: pecho, presencia, recorte de aire digital
    mezcla = mezcla + 0.5 * lp(mezcla, 220) + 0.30 * bp(mezcla, 2000, 4200)
    mezcla = lp(hp(mezcla, 70), 11000)
    mezcla = soft(norm(mezcla, 0.8), 2.4)
    mezcla = compress(mezcla, 0.28, 3.5)

    # eco corto + sala
    eco = np.concatenate([np.zeros(int(0.13 * SR)), mezcla])[:n] * 0.16
    mezcla = pad(mezcla, n) + eco
    mezcla = hall(mezcla, 2.4, 0.26, 0.02)
    return norm(trim(mezcla, 0.003), 0.95)


# ------------------------------------------------------------------ recetas por evento

def impacto(t0, base, y, poder=1.0, f=65.0, acorde=None):
    y = place(y, sub_drop(1.1 + 0.2 * poder), t0, 0.95)
    y = place(y, timpani(f), t0, 0.9)
    y = place(y, crash(0.8 + 0.3 * poder, 0.25 + 0.06 * poder), t0, 0.16 * poder)
    y = place(y, clang(0.6), t0, 0.35)
    if acorde:
        y = place(y, chord(acorde, 0.75 + 0.15 * poder, brillo=0.8), t0, 0.5)
    return y


def r_first_blood():
    y = np.zeros(int(0.4 * SR))
    y = place(y, riser(0.55), 0.0, 0.7)
    y = impacto(0.55, None, y, 1.0, 62, [98, 147, 196])
    return y, 0.62


def r_multi(n):
    def receta():
        y = np.zeros(int(0.4 * SR))
        y = place(y, riser(0.5 + 0.06 * n, 160, 2800), 0.0, 0.7)
        t0 = 0.5 + 0.06 * n
        y = impacto(t0, None, y, 0.8 + 0.3 * n, 62 - 3 * n, [98, 147, 196, 247][: 2 + (n > 2) + (n > 3)])
        if n >= 3:
            y = place(y, roll(70, 0.7, 6 + 2 * n), t0 + 0.1, 0.5)
        if n >= 4:
            y = place(y, choir([98, 147, 196], 1.9, 0.5), t0, 0.4)
        if n >= 5:
            y = place(y, gong(70, 3.4), t0, 0.8)
            y = place(y, shimmer(2.2), t0, 0.5)
            y = place(y, chord([196, 247, 294, 392], 1.6, brillo=0.95), t0 + 0.3, 0.5)
        return y, t0 + 0.1
    return receta


def r_ace():
    y = np.zeros(int(0.4 * SR))
    y = place(y, riser(0.9, 140, 3200), 0.0, 0.8)
    y = impacto(0.9, None, y, 2.0, 55, [73, 110, 147, 196])
    y = place(y, gong(62, 3.6), 0.9, 0.9)
    y = place(y, roll(60, 1.0, 14), 1.0, 0.5)
    return y, 1.0


def r_ace_lost():
    y = np.zeros(int(0.4 * SR))
    y = place(y, gong(52, 3.8), 0.0, 1.0)
    y = place(y, sub_drop(1.6, 90, 28, 0.8), 0.0, 1.0)
    y = place(y, choir([98, 117, 147], 2.2, 0.6, "o"), 0.1, 0.5)
    return y, 0.35


def r_shutdown():
    y = np.zeros(int(0.4 * SR))
    y = place(y, shatter(1.0), 0.0, 0.9)
    y = impacto(0.05, None, y, 1.2, 58, [87, 131])
    return y, 0.3


def r_streak(n):
    def receta():
        y = np.zeros(int(0.4 * SR))
        for k in range(n):
            y = place(y, chord([98 * 2 ** (k / 6), 147 * 2 ** (k / 6)], 0.45, ataque=0.02, brillo=0.9), 0.18 * k, 0.5)
            y = place(y, timpani(70 + 6 * k), 0.18 * k, 0.8)
        t0 = 0.18 * n
        y = impacto(t0, None, y, 0.9 + 0.25 * n, 60, [110, 165, 220])
        if n >= 3:
            y = place(y, choir([110, 165, 220], 1.8, 0.45), t0, 0.35)
        if n >= 4:
            y = place(y, gong(66, 3.0), t0, 0.8)
            y = place(y, shimmer(2.0), t0, 0.4)
        return y, t0 + 0.08
    return receta


def r_turret_ally():
    y = np.zeros(int(0.4 * SR))
    y = place(y, gong(58, 3.0), 0.0, 0.9)
    y = place(y, sub_drop(1.4, 110, 26, 0.7), 0.0, 1.0)
    caida = lp(RNG.standard_normal(int(1.4 * SR)), 420) * env(tt(1.4), 0.01, 0.5)
    y = place(y, caida, 0.0, 0.7)
    return y, 0.25


def r_turret_enemy():
    y = np.zeros(int(0.4 * SR))
    y = impacto(0.0, None, y, 1.1, 64, [98, 147, 196])
    y = place(y, lp(RNG.standard_normal(int(1.2 * SR)), 520) * env(tt(1.2), 0.01, 0.45), 0.0, 0.5)
    return y, 0.28


def r_nexus_attack():
    y = alarm(3)
    y = place(y, sub_drop(1.0, 80, 30, 0.45), 0.0, 0.7)
    return y, 0.55


def r_match_start():
    y = np.zeros(int(0.4 * SR))
    y = place(y, horn(1.5, 98, 0.6, 0.35), 0.0, 0.9)
    y = place(y, horn(1.5, 147, 0.6, 0.4), 0.1, 0.6)
    y = place(y, roll(75, 1.0, 12), 0.5, 0.7)
    y = impacto(1.45, None, y, 1.4, 62, [98, 147, 196, 294])
    y = place(y, gong(70, 3.2), 1.45, 0.6)
    return y, 1.55


def r_victory():
    y = np.zeros(int(0.4 * SR))
    y = place(y, roll(70, 1.1, 18), 0.0, 0.6)
    y = place(y, riser(1.0, 160, 3000), 0.0, 0.6)
    t0 = 1.05
    y = impacto(t0, None, y, 1.8, 58, [98, 147, 196, 247])
    notas = [(392, 0.0, 0.32), (494, 0.3, 0.32), (587, 0.6, 0.32), (784, 0.9, 1.9)]
    for f, ini, dur in notas:
        y = place(y, chord([f, f * 1.5, f * 2], dur, ataque=0.03, brillo=0.95), t0 + ini + 0.1, 0.6)
    y = place(y, choir([196, 247, 294, 392, 494], 3.0, 0.5), t0 + 0.9, 0.55)
    y = place(y, gong(66, 4.0), t0, 0.9)
    y = place(y, shimmer(3.0), t0 + 0.9, 0.6)
    return y, 1.05


def r_defeat():
    y = np.zeros(int(0.4 * SR))
    y = place(y, sub_drop(2.0, 100, 24, 0.9), 0.0, 1.0)
    y = place(y, gong(50, 4.4), 0.0, 1.0)
    notas = [(262, 0.0, 0.7), (247, 0.6, 0.7), (220, 1.2, 0.7), (165, 1.8, 2.4)]
    for f, ini, dur in notas:
        y = place(y, chord([f, f * 1.2, f * 1.5], dur, ataque=0.12, brillo=0.35), ini, 0.4)
    y = place(y, choir([110, 131, 165], 3.4, 0.7, "o"), 0.2, 0.55)
    return y, 0.5


def r_golden_flyover():
    y = np.zeros(int(0.4 * SR))
    y = place(y, roar(1.8), 0.0, 0.8)
    y = place(y, shimmer(3.0), 0.2, 0.8)
    y = place(y, choir([196, 247, 294, 392], 2.6, 0.7), 0.3, 0.5)
    return y, 0.9


def r_golden_invaders():
    y = np.zeros(int(0.4 * SR))
    y = place(y, horn(1.2, 110, 0.7, 0.2), 0.0, 0.9)
    y = place(y, roar(1.5), 0.1, 0.7)
    for k in range(4):
        y = place(y, timpani(58, 0.6), 0.3 + 0.28 * k, 0.9)
    y = impacto(1.45, None, y, 1.4, 60, [110, 165, 220])
    return y, 1.45


def r_dragon_ally():
    y = np.zeros(int(0.4 * SR))
    y = place(y, roar(1.5), 0.0, 0.7)
    y = impacto(1.2, None, y, 1.6, 62, [98, 147, 196, 294])
    y = place(y, choir([196, 247, 294], 2.2, 0.5), 1.2, 0.5)
    y = place(y, shimmer(2.2), 1.3, 0.5)
    return y, 1.3


def r_dragon_enemy():
    y = np.zeros(int(0.4 * SR))
    y = place(y, roar(1.5), 0.0, 0.7)
    y = place(y, gong(55, 3.2), 1.1, 0.9)
    y = place(y, sub_drop(1.4, 90, 28, 0.6), 1.1, 0.9)
    return y, 1.3


RECETAS = {
    "first_blood": r_first_blood,
    "double": r_multi(2), "triple": r_multi(3), "quadra": r_multi(4), "penta": r_multi(5),
    "ace": r_ace, "ace_lost": r_ace_lost, "shutdown": r_shutdown,
    "spree": r_streak(1), "dominating": r_streak(2), "unstoppable": r_streak(3), "legendary": r_streak(4),
    "turret_ally": r_turret_ally, "turret_enemy": r_turret_enemy,
    "nexus_attack": r_nexus_attack, "match_start": r_match_start,
    "victory": r_victory, "defeat": r_defeat,
    "golden_flyover": r_golden_flyover, "golden_invaders": r_golden_invaders,
    "dragon_ally": r_dragon_ally, "dragon_enemy": r_dragon_enemy,
}


def envolvente_voz(v, suavizado=0.06):
    e = np.abs(v)
    k = int(suavizado * SR)
    e = np.convolve(e, np.ones(k) / k, mode="same")
    return np.clip(e / (np.max(e) + 1e-9) * 2.0, 0, 1)


def mezclar(nombre, ruta_voz):
    efecto, t_voz = RECETAS[nombre]()
    v = voz(ruta_voz)
    total = max(len(efecto), int((t_voz + 0.0) * SR) + len(v)) + int(0.2 * SR)
    efecto = pad(efecto, total)
    cobertura = np.zeros(total)
    i = int(t_voz * SR)
    cobertura[i:i + len(v)] = envolvente_voz(v)
    cobertura = np.convolve(cobertura, np.ones(int(0.08 * SR)) / int(0.08 * SR), mode="same")
    efecto = efecto * (1 - 0.55 * cobertura)         # la voz "se abre paso" bajando el fondo
    mezcla = efecto * 0.85
    mezcla = place(mezcla, v, t_voz, 1.15)
    mezcla = hall(mezcla, 2.2, 0.12, 0.0)
    mezcla = lp(hp(mezcla, 28), 11000)
    mezcla = compress(mezcla, 0.35, 2.6, 0.01, 0.2)
    mezcla = trim(mezcla, 0.004)
    tope = int(DURACION_MAX.get(nombre, DURACION_MAX_DEFECTO) * SR)
    if len(mezcla) > tope:
        mezcla = mezcla[:tope]
        corte = int(0.5 * SR)
        mezcla[-corte:] *= np.linspace(1, 0, corte) ** 1.5
    # nivel parejo entre todos los sonidos
    activo = mezcla[np.abs(mezcla) > 0.01]
    rms = np.sqrt(np.mean(activo ** 2)) if len(activo) else 0.1
    mezcla = mezcla * (10 ** (TARGET_RMS_DB / 20) / rms)
    mezcla = limit(mezcla)
    # fade-out final suave
    f = int(0.25 * SR)
    mezcla[-f:] *= np.linspace(1, 0, f)
    return mezcla


def escribir(ruta, x):
    pcm = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(str(ruta), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def main():
    crudo, salida = Path(sys.argv[1]), Path(sys.argv[2])
    solo = set(sys.argv[3:])
    salida.mkdir(parents=True, exist_ok=True)
    for nombre in RECETAS:
        if solo and nombre not in solo:
            continue
        ruta = crudo / f"{nombre}.wav"
        if not ruta.exists():
            print("falta", nombre)
            continue
        y = mezclar(nombre, ruta)
        escribir(salida / f"{nombre}.wav", y)
        print(f"{nombre}.wav  {len(y) / SR:.2f} s  pico {np.max(np.abs(y)):.2f}")


if __name__ == "__main__":
    main()
