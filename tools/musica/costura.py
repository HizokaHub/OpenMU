"""Revisa la costura del bucle de cada WAV: salto entre la ultima y la primera muestra frente al salto tipico entre muestras vecinas."""
import sys
import wave
from pathlib import Path

import numpy as np


def main():
    for ruta in sorted(Path(sys.argv[1]).glob("*.wav")):
        with wave.open(str(ruta)) as w:
            x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).reshape(-1, w.getnchannels()).astype(float) / 32768.0
        salto = np.abs(x[0] - x[-1]).max()
        tipico = np.abs(np.diff(x, axis=0)).mean()
        n = int(0.05 * 44100)
        rms_fin, rms_ini = np.sqrt(np.mean(x[-n:] ** 2)), np.sqrt(np.mean(x[:n] ** 2))
        print(f"{ruta.name}: salto {salto:.4f} (tipico {tipico:.4f}); rms ultimos 50 ms {rms_fin:.3f} / primeros 50 ms {rms_ini:.3f}")


if __name__ == "__main__":
    main()
