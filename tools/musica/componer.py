"""Musica ambiental del MOBA (sintesis propia): cuatro pistas en bucle con un tema recurrente (leitmotiv de Eclipse Arena).

Pistas: prep (cuenta atras), early (inicio), mid (batalla), golden (invasion dorada). Re menor / Re mayor, estilo orquestal oscuro:
cuerdas, metales, coro, arpa pulsada (Karplus-Strong), campanas, bajo, timbales y percusion.

Uso: python -I componer.py <carpeta_salida> [pista ...]
Salida: <pista>.wav estereo 44,1 kHz 16 bits (luego se codifica a OGG con ffmpeg).
"""
import sys
import wave
from pathlib import Path

import numpy as np
from scipy import signal

SR = 44100
RNG = np.random.default_rng(777)
RMS_OBJETIVO_DB = -21.0
PICO = 0.89


# ------------------------------------------------------------------ utilidades

def mtof(m):
    return 440.0 * 2.0 ** ((np.asarray(m, dtype=float) - 69.0) / 12.0)


def tt(seg):
    return np.arange(int(SR * seg)) / SR


def pad(x, n):
    return x[:n] if len(x) >= n else np.concatenate([x, np.zeros(n - len(x))])


def place(base, x, t, gain=1.0):
    i = int(round(t * SR))
    if i < 0:
        return base
    fin = i + len(x)
    if fin > len(base):
        base = np.concatenate([base, np.zeros(fin - len(base))])
    base[i:fin] += x * gain
    return base


def filtro(x, tipo, f, orden=2):
    fn = f / (SR / 2) if np.isscalar(f) else [v / (SR / 2) for v in f]
    return signal.sosfilt(signal.butter(orden, fn, btype=tipo, output="sos"), x)


def lp(x, f, orden=2):
    return filtro(x, "low", min(f, SR / 2 - 200), orden)


def hp(x, f, orden=2):
    return filtro(x, "high", f, orden)


def bp(x, lo, hi, orden=2):
    return filtro(x, "band", [lo, min(hi, SR / 2 - 200)], orden)


def env(t, ataque, caida):
    return np.minimum(t / max(ataque, 1e-4), 1.0) * np.exp(-t / caida)


def suave(x, drive=1.4):
    return np.tanh(x * drive) / np.tanh(drive)


# ------------------------------------------------------------------ instrumentos

def sierra(freq, seg, vibrato=0.0):
    t = tt(seg)
    mod = 1 + vibrato * np.sin(2 * np.pi * 5.2 * t + RNG.uniform(0, 6.28))
    fase = np.cumsum(freq * mod) / SR
    return 2 * (fase - np.floor(fase + 0.5))


def cuerdas(midis, dur, ataque=0.7, relajo=0.9, brillo=0.5):
    """Seccion de cuerdas: sierras desafinadas (conjunto) con vibrato leve y filtro paso bajo."""
    t = tt(dur + relajo)
    y = np.zeros_like(t)
    for m in midis:
        for d in (0.994, 1.0, 1.006):
            y += sierra(mtof(m) * d, dur + relajo, 0.003)
    y = lp(y, 700 + 3200 * brillo)
    a = np.minimum(t / ataque, 1.0)
    r = np.where(t > dur, np.exp(-(t - dur) / (relajo / 3.0)), 1.0)
    return y * a * r / np.sqrt(len(midis) * 3)


def corno(midi, dur, ataque=0.12, brillo=0.7, relajo=0.5):
    """Metal (corno/trombon): tres sierras con barrido de brillo durante el ataque."""
    t = tt(dur + relajo)
    v = sum(sierra(mtof(midi) * d, dur + relajo, 0.004) for d in (0.996, 1.0, 1.004)) / 3
    oscuro, claro = lp(v, 600), lp(v, 4800)
    barrido = np.clip(t / (ataque * 2.5), 0, 1) ** 0.7 * brillo
    y = oscuro * (1 - barrido) + claro * barrido
    a = np.minimum(t / ataque, 1.0)
    r = np.where(t > dur, np.exp(-(t - dur) / (relajo / 3.0)), 1.0)
    return y * a * r


