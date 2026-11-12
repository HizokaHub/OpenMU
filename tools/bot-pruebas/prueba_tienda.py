"""Prueba automatica de la tienda del MOBA con el piloto.

Recorre: entrar al juego -> /moba -> herrero -> las 7 pestañas -> comprar -> vender -> comprar -> deshacer.
Deja capturas numeradas en capturas/tienda_* y comprueba el log del servidor (ninguna pagina rechazada, ninguna venta fallida).
Uso (Git Bash: export MSYS_NO_PATHCONV=1):  python -I prueba_tienda.py [--sin-entrar]
Durante la prueba no toques el raton ni el teclado.
"""
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))  # con -I Python no ve la carpeta del script
import piloto  # noqa: E402

CLIC_VALHALLA = (503, 301)
CLIC_VALHALLA_1 = (680, 301)
CLIC_OK_ACCESO = (693, 563)
CLIC_PERSONAJE = (515, 520)
CLIC_CONECTAR = (1262, 697)
CLIC_HERRERO = (625, 262)
PESTANAS = {"para_ti": (84, 78), "consumibles": (214, 78), "armas": (349, 78), "sets_t1": (482, 78),
            "sets_t2": (615, 78), "sets_t3": (745, 78), "mascotas": (878, 78)}
CELDA_ITEM_CARO = (330, 435)      # arma de tier 3, primera de su seccion
CELDA_ITEM_CARO_2 = (560, 435)    # la segunda arma de tier 3
BOTON_PAGINA_SIGUIENTE = (917, 491)
BOTON_COMPRAR = (830, 541)
BOTON_VENDER = (830, 585)
BOTON_DESHACER = (830, 626)
REPOSO_RATON = (1150, 640)  # sobre el inventario vacio: no abre tooltips
PAUSA_UI_S = 2.0
PAUSA_CARGA_S = 14.0


def esperar(segundos):
    time.sleep(segundos)


def entrar_al_juego():
    piloto.lanzar()
    esperar(4)
    piloto.clic(*CLIC_VALHALLA)
    esperar(6)
    piloto.clic(*CLIC_VALHALLA_1)
    esperar(4)
    piloto.login()
    esperar(1)
    piloto.clic(*CLIC_OK_ACCESO)
    esperar(8)
    piloto.clic(*CLIC_PERSONAJE)
    esperar(1)
    piloto.clic(*CLIC_CONECTAR)
    esperar(PAUSA_CARGA_S)
    piloto.chat("/moba")
    esperar(20)


def foto(nombre):
    piloto.mover(*REPOSO_RATON)
    esperar(1)
    piloto.foto("tienda_" + nombre)


def lineas_log_desde(marca):
    texto = piloto.LOG_SERVIDOR.read_text(encoding="utf-8", errors="replace").splitlines()
    return texto[marca:]


def comprobar_log(marca):
    nuevas = lineas_log_desde(marca)
    problemas = [l for l in nuevas if "refused" in l or "but item wasn't found" in l or "no npc merchant store" in l]
    for linea in problemas:
        print("PROBLEMA EN EL LOG:", linea[:200])
    print("log: sin problemas" if not problemas else f"log: {len(problemas)} problema(s)")
    return not problemas


def probar_tienda():
    marca = len(piloto.LOG_SERVIDOR.read_text(encoding="utf-8", errors="replace").splitlines())
    piloto.chat("/mobaoro 50000")
    esperar(3)
    piloto.clic(*CLIC_HERRERO)
    esperar(3)
    for nombre, posicion in PESTANAS.items():
        piloto.clic(*posicion)
        esperar(PAUSA_UI_S)
        foto(nombre)
    piloto.clic(*PESTANAS["armas"])
    esperar(PAUSA_UI_S)
    piloto.clic(*CELDA_ITEM_CARO)
    esperar(PAUSA_UI_S)
    piloto.clic(*BOTON_COMPRAR)
    esperar(3)
    foto("comprado")
    piloto.clic(*BOTON_VENDER)
    esperar(3)
    foto("vendido")
    piloto.clic(*CELDA_ITEM_CARO_2, derecho=True)   # clic derecho: compra directa, sin el boton COMPRAR
    esperar(3)
    foto("clic_derecho")
    piloto.clic(*BOTON_DESHACER)
    esperar(3)
    foto("deshecho")
    piloto.clic(*PESTANAS["consumibles"])
    esperar(PAUSA_UI_S)
    piloto.clic(*BOTON_PAGINA_SIGUIENTE)
    esperar(PAUSA_UI_S)
    foto("consumibles_pagina2")
    return comprobar_log(marca)


if __name__ == "__main__":
    if "--sin-entrar" not in sys.argv:
        entrar_al_juego()
    sys.exit(0 if probar_tienda() else 1)
