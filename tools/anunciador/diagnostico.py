"""Espectrogramas y envolvente de varios WAV para revisar a ojo el resultado (no sustituye al oido). Solo numpy/scipy/PIL.

Uso: python -I diagnostico.py <carpeta_wav> <salida.png> <nombre> [<nombre> ...]
"""
import sys
import wave
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy import signal

ANCHO = 900
ALTO_ESPECTRO = 150
ALTO_ONDA = 50
FMAX = 9000


def leer(ruta):
    with wave.open(str(ruta)) as w:
        return np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16) / 32768.0, w.getframerate()


def espectro(x, sr):
    f, _, s = signal.spectrogram(x, sr, nperseg=2048, noverlap=1536)
    s = 10 * np.log10(s[f <= FMAX] + 1e-12)
    s = np.clip((s + 110) / 80, 0, 1)[::-1]
    img = Image.fromarray((s * 255).astype(np.uint8)).resize((ANCHO, ALTO_ESPECTRO), Image.BILINEAR)
    return Image.merge("RGB", (img.point(lambda v: min(255, v * 2)), img.point(lambda v: max(0, v - 60) * 2), img.point(lambda v: 255 - v)))


def onda(x):
    lienzo = Image.new("RGB", (ANCHO, ALTO_ONDA), (10, 10, 20))
    pintor = ImageDraw.Draw(lienzo)
    paso = max(1, len(x) // ANCHO)
    for i in range(ANCHO):
        trozo = x[i * paso:(i + 1) * paso]
        if len(trozo):
            a = float(np.max(np.abs(trozo)))
            pintor.line([(i, ALTO_ONDA / 2 - a * ALTO_ONDA / 2), (i, ALTO_ONDA / 2 + a * ALTO_ONDA / 2)], fill=(120, 200, 255))
    return lienzo


def main():
    carpeta, salida = Path(sys.argv[1]), Path(sys.argv[2])
    nombres = sys.argv[3:]
    alto_fila = ALTO_ESPECTRO + ALTO_ONDA + 16
    lienzo = Image.new("RGB", (ANCHO, alto_fila * len(nombres)), (0, 0, 0))
    pintor = ImageDraw.Draw(lienzo)
    for k, nombre in enumerate(nombres):
        x, sr = leer(carpeta / f"{nombre}.wav")
        y0 = k * alto_fila
        pintor.text((4, y0 + 2), f"{nombre}  {len(x) / sr:.2f}s  pico {np.max(np.abs(x)):.2f}  rms {np.sqrt(np.mean(x ** 2)):.3f}", fill=(255, 255, 255))
        lienzo.paste(onda(x), (0, y0 + 14))
        lienzo.paste(espectro(x, sr), (0, y0 + 14 + ALTO_ONDA))
    lienzo.save(salida)


if __name__ == "__main__":
    main()