def coro(midis, dur, ataque=1.0, relajo=1.2, vocal="a"):
    formantes = {"a": [(730, 1.0), (1090, .55), (2440, .25)], "o": [(570, 1.0), (840, .5), (2410, .2)]}[vocal]
    t = tt(dur + relajo)
    y = np.zeros_like(t)
    for m in midis:
        v = sum(sierra(mtof(m) * d, dur + relajo, 0.006) for d in (0.99, 1.0, 1.012)) / 3
        y += sum(g * bp(v, fc * 0.85, fc * 1.18) for fc, g in formantes)
    a = np.minimum(t / ataque, 1.0)
    r = np.where(t > dur, np.exp(-(t - dur) / (relajo / 3.0)), 1.0)
    return y * a * r / np.sqrt(len(midis))


def bajo(midi, dur):
    t = tt(dur + 0.4)
    f = mtof(midi)
    y = np.sin(2 * np.pi * f * t) + 0.35 * lp(sierra(f, dur + 0.4), 260)
    a = np.minimum(t / 0.04, 1.0)
    r = np.where(t > dur, np.exp(-(t - dur) / 0.12), 1.0)
    return y * a * r


def pulsada(midi, seg=2.6, caida=0.9967, brillo=0.6):
    """Cuerda pulsada (Karplus-Strong): arpa / laud."""
    n = max(int(SR / mtof(midi)), 2)
    x = np.zeros(int(seg * SR))
    rafaga = RNG.uniform(-1, 1, n)
    x[:n] = lp(rafaga, 1500 + 6000 * brillo, 1)
    a = np.zeros(n + 2)
    a[0] = 1.0
    a[n] = -caida / 2
    a[n + 1] = -caida / 2
    return signal.lfilter([1.0], a, x) * 0.9


def campana(midi, seg=4.0):
    t = tt(seg)
    f = mtof(midi)
    y = np.zeros_like(t)
    for razon, g, c in [(1, 1.0, 2.4), (2.76, .55, 1.6), (5.4, .35, 1.1), (8.93, .2, 0.7)]:
        y += g * np.sin(2 * np.pi * f * razon * t + RNG.uniform(0, 6.28)) * env(t, 0.002, c)
    return y * 0.5


def taiko(f=62.0, seg=1.0):
    t = tt(seg)
    f_t = f * (1 + 0.7 * np.exp(-t * 28))
    cuerpo = np.sin(2 * np.pi * np.cumsum(f_t) / SR) * env(t, 0.001, 0.32)
    golpe = lp(RNG.standard_normal(len(t)), 900) * env(t, 0.0005, 0.04)
    return cuerpo + 0.8 * golpe


def caja_marcial(seg=0.3):
    t = tt(seg)
    return (bp(RNG.standard_normal(len(t)), 900, 5200) * env(t, 0.0005, 0.07)
            + 0.5 * np.sin(2 * np.pi * 190 * t) * env(t, 0.0005, 0.05))


def agitador(seg=0.12):
    t = tt(seg)
    return hp(RNG.standard_normal(len(t)), 6500) * env(t, 0.002, 0.03)


def platillo(seg=2.2, caida=0.7):
    t = tt(seg)
    r = hp(RNG.standard_normal(len(t)), 3000) * env(t, 0.002, caida)
    return r + 0.35 * bp(RNG.standard_normal(len(t)), 5500, 10000) * env(t, 0.004, caida * 1.5)


def brillo_dorado(seg=3.0):
    t = tt(seg)
    y = np.zeros_like(t)
    for f in (1318, 1568, 1975, 2637, 3136, 3951):
        y += np.sin(2 * np.pi * f * t + RNG.uniform(0, 6.28)) * (0.5 + 0.5 * np.sin(2 * np.pi * RNG.uniform(2, 6) * t)) * env(t, 0.4, seg * 0.45) * 0.12
    return y


# ------------------------------------------------------------------ armonia

# nombre: (nota del bajo, voces de las cuerdas)
ACORDES = {
    "Dm": (38, [50, 57, 62, 65]), "Bb": (46, [53, 58, 62, 65]), "F": (41, [53, 57, 60, 65]), "C": (36, [52, 55, 60, 64]),
    "Gm": (43, [50, 55, 58, 62]), "A": (45, [52, 57, 61, 64]),
    "D": (38, [50, 57, 62, 66]), "Bm": (47, [50, 54, 59, 62]), "G": (43, [50, 55, 59, 62]), "Em": (40, [52, 55, 59, 64]),
}

