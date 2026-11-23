r"""Pone el icono de Rush (GIF de 30x40 de la guia muonlinefanz.com) en la celda 338 (= 638 - 300) de las hojas de iconos del arbol maestro.
Las hojas son OZJ (24 bytes de cabecera + JPEG de 512x512, celdas de 20x28, 25 por fila): Data\Interface\new_Master_Icon.OZJ (activa) y
new_Master_non_Icon.OZJ (inactiva: gris al 63 %). El mismo icono se ve en el arbol, la barra y el selector (los skills >= 300 leen de esa hoja).
Uso:  python -I -X utf8 tools/rush_icono.py <rush.gif> <carpeta Data\Interface> [vista.png]
"""
import io
import sys

import numpy as np
from PIL import Image

CELDA = 338
ANCHO, ALTO, COLUMNAS = 20, 28, 25
CABECERA = 24
GRIS = 0.63


def cargar(ruta):
    d = open(ruta, "rb").read()
    im = Image.open(io.BytesIO(d[CABECERA:]))
    im.load()
    return d[:CABECERA], im.convert("RGB")


def guardar(ruta, cabecera, im):
    b = io.BytesIO()
    im.save(b, "JPEG", quality=97, subsampling=0)
    open(ruta, "wb").write(cabecera + b.getvalue())


def main():
    gif, carpeta = sys.argv[1], sys.argv[2]
    icono = Image.open(gif).convert("RGB").resize((ANCHO, ALTO), Image.LANCZOS)
    x, y = (CELDA % COLUMNAS) * ANCHO, (CELDA // COLUMNAS) * ALTO
    gris = Image.fromarray(np.clip(np.array(icono).astype(float).mean(axis=2, keepdims=True).repeat(3, axis=2) * GRIS, 0, 255).astype("uint8"))
    vista = Image.new("RGB", (ANCHO * 2 + 4, ALTO), (30, 30, 30))
    for nombre, pieza in (("new_Master_Icon.OZJ", icono), ("new_Master_non_Icon.OZJ", gris)):
        ruta = f"{carpeta}/{nombre}"
        cab, hoja = cargar(ruta)
        hoja.paste(pieza, (x, y))
        guardar(ruta, cab, hoja)
        comprobada = cargar(ruta)[1].crop((x, y, x + ANCHO, y + ALTO))
        print(nombre, "celda", CELDA, "pegada; error medio tras el JPEG:", float(np.abs(np.array(comprobada).astype(float) - np.array(pieza)).mean()))
        vista.paste(comprobada, (0 if "non" not in nombre else ANCHO + 4, 0))
    if len(sys.argv) > 3:
        vista.resize((vista.width * 8, vista.height * 8), Image.LANCZOS).save(sys.argv[3])


if __name__ == "__main__":
    main()
