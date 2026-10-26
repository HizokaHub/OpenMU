"""Genera las frases del anunciador con Piper (voz neuronal local) en WAV crudos para producir.py.

Uso: python -I piper_voces.py <piper.exe> <modelo.onnx> <carpeta_salida> [--fonetico] [--ritmo 1.2] [clave ...]
length_scale > 1 habla mas despacio (1.0 es el ritmo natural del modelo).
--fonetico escribe las frases en ingles con ortografia española para que la voz española las diga mejor.
"""
import subprocess
import sys
from pathlib import Path

# Guion: kills y rachas en ingles (estilo esports), objetivos y partida en español epico.
FRASES = {
    "first_blood": "First Blood",
    "double": "Double Kill",
    "triple": "Triple Kill",
    "quadra": "Quadra Kill",
    "penta": "Penta Kill",
    "ace": "¡Equipo eliminado!",
    "ace_lost": "Todos han caído",
    "shutdown": "Shutdown",
    "spree": "Killing Spree",
    "dominating": "Dominating",
    "unstoppable": "Unstoppable",
    "legendary": "Legendary",
    "turret_ally": "Una torre ha caído",
    "turret_enemy": "Su torre ha caído",
    "nexus_attack": "¡Defiendan el nexo!",
    "match_start": "Cae la noche. Empieza la guerra.",
    "victory": "Gloria eterna. Victoria.",
    "defeat": "Tu reino ha caído. Derrota.",
    "golden_flyover": "El cielo arde en oro",
    "golden_invaders": "La invasión dorada ha comenzado",
    "dragon_ally": "¡El gran dragón ha caído!",
    "dragon_enemy": "El enemigo ha matado al dragón",
}

# Version con ortografia española para las palabras en ingles que la voz española lee peor.
FONETICAS = {
    "first_blood": "Ferst Blad",
    "double": "Dábol Kil",
    "triple": "Trípol Kil",
    "quadra": "Cuádra Kil",
    "penta": "Pénta Kil",
    "shutdown": "Chatdáun",
    "spree": "Kílin Spri",
    "dominating": "Dóminéitin",
    "unstoppable": "Anstópabol",
    "legendary": "Léyenderi",
}


def sintetizar(piper: Path, modelo: Path, texto: str, salida: Path, ritmo: float) -> None:
    orden = [str(piper), "--model", str(modelo), "--output_file", str(salida), "--length_scale", str(ritmo),
             "--noise_scale", "0.75", "--noise_w", "0.9", "--sentence_silence", "0.1"]
    resultado = subprocess.run(orden, input=texto.encode("utf-8"), capture_output=True, cwd=piper.parent, check=False)
    if resultado.returncode != 0:
        raise RuntimeError(resultado.stderr.decode("utf-8", "replace")[-400:])


def main() -> None:
    args = sys.argv[1:]
    fonetico = "--fonetico" in args
    ritmo = 1.2
    if "--ritmo" in args:
        i = args.index("--ritmo")
        ritmo = float(args[i + 1])
        del args[i:i + 2]
    args = [a for a in args if a != "--fonetico"]
    piper, modelo, salida = Path(args[0]), Path(args[1]), Path(args[2])
    solo = set(args[3:])
    salida.mkdir(parents=True, exist_ok=True)
    for clave, texto in FRASES.items():
        if solo and clave not in solo:
            continue
        if fonetico:
            texto = FONETICAS.get(clave, texto)
        sintetizar(piper, modelo, texto, salida / f"{clave}.wav", ritmo)
        print(clave, "->", texto.encode("ascii", "replace").decode())


if __name__ == "__main__":
    main()