# Tema de Eclipse Arena (16 tiempos, 4 compases): (nota MIDI, duracion en tiempos). En Re menor y en Re mayor.
TEMA_MENOR = [(62, 1), (65, 1), (69, 1), (74, 2.5), (72, .5), (70, 1), (69, 2), (67, 1), (65, 1), (62, 3)]
TEMA_MAYOR = [(62, 1), (66, 1), (69, 1), (74, 2.5), (73, .5), (71, 1), (69, 2), (67, 1), (66, 1), (62, 3)]


class Pista:
    """Una pista con un compas fijo; coloca instrumentos por compas y tiempo."""

    def __init__(self, bpm, compases, cola=4.0):
        self.tiempo = 60.0 / bpm
        self.compas = 4 * self.tiempo
        self.compases = compases
        self.largo = compases * self.compas
        self.cola = cola
        self.y = np.zeros(int((self.largo + cola) * SR))

    def en(self, compas, tiempo=0.0):
        return compas * self.compas + tiempo * self.tiempo

    def poner(self, x, compas, tiempo=0.0, ganancia=1.0):
        self.y = place(self.y, x, self.en(compas, tiempo), ganancia)

    def tema(self, compas, notas, instrumento, ganancia, transposicion=0, **kw):
        t = 0.0
        for midi, dur in notas:
            self.poner(instrumento(midi + transposicion, dur * self.tiempo * 0.97, **kw), compas, t, ganancia)
            t += dur

    def cerrar(self):
        """Pliega la cola sobre el principio para que el bucle sea continuo."""
        n = int(self.largo * SR)
        cuerpo = self.y[:n].copy()
        resto = self.y[n:n + int(self.cola * SR)]
        cuerpo[:len(resto)] += resto
        return cuerpo


def arpegio(p, compas, nombre, patron, ganancia, octava=12, pulsos=8):
    _, voces = ACORDES[nombre]
    paso = 4.0 / pulsos
    for k in range(pulsos):
        nota = voces[patron[k % len(patron)]] + octava
        p.poner(pulsada(nota, 2.2), compas, k * paso, ganancia * (0.8 + 0.2 * (k % 2 == 0)))


# ------------------------------------------------------------------ pistas

def pista_prep():
    """Cuenta atras: tension oscura, latido de timbal, pulsada escasa y coro que sube. 60 BPM, 16 compases."""
    p = Pista(60, 16)
    prog = ["Dm", "Dm", "Bb", "Bb", "Gm", "Gm", "A", "A", "Dm", "Dm", "Bb", "Bb", "Gm", "Gm", "A", "A"]
    for c, nombre in enumerate(prog):
        bajo_n, voces = ACORDES[nombre]
        p.poner(bajo(bajo_n, p.compas * 0.98), c, 0, 0.55)
        if c % 2 == 0:
            p.poner(cuerdas(voces, p.compas * 2, 1.6, 1.4, 0.35), c, 0, 0.30)
        # latido: dos golpes graves cada compas
        p.poner(taiko(55, 0.9), c, 0, 0.30 + 0.015 * c)
        p.poner(taiko(55, 0.9), c, 0.75, 0.20 + 0.01 * c)
    for c in (1, 5, 9, 13):
        arpegio(p, c, prog[c], [0, 2, 1, 3], 0.12, 12, 4)
    p.poner(campana(74, 5.0), 8, 0, 0.10)
    p.poner(campana(69, 5.0), 12, 0, 0.10)
    p.poner(coro([50, 57, 62], p.compas * 4, 2.2, 1.4, "o"), 12, 0, 0.30)
    p.poner(brillo_dorado(3.5), 15, 2, 0.5)
    return p


