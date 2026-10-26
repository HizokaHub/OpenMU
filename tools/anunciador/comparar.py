"""Compara dos carpetas de WAV (mismo nombre, mismos muestras)."""
import sys
import wave
from pathlib import Path

import numpy as np


def leer(ruta):
    with wave.open(str(ruta)) as w:
        return np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16)


def main():
    originales, copias = Path(sys.argv[1]), Path(sys.argv[2])
    distintos = 0
    for ruta in sorted(originales.glob("*.wav")):
        otra = copias / ruta.name
        x, y = leer(ruta), leer(otra)
        if len(x) != len(y) or not np.array_equal(x, y):
            distintos += 1
            print("DIFIERE", ruta.name, len(x), len(y))
    print("comparados; distintos:", distintos)


if __name__ == "__main__":
    main()
