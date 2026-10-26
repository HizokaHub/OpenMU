"""Sonidos de los pings del MOBA (sintesis propia): uno por tipo de ping y tres de la rueda. WAV mono 44,1 kHz 16 bits.

Uso: python -I ping_sounds.py <carpeta_salida>
Claves (el cliente las carga en este orden, ver World/Moba/MobaPing.cpp):
  generic, caution, retreat, push, onmyway, allin, assist, needvision, missing, enemyvision, wheel_open, wheel_tick, denied
"""
import sys
import wave
from pathlib import Path

import numpy as np
from scipy import signal

SR = 44100
RNG = np.random.default_rng(404)
PICO = 0.80


def tt(seg):
    return np.arange(int(SR * seg)) / SR


def lp(x, f, orden=2):
    return signal.sosfilt(signal.butter(orden, f / (SR / 2), btype="low", output="sos"), x)


def hp(x, f, orden=2):
    return signal.sosfilt(signal.butter(orden, f / (SR / 2), btype="high", output="sos"), x)


def env(t, ataque, caida):
    return np.minimum(t / max(ataque, 1e-4), 1.0) * np.exp(-t / caida)


def place(base, x, t, gain=1.0):
    i = int(round(t * SR))
    fin = i + len(x)
    if fin > len(base):
        base = np.concatenate([base, np.zeros(fin - len(base))])
    base[i:fin] += x * gain
    return base


def campana(f, seg=0.8, brillo=1.0):
    t = tt(seg)
    y = np.zeros_like(t)
    for razon, g, c in [(1, 1.0, 0.45), (2.76, .5 * brillo, 0.25), (5.4, .3 * brillo, 0.14), (8.93, .15 * brillo, 0.08)]:
        y += g * np.sin(2 * np.pi * f * razon * t + RNG.uniform(0, 6.28)) * env(t, 0.002, c)
    return y * 0.5


def tono(f, seg, ataque=0.005, caida=0.2, armonicos=(1.0,)):
    t = tt(seg)
    y = sum(g * np.sin(2 * np.pi * f * (k + 1) * t) for k, g in enumerate(armonicos))
    return y * env(t, ataque, caida)


def deslizar(f0, f1, seg, vibrato=0.0):
    t = tt(seg)
    f = f0 + (f1 - f0) * (t / seg) ** 1.4
    f = f * (1 + vibrato * np.sin(2 * np.pi * 6 * t))
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * env(t, 0.01, seg * 0.6)


def sierra(f, seg):
    t = tt(seg)
    fase = np.cumsum(np.full(len(t), f)) / SR
    return 2 * (fase - np.floor(fase + 0.5))


def metal(f, seg, ataque=0.02, brillo=0.8):
    t = tt(seg)
    v = sum(sierra(f * d, seg) for d in (0.996, 1.0, 1.004)) / 3
    y = lp(v, 600) * (1 - np.clip(t / (ataque * 3), 0, 1) * brillo) + lp(v, 4200) * np.clip(t / (ataque * 3), 0, 1) * brillo
    return y * env(t, ataque, seg * 0.55)


def timbal(f=70.0, seg=0.5):
    t = tt(seg)
    ft = f * (1 + 0.6 * np.exp(-t * 35))
    return np.sin(2 * np.pi * np.cumsum(ft) / SR) * env(t, 0.001, 0.2) + 0.5 * lp(RNG.standard_normal(len(t)), 800) * env(t, 0.0005, 0.03)


def barrido_ruido(seg, subida=True):
    t = tt(seg)
    r = RNG.standard_normal(len(t))
    y = lp(r, 300) * 0
    paso = 441
    out = np.zeros_like(r)
    for i in range(0, len(t), paso):
        c = 500 + 4500 * (i / len(t)) if subida else 5000 - 4500 * (i / len(t))
        out[i:i + paso] = signal.sosfilt(signal.butter(2, [c * 0.6 / (SR / 2), min(c * 1.5, 20000) / (SR / 2)], btype="band", output="sos"), r[i:i + paso])
    curva = np.sin(np.pi * t / seg) ** 1.4
    return out * curva


def sala(x, seg=0.9, humedo=0.22):
    n = int(SR * seg)
    t = np.arange(n) / SR
    ir = hp(lp(RNG.standard_normal(n) * np.exp(-t * 7), 5000), 200)
    ir /= np.max(np.abs(ir))
    c = signal.fftconvolve(x, ir)
    seco = np.concatenate([x, np.zeros(len(c) - len(x))])
    c *= np.sqrt(np.mean(seco ** 2)) / (np.sqrt(np.mean(c ** 2)) + 1e-9)
    return (1 - humedo) * seco + humedo * c