def pista_early(bpm=72):
    """Inicio de partida: ambiente tenso y sereno, cuerdas largas, arpa escasa, el tema en el corno al fondo. 32 compases."""
    p = Pista(bpm, 32)
    base = ["Dm", "Bb", "F", "C", "Dm", "Gm", "Bb", "A"]
    prog = base * 4
    for c, nombre in enumerate(prog):
        bajo_n, voces = ACORDES[nombre]
        p.poner(bajo(bajo_n, p.compas * 0.98), c, 0, 0.50)
        p.poner(cuerdas(voces, p.compas, 0.9, 1.0, 0.45), c, 0, 0.28)
        p.poner(taiko(58, 0.9), c, 0, 0.22)
        if c % 2 == 1:
            p.poner(taiko(58, 0.9), c, 3, 0.14)
        if c % 4 in (1, 3):
            arpegio(p, c, nombre, [0, 2, 1, 3, 2, 1], 0.10, 12, 8)
    for c in range(32):
        for k in (1, 3):
            p.poner(agitador(), c, k + 0.5, 0.05)
    for c in (8, 24):
        p.tema(c, TEMA_MENOR, corno, 0.17, 0, ataque=0.25, brillo=0.5)
    for c in (16, 28):
        p.poner(coro(ACORDES[prog[c]][1][:3], p.compas * 4, 1.6, 1.4, "a"), c, 0, 0.22)
    p.poner(campana(74, 5.0), 0, 0, 0.08)
    p.poner(campana(69, 5.0), 16, 0, 0.08)
    return p


def pista_mid(bpm=100):
    """Batalla: ostinato de cuerdas, timbales y caja, metales en los cambios, el tema en los metales. 40 compases."""
    p = Pista(bpm, 40)
    base = ["Dm", "Dm", "Bb", "Bb", "F", "F", "C", "A"]
    prog = base * 5
    for c, nombre in enumerate(prog):
        bajo_n, voces = ACORDES[nombre]
        p.poner(bajo(bajo_n, p.compas * 0.98), c, 0, 0.55)
        for k in range(8):                      # ostinato de corcheas con acento
            p.poner(cuerdas([voces[0], voces[1]], p.tiempo * 0.45, 0.02, 0.15, 0.7), c, k * 0.5, 0.22 if k % 2 == 0 else 0.13)
        p.poner(taiko(58, 0.9), c, 0, 0.45)
        p.poner(taiko(58, 0.9), c, 2, 0.38)
        p.poner(taiko(66, 0.7), c, 1.5, 0.22)
        p.poner(taiko(66, 0.7), c, 3.5, 0.22)
        p.poner(caja_marcial(), c, 1, 0.20)
        p.poner(caja_marcial(), c, 3, 0.20)
        if c % 4 == 0:
            p.poner(platillo(2.0, 0.6), c, 0, 0.12)
            p.poner(corno(voces[1], p.tiempo * 1.5, 0.04, 0.9), c, 0, 0.20)
        if c % 8 == 7:
            for k in range(8):
                p.poner(caja_marcial(0.2), c, 2 + k * 0.25, 0.10 + 0.03 * k)
    for c in (16, 32):
        p.tema(c, TEMA_MENOR, corno, 0.26, 0, ataque=0.06, brillo=0.85)
    for c in (8, 24):
        p.poner(coro(ACORDES[prog[c]][1][:3], p.compas * 4, 0.8, 1.0, "a"), c, 0, 0.22)
    return p


def pista_golden(bpm=112):
    """Invasion dorada: Re mayor heroico, coro y metales en el tema, arpa rapida y redobles. 40 compases."""
    p = Pista(bpm, 40)
    base = ["D", "A", "Bm", "G", "D", "A", "Em", "A"]
    prog = base * 5
    for c, nombre in enumerate(prog):
        bajo_n, voces = ACORDES[nombre]
        p.poner(bajo(bajo_n, p.compas * 0.98), c, 0, 0.55)
        p.poner(cuerdas(voces, p.compas, 0.35, 0.8, 0.75), c, 0, 0.30)
        for k in range(4):
            p.poner(taiko(60, 0.8), c, k, 0.34 if k % 2 == 0 else 0.20)
        for k in range(8):
            p.poner(agitador(), c, k * 0.5, 0.06)
        if c % 2 == 0:
            arpegio(p, c, nombre, [0, 1, 2, 3, 2, 1], 0.11, 12, 8)
        if c % 4 == 0:
            p.poner(platillo(2.4, 0.8), c, 0, 0.14)
            p.poner(brillo_dorado(3.0), c, 0, 0.45)
    for c in (8, 24, 36):
        p.tema(c, TEMA_MAYOR, corno, 0.28, 0, ataque=0.05, brillo=0.9)
    for c in (16, 32):
        p.tema(c, TEMA_MAYOR, corno, 0.28, 0, ataque=0.05, brillo=0.9)
        p.poner(coro([62, 66, 69, 74], p.compas * 4, 0.5, 1.2, "a"), c, 0, 0.30)
    for c in (0, 20):
        p.poner(coro([62, 66, 69], p.compas * 4, 0.9, 1.2, "a"), c, 0, 0.26)
    return p


