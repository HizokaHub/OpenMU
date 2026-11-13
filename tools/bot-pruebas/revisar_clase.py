"""Revisa la tienda de una clase de la cuenta de prueba: sets T1-T3, armas y mascotas/escudos/alas.

Uso (Git Bash: export MSYS_NO_PATHCONV=1):  python -I revisar_clase.py <nombre> <x del personaje en la seleccion>
Personajes (x): Dk 330, Dw 515, Elf 700, Dl 880, Mg 1070.  Deja capturas en capturas/clase_<nombre>_*.png
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import piloto  # noqa: E402
import prueba_tienda as t  # noqa: E402

PESTANAS_REVISADAS = ("sets_t1", "sets_t2", "sets_t3", "armas", "mascotas")
Y_PERSONAJE = 520


def main(nombre, x_personaje):
    piloto.lanzar()
    t.esperar(4)
    piloto.clic(*t.CLIC_VALHALLA)
    t.esperar(6)
    piloto.clic(*t.CLIC_VALHALLA_1)
    t.esperar(4)
    piloto.login()
    t.esperar(1)
    piloto.clic(*t.CLIC_OK_ACCESO)
    t.esperar(8)
    piloto.clic(int(x_personaje), Y_PERSONAJE)
    t.esperar(1)
    piloto.clic(*t.CLIC_CONECTAR)
    t.esperar(t.PAUSA_CARGA_S)
    piloto.chat("/moba")
    t.esperar(20)
    piloto.clic(*t.CLIC_HERRERO)
    t.esperar(3)
    for pestana in PESTANAS_REVISADAS:
        piloto.clic(*t.PESTANAS[pestana])
        t.esperar(t.PAUSA_UI_S)
        t.foto(f"clase_{nombre}_{pestana}")
    # Sin esto la cuenta queda como fantasma en la partida y la siguiente clase entra con el campeon de la primera
    # (el servidor reengancha la cuenta a su campeon: es lo correcto para un reintento, no un error).
    piloto.chat("/mobaleave")
    t.esperar(4)
    piloto.cerrar()


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
