"""Analiza un WAV: duracion, tempo aproximado, tonalidad probable (croma) y reparto de energia por bandas. Solo numpy/scipy.

Uso: python -I analizar.py <archivo.wav> [<imagen.png>]
"""
import sys
import wave

import numpy as np
from PIL import Image
from scipy import signal

NOTAS = ["Do", "Do#", "Re", "Re#", "Mi", "Fa", "Fa#", "Sol", "Sol#", "La", "La#", "Si"]
MAYOR = np.array([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88])
MENOR = np.array([6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17])


def leer(ruta):
    with wave.open(ruta) as w:
        canales, tasa = w.getnchannels(), w.getframerate()
        datos = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64) / 32768.0
    return (datos.reshape(-1, canales).mean(axis=1) if canales > 1 else datos), tasa


def tempo(x, sr):
    f, t, s = signal.spectrogram(x, sr, nperseg=1024, noverlap=512)
    flujo = np.maximum(np.diff(np.log1p(s * 1e4), axis=1), 0).sum(axis=0)
    flujo -= flujo.mean()
    ac = np.correlate(flujo, flujo, mode="full")[len(flujo) - 1:]
    paso = t[1] - t[0]
    mejor = None
    for bpm in range(50, 181):
        lag = int(round(60.0 / bpm / paso))
        if lag < len(ac) and (mejor is None or ac[lag] > mejor[1]):
            mejor = (bpm, ac[lag])
    return mejor[0]


def tonalidad(x, sr):
    n = 1 << 15
    croma = np.zeros(12)
    for i in range(0, len(x) - n, n // 2):
        espectro = np.abs(np.fft.rfft(x[i:i + n] * np.hanning(n)))
        freqs = np.fft.rfftfreq(n, 1 / sr)
        for k in range(1, len(freqs)):
            if 60 < freqs[k] < 2000:
                nota = int(round(12 * np.log2(freqs[k] / 261.6256))) % 12
                croma[nota] += espectro[k] ** 2
    puntuaciones = []
    for base in range(12):
        puntuaciones.append((np.corrcoef(croma, np.roll(MAYOR, base))[0, 1], f"{NOTAS[base]} mayor"))
        puntuaciones.append((np.corrcoef(croma, np.roll(MENOR, base))[0, 1], f"{NOTAS[base]} menor"))
    puntuaciones.sort(reverse=True)
    return puntuaciones[:3], croma / croma.max()


def bandas(x, sr):
    espectro = np.abs(np.fft.rfft(x)) ** 2
    freqs = np.fft.rfftfreq(len(x), 1 / sr)
    total = espectro.sum()
    rangos = [("sub <120", 0, 120), ("graves 120-400", 120, 400), ("medios 400-2k", 400, 2000), ("agudos 2k-6k", 2000, 6000), ("aire >6k", 6000, 1e9)]
    return {n: round(100 * espectro[(freqs >= a) & (freqs < b)].sum() / total, 1) for n, a, b in rangos}


def main():
    x, sr = leer(sys.argv[1])
    print(f"{sys.argv[1]}: {len(x) / sr:.1f} s, {sr} Hz")
    print("tempo aprox.:", tempo(x, sr), "bpm")
    mejores, croma = tonalidad(x, sr)
    print("tonalidad probable:", [(n, round(float(c), 2)) for c, n in mejores])
    print("croma:", {NOTAS[i]: round(float(croma[i]), 2) for i in range(12)})
    print("energia por bandas (%):", bandas(x, sr))
    ventana = 5 * sr
    print("nivel RMS cada 5 s:", [round(float(np.sqrt(np.mean(x[i:i + ventana] ** 2))), 3) for i in range(0, len(x) - ventana + 1, ventana)])
    if len(sys.argv) > 2:
        f, t, s = signal.spectrogram(x, sr, nperseg=2048, noverlap=1536)
        s = 10 * np.log10(s[f <= 8000] + 1e-12)
        img = Image.fromarray((np.clip((s + 110) / 80, 0, 1)[::-1] * 255).astype(np.uint8)).resize((1000, 300))
        img.save(sys.argv[2])


if __name__ == "__main__":
    main()