PISTAS = {"prep": pista_prep, "early": pista_early, "mid": pista_mid, "golden": pista_golden}


# ------------------------------------------------------------------ mezcla final

def sala_estereo(x, seg=3.4, humedo=0.28):
    n = int(SR * seg)
    t = np.arange(n) / SR
    salidas = []
    for _ in range(2):
        ir = hp(lp(RNG.standard_normal(n) * np.exp(-t * (6.5 / seg) * 1.5), 5200), 140)
        ir /= np.max(np.abs(ir))
        c = signal.fftconvolve(x, ir)
        mojado = np.zeros(len(x))
        mojado[:] = c[:len(x)]
        mojado *= np.sqrt(np.mean(x ** 2)) / (np.sqrt(np.mean(mojado ** 2)) + 1e-9)
        salidas.append((1 - humedo) * x + humedo * mojado)
    return np.stack(salidas, axis=1)


def igualar_bandas(y, objetivo=None):
    """Reparte la energia entre bandas hacia un balance dado (porcentajes): evita que el bajo y los timbales tapen todo."""
    objetivo = objetivo or {"sub": 22, "graves": 38, "medios": 30, "agudos": 8, "aire": 2}
    partes = {
        "sub": lp(y, 120, 3),
        "graves": bp(y, 120, 400, 2),
        "medios": bp(y, 400, 2000, 2),
        "agudos": bp(y, 2000, 6000, 2),
        "aire": hp(y, 6000, 2),
    }
    energias = {k: float(np.sum(v ** 2)) + 1e-12 for k, v in partes.items()}
    total = sum(energias.values())
    salida = np.zeros_like(y)
    for k, v in partes.items():
        actual = 100.0 * energias[k] / total
        ganancia = float(np.clip(np.sqrt(objetivo[k] / max(actual, 1e-6)), 0.02, 30.0))
        salida += v * ganancia
    return salida


def masterizar(y):
    """Se procesa la pista repetida tres veces y se conserva la copia central: los filtros y la sala llegan al principio ya 'calientes',
    asi que el final y el principio encajan y el bucle no hace clic."""
    n = len(y)
    y3 = np.tile(y - np.mean(y), 3)
    y3 = hp(y3, 30)
    y3 = igualar_bandas(igualar_bandas(y3))
    est = sala_estereo(y3)[n:2 * n]
    est = suave(est / (np.max(np.abs(est)) + 1e-9), 1.3)
    rms = np.sqrt(np.mean(est ** 2))
    est = est * (10 ** (RMS_OBJETIVO_DB / 20) / rms)
    pico = np.max(np.abs(est))
    if pico > PICO:
        est *= PICO / pico
    return est


def escribir(ruta, est):
    pcm = (np.clip(est, -1, 1) * 32767).astype(np.int16)
    with wave.open(str(ruta), "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def main():
    salida = Path(sys.argv[1])
    solo = set(sys.argv[2:])
    salida.mkdir(parents=True, exist_ok=True)
    for nombre, fabrica in PISTAS.items():
        if solo and nombre not in solo:
            continue
        pista = fabrica()
        est = masterizar(pista.cerrar())
        escribir(salida / f"{nombre}.wav", est)
        print(f"{nombre}.wav  {len(est) / SR:.1f} s  rms {np.sqrt(np.mean(est ** 2)):.3f}  pico {np.max(np.abs(est)):.2f}")


if __name__ == "__main__":
    main()
