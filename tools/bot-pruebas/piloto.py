"""Piloto del bot de pruebas del MOBA (Windows).

Maneja el cliente Main.exe como lo haria una persona: lo lanza, captura su ventana, hace clic, escribe y lee el log del servidor.
Todo local y gratis. Mientras corre mueve el raton y el teclado reales: no toques el PC durante una prueba.

Uso (desde la carpeta tools/bot-pruebas):
    python piloto.py lanzar                 abre Main.exe (si no hay otro abierto) y espera la ventana
    python piloto.py foto nombre            captura la ventana del juego en capturas/<nombre>.png y muestra su tamano
    python piloto.py clic X Y [derecho]     clic en coordenadas de la ventana (X, Y como en la captura)
    python piloto.py mover X Y              mueve el raton (para ver tooltips)
    python piloto.py rueda X Y N            gira la rueda N pasos sobre el punto (N negativo = bajar)
    python piloto.py escribir "texto"       escribe texto (sin Enter)
    python piloto.py tecla enter            pulsa una tecla (enter, tab, esc, f1, a, ...)
    python piloto.py chat "/moba"           abre el chat con Enter, escribe y envia
    python piloto.py log [N]                ultimas N lineas del log del servidor con [MOBA-...] o avisos (por defecto 25)
    python piloto.py estado                 dice si el juego esta abierto y su ventana
    python piloto.py cerrar                 cierra el juego pidiendolo a su ventana (no mata procesos)
    python piloto.py login                  escribe la clave de cuenta.local.json en la pantalla de acceso
"""
import json
import subprocess
import sys
import time
from pathlib import Path

import pyautogui
import win32con
import win32gui
import win32process
from PIL import ImageGrab

RAIZ = Path(__file__).resolve().parent
CAPTURAS = RAIZ / "capturas"
CLIENTE = Path(r"C:\Users\aruiz\Proyectos\mu-main\out\build\windows-x64\src\Release")
LOG_SERVIDOR = RAIZ.parent.parent / "logs" / "servidor.log"
CUENTA = RAIZ / "cuenta.local.json"
TITULO = "MU Online"
ESPERA_VENTANA_S = 60
PAUSA_ACCION_S = 0.25
PAUSA_ANTES_CLIC_S = 0.15
CAMPO_CLAVE = (705, 491)  # campo "Contrasena" de la pantalla de acceso (ventana de 1366x768)
PAUSA_CLIC_S = 0.09
PAUSA_TECLA_S = 0.09
PASO_RUEDA = 120  # un "clic" de la rueda de Windows
FILTRO_LOG = ("[MOBA", "[Warning]", "[Error]", "Exception")

pyautogui.FAILSAFE = True  # mover el raton a una esquina de la pantalla aborta el bot
pyautogui.PAUSE = PAUSA_ACCION_S


def buscar_ventana():
    encontrada = []

    def revisar(hwnd, _):
        if win32gui.IsWindowVisible(hwnd) and win32gui.GetWindowText(hwnd) == TITULO:
            encontrada.append(hwnd)

    win32gui.EnumWindows(revisar, None)
    return encontrada[0] if encontrada else None


def area_cliente(hwnd):
    izq, arr, der, aba = win32gui.GetClientRect(hwnd)
    x0, y0 = win32gui.ClientToScreen(hwnd, (izq, arr))
    return x0, y0, der - izq, aba - arr


def al_frente(hwnd):
    if win32gui.IsIconic(hwnd):
        win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
    pyautogui.press("alt")  # permite SetForegroundWindow desde un proceso en segundo plano
    win32gui.SetForegroundWindow(hwnd)
    time.sleep(0.3)


def pulsar(nombre):
    """Pulsa y suelta una tecla con una pausa: el juego no ve las pulsaciones instantaneas."""
    pyautogui.keyDown(nombre)
    time.sleep(PAUSA_TECLA_S)
    pyautogui.keyUp(nombre)


def ventana_o_salir():
    hwnd = buscar_ventana()
    if hwnd is None:
        sys.exit("El juego no esta abierto (usa: piloto.py lanzar).")
    al_frente(hwnd)
    return hwnd


def lanzar():
    if buscar_ventana():
        print("Ya hay un Main.exe abierto; no se abre otro.")
        return
    subprocess.Popen([str(CLIENTE / "Main.exe")], cwd=str(CLIENTE))
    limite = time.time() + ESPERA_VENTANA_S
    while time.time() < limite:
        if buscar_ventana():
            time.sleep(3)
            print("Ventana del juego lista.")
            return
        time.sleep(1)
    sys.exit("La ventana del juego no aparecio a tiempo.")