# ------------------------------------------------------------------ los sonidos

def s_generic():
    return place(campana(880, 0.7), campana(1320, 0.5, 0.6), 0.09, 0.7)


def s_caution():
    y = np.zeros(int(0.1 * SR))
    for k, f in enumerate((740, 587, 740, 587)):
        y = place(y, tono(f, 0.2, 0.004, 0.11, (1.0, 0.5, 0.25)), 0.13 * k, 0.8)
    return y


def s_retreat():
    y = np.zeros(int(0.1 * SR))
    for k, f in enumerate((880, 660, 440)):
        y = place(y, campana(f, 0.5), 0.12 * k, 0.8)
    y = place(y, tono(70, 0.5, 0.004, 0.18), 0.0, 0.6)
    return y


def s_push():
    y = np.zeros(int(0.1 * SR))
    for k, f in enumerate((440, 554, 659)):
        y = place(y, tono(f, 0.3, 0.01, 0.12, (1.0, 0.4, 0.2)), 0.1 * k, 0.8)
    y = place(y, barrido_ruido(0.35, True), 0.0, 0.25)
    return y


def s_onmyway():
    y = barrido_ruido(0.3, True) * 0.5
    y = place(y, campana(784, 0.5), 0.12, 0.7)
    y = place(y, campana(1175, 0.45, 0.6), 0.2, 0.6)
    return y


def s_allin():
    y = metal(196, 0.55) * 0.9
    y = place(y, metal(294, 0.5), 0.0, 0.5)
    y = place(y, timbal(65, 0.6), 0.0, 0.9)
    y = place(y, hp(RNG.standard_normal(int(0.5 * SR)), 3000) * env(tt(0.5), 0.002, 0.15), 0.0, 0.15)
    return y


def s_assist():
    y = np.zeros(int(0.1 * SR))
    for k in range(3):
        y = place(y, tono(1000, 0.1, 0.003, 0.05, (1.0, 0.6)), 0.12 * k, 0.8)
    y = place(y, campana(500, 0.4), 0.0, 0.4)
    return y


def s_needvision():
    y = campana(1320, 0.8, 0.4) * 0.8
    y = place(y, campana(1760, 0.7, 0.3), 0.18, 0.5)
    return sala(y, 1.0, 0.35)


def s_missing():
    y = deslizar(500, 900, 0.4, 0.02) * 0.8
    y = place(y, campana(1050, 0.4, 0.5), 0.38, 0.5)
    return y


def s_enemyvision():
    y = campana(330, 0.9, 0.6) * 0.8
    y = place(y, tono(165, 0.4, 0.005, 0.2), 0.0, 0.5)
    y = place(y, campana(330, 0.7, 0.4), 0.28, 0.4)
    return sala(y, 1.0, 0.3)


def s_wheel_open():
    return barrido_ruido(0.16, True) * 0.5 + place(np.zeros(int(0.16 * SR)), tono(600, 0.1, 0.002, 0.04), 0.0, 0.25)


def s_wheel_tick():
    return tono(1800, 0.05, 0.001, 0.012) * 0.5


def s_denied():
    return tono(120, 0.18, 0.003, 0.07, (1.0, 0.5)) * 0.8


SONIDOS = {
    "generic": s_generic, "caution": s_caution, "retreat": s_retreat, "push": s_push, "onmyway": s_onmyway,
    "allin": s_allin, "assist": s_assist, "needvision": s_needvision, "missing": s_missing, "enemyvision": s_enemyvision,
    "wheel_open": s_wheel_open, "wheel_tick": s_wheel_tick, "denied": s_denied,
}


def acabado(y):
    idx = np.where(np.abs(y) > 0.003)[0]
    y = y[idx[0]:idx[-1] + 1] if len(idx) else y
    y = y * (PICO / (np.max(np.abs(y)) + 1e-9))
    f = min(int(0.03 * SR), len(y) // 4)
    y[-f:] *= np.linspace(1, 0, f)
    return y


def escribir(ruta, y):
    pcm = (np.clip(y, -1, 1) * 32767).astype(np.int16)
    with wave.open(str(ruta), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def main():
    salida = Path(sys.argv[1])
    salida.mkdir(parents=True, exist_ok=True)
    for nombre, fabrica in SONIDOS.items():
        y = acabado(fabrica())
        escribir(salida / f"{nombre}.wav", y)
        print(f"{nombre}.wav  {len(y) / SR:.2f} s")


if __name__ == "__main__":
    main()
