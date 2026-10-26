"""Convierte los caracteres no ASCII de los literales de texto de un fuente C++ en escapes \\uXXXX (el compilador lee los fuentes como ANSI).

Uso: python -I escapar_unicode.py <archivo> [<archivo> ...]
Solo toca las lineas que contienen una comilla; los comentarios con acentos quedan como estaban si la linea no tiene comillas.
"""
import sys
from pathlib import Path


def escapar(linea):
    if '"' not in linea:
        return linea, 0
    salida, n = [], 0
    for ch in linea:
        if ord(ch) > 127:
            salida.append("\\u%04x" % ord(ch))
            n += 1
        else:
            salida.append(ch)
    return "".join(salida), n


def main():
    for ruta in sys.argv[1:]:
        p = Path(ruta)
        lineas = p.read_text(encoding="utf-8").split("\n")
        total = 0
        for i, linea in enumerate(lineas):
            nueva, n = escapar(linea)
            lineas[i] = nueva
            total += n
        p.write_text("\n".join(lineas), encoding="utf-8")
        print(p.name, total, "caracteres escapados")


if __name__ == "__main__":
    main()