def foto(nombre):
    hwnd = ventana_o_salir()
    x, y, ancho, alto = area_cliente(hwnd)
    CAPTURAS.mkdir(exist_ok=True)
    destino = CAPTURAS / f"{nombre}.png"
    ImageGrab.grab(bbox=(x, y, x + ancho, y + alto)).save(destino)
    print(f"{destino} ({ancho}x{alto})")


def a_pantalla(hwnd, cx, cy):
    x, y, _, _ = area_cliente(hwnd)
    return x + int(cx), y + int(cy)


def clic(cx, cy, derecho=False):
    hwnd = ventana_o_salir()
    px, py = a_pantalla(hwnd, cx, cy)
    boton = "right" if derecho else "left"
    pyautogui.moveTo(px, py, duration=0.15)
    time.sleep(PAUSA_ANTES_CLIC_S)  # el juego lee la posicion del raton un cuadro antes del clic
    pyautogui.mouseDown(button=boton)
    time.sleep(PAUSA_CLIC_S)
    pyautogui.mouseUp(button=boton)


def rueda(cx, cy, pasos):
    """Gira la rueda del raton sobre un punto de la ventana (pasos > 0 sube, < 0 baja)."""
    hwnd = ventana_o_salir()
    px, py = a_pantalla(hwnd, cx, cy)
    pyautogui.moveTo(px, py, duration=0.15)
    time.sleep(PAUSA_ANTES_CLIC_S)
    for _ in range(abs(int(pasos))):
        pyautogui.scroll(PASO_RUEDA if pasos > 0 else -PASO_RUEDA)
        time.sleep(0.15)


def mover(cx, cy):
    hwnd = ventana_o_salir()
    px, py = a_pantalla(hwnd, cx, cy)
    pyautogui.moveTo(px, py, duration=0.15)


def escribir(texto):
    ventana_o_salir()
    pyautogui.write(texto, interval=0.04)


def tecla(nombre):
    ventana_o_salir()
    pulsar(nombre)


def chat(texto):
    ventana_o_salir()
    pulsar("enter")
    time.sleep(0.3)
    pyautogui.write(texto, interval=0.04)
    time.sleep(0.2)
    pulsar("enter")


def login():
    """Escribe la clave en la pantalla de acceso (el juego recuerda el usuario; si hiciera falta otro, se escribe antes a mano)."""
    if not CUENTA.exists():
        sys.exit("Falta cuenta.local.json ({\"usuario\": ..., \"clave\": ...}); no esta en git.")
    datos = json.loads(CUENTA.read_text(encoding="utf-8"))
    clic(*CAMPO_CLAVE)
    pyautogui.write(datos["clave"], interval=0.05)
    pulsar("enter")


def log(cantidad=25):
    if not LOG_SERVIDOR.exists():
        sys.exit(f"No existe {LOG_SERVIDOR}")
    lineas = LOG_SERVIDOR.read_text(encoding="utf-8", errors="replace").splitlines()
    utiles = [l[:230] for l in lineas if any(f in l for f in FILTRO_LOG) and "MOBA-STRUCT" not in l and "MOBA-LAG" not in l]
    print("\n".join(utiles[-cantidad:]))


def estado():
    hwnd = buscar_ventana()
    if hwnd is None:
        print("cerrado")
        return
    x, y, ancho, alto = area_cliente(hwnd)
    _, pid = win32process.GetWindowThreadProcessId(hwnd)
    print(f"abierto pid={pid} cliente={ancho}x{alto} en ({x},{y})")


def cerrar():
    hwnd = buscar_ventana()
    if hwnd is None:
        print("El juego ya estaba cerrado.")
        return
    win32gui.PostMessage(hwnd, win32con.WM_CLOSE, 0, 0)
    print("Pedido el cierre a la ventana.")


def main(argumentos):
    if not argumentos:
        print(__doc__)
        return
    orden, resto = argumentos[0], argumentos[1:]
    if orden == "lanzar":
        lanzar()
    elif orden == "foto":
        foto(resto[0] if resto else "captura")
    elif orden == "clic":
        clic(resto[0], resto[1], derecho=len(resto) > 2 and resto[2] == "derecho")
    elif orden == "rueda":
        rueda(resto[0], resto[1], resto[2])
    elif orden == "mover":
        mover(resto[0], resto[1])
    elif orden == "escribir":
        escribir(resto[0])
    elif orden == "tecla":
        tecla(resto[0])
    elif orden == "chat":
        chat(resto[0])
    elif orden == "login":
        login()
    elif orden == "log":
        log(int(resto[0]) if resto else 25)
    elif orden == "estado":
        estado()
    elif orden == "cerrar":
        cerrar()
    else:
        print(__doc__)


if __name__ == "__main__":
    main(sys.argv[1:])
