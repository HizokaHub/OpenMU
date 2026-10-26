"""Genera con Piper las PALABRAS sueltas de las frases en ingles (kills y rachas) para el estilo de impacto de producir.py.

Cada frase se parte en dos: la primera palabra se alarga (length_scale alto) y la segunda cae como golpe. Las de una sola palabra
(Dominating, Unstoppable, Legendary) van enteras y muy alargadas. Ortografia fonetica española para que la voz mexicana las diga bien.

Uso: python -I piper_partes.py <piper.exe> <modelo.onnx> <carpeta_salida> [clave ...]
Salida: <clave>_1.wav (y <clave>_2.wav cuando hay segunda palabra).
"""
import subprocess
import sys
from pathlib import Path

# clave: [(texto, length_scale), ...]
PARTES = {
    "first_blood": [("Ferst", 1.5), ("Blad", 1.15)],
    "double": [("Dábol", 1.3), ("Kil", 1.1)],
    "triple": [("Trípol", 1.35), ("Kil", 1.1)],
    "quadra": [("Cuádra", 1.45), ("Kil", 1.15)],
    "penta": [("Pénta", 1.8), ("Kil", 1.2)],
    "spree": [("Kílin", 1.3), ("Spri", 1.2)],
    "shutdown": [("Chat", 1.3), ("Dáun", 1.2)],
    "dominating": [("Dóminéitin", 1.55)],
    "unstoppable": [("Anstópabol", 1.5)],
    "legendary": [("Léyenderi", 1.6)],
}


def sintetizar(piper: Path, modelo: Path, texto: str, salida: Path, ritmo: float) -> None:
    orden = [str(piper), "--model", str(modelo), "--output_file", str(salida), "--length_scale", str(ritmo),
             "--noise_scale", "0.75", "--noise_w", "0.9", "--sentence_silence", "0.05"]
    resultado = subprocess.run(orden, input=texto.encode("utf-8"), capture_output=True, cwd=piper.parent, check=False)
    if resultado.returncode != 0:
        raise RuntimeError(resultado.stderr.decode("utf-8", "replace")[-400:])


def main() -> None:
    piper, modelo, salida = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
    solo = set(sys.argv[4:])
    salida.mkdir(parents=True, exist_ok=True)
    for clave, partes in PARTES.items():
        if solo and clave not in solo:
            continue
        for numero, (texto, ritmo) in enumerate(partes, start=1):
            sintetizar(piper, modelo, texto, salida / f"{clave}_{numero}.wav", ritmo)
        print(clave, len(partes), "parte(s)")


if __name__ == "__main__":
    main()
