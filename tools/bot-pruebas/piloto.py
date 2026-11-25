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
    python piloto.py entrar [x] [orden]     TODO EN UNO (2026-11-25): abre el juego, elige servidor, entra con la cuenta, escoge el personaje (x en la seleccion,
                                            515 por defecto = Dw), entra al mundo y manda la orden (por defecto /moba). Cada paso espera la senal real (la pantalla o el
                                            log del servidor) en vez de dormir, y escribe cuanto tardo cada uno.
"""
import json
import os
import subprocess
import sys
import time
from pathlib import Path

import pyautogui
import win32con
import win32gui
import win32process
from PIL import ImageChops, ImageGrab

RAIZ = Path(__file__).resolve().parent
CAPTURAS = RAIZ / "capturas"
CLIENTE = Path(r"C:\Users\aruiz\Proyectos\mu-main\out\build\windows-x64\src\Release")
LOG_SERVIDOR = RAIZ.parent.parent / "logs" / "servidor.log"
CUENTA = RAIZ / os.environ.get("MOBA_CUENTA", "cuenta.local.json")
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


def login(rapido=False):
    """Escribe la clave en la pantalla de acceso (el juego recuerda el ultimo usuario; se reescribe siempre el de la cuenta elegida).
    rapido=True (lo usa `entrar`): sin traer la ventana al frente en cada clic y con pausas cortas."""
    if not CUENTA.exists():
        sys.exit("Falta cuenta.local.json ({\"usuario\": ..., \"clave\": ...}); no esta en git.")
    datos = json.loads(CUENTA.read_text(encoding="utf-8"))
    pulso = 0.04 if rapido else None
    if rapido:
        hwnd = ventana_viva()
        clic_rapido(hwnd, CAMPO_CLAVE[0], CAMPO_CLAVE[1] - 25)
    else:
        clic(CAMPO_CLAVE[0], CAMPO_CLAVE[1] - 25)
    for _ in range(len(datos["usuario"]) + 3 if rapido else 16):
        if rapido:
            pyautogui.keyDown("backspace")
            time.sleep(pulso)
            pyautogui.keyUp("backspace")
        else:
            pulsar("backspace")
    pyautogui.write(datos["usuario"], interval=0.03 if rapido else 0.04)
    if rapido:
        clic_rapido(hwnd, *CAMPO_CLAVE)
    else:
        clic(*CAMPO_CLAVE)
    pyautogui.write(datos["clave"], interval=0.03 if rapido else 0.05)
    pulsar("enter")


# ---------------------------------------------------------------- entrada rapida (2026-11-25)
VALHALLA = (503, 301)
SERVIDOR = (680, 301)
ACEPTAR = (693, 563)
INICIAR = (1262, 697)
Y_PERSONAJE = 520


def ventana_viva():
    """La ventana de verdad: el cliente abre antes una ventana de arranque con el mismo titulo que desaparece, asi que se vuelve a buscar cada vez."""
    for _ in range(40):
        hwnd = buscar_ventana()
        if hwnd is not None and win32gui.IsWindow(hwnd):
            try:
                if area_cliente(hwnd)[2] > 300:
                    return hwnd
            except Exception:
                pass
        time.sleep(0.25)
    raise VentanaPerdida("No encuentro la ventana del juego.")


class VentanaPerdida(RuntimeError):
    """El cliente se cerro solo en mitad del recorrido (pasa a veces al abrirlo justo despues de reiniciar el servidor)."""


def captura(hwnd=None):
    hwnd = ventana_viva()
    x, y, ancho, alto = area_cliente(hwnd)
    return ImageGrab.grab(bbox=(x, y, x + ancho, y + alto))


def cuenta_pixeles(img, region, criterio):
    recorte = img.crop(region)
    datos = recorte.get_flattened_data() if hasattr(recorte, "get_flattened_data") else recorte.getdata()
    return sum(1 for px in datos if criterio(px[0], px[1], px[2]))


def claro(r, g, b):
    return min(r, g, b) > 170


def naranja(r, g, b):
    return r > 190 and 110 < g < 210 and b < 120


def naranja_texto(r, g, b):
    return r > 200 and 120 < g < 200 and b < 90


def naranja_barra(r, g, b):
    return r > 200 and 100 < g < 190 and b < 80


def rojo(r, g, b):
    return r > 150 and g < 60 and b < 60


def diferencia(a, b):
    """Cambio medio (0-255) entre dos capturas reducidas, en gris."""
    from PIL import ImageStat
    pa, pb = a.resize((96, 54)).convert("L"), b.resize((96, 54)).convert("L")
    return ImageStat.Stat(ImageChops.difference(pa, pb)).mean[0]


def espera(descripcion, condicion, limite_s, respaldo_s=0.0, critico=False):
    """Sondea cada 0,15 s hasta que la condicion se cumpla; si no llega, espera lo que esperaba el metodo viejo y sigue (nunca peor que antes)."""
    t0 = time.time()
    while time.time() - t0 < limite_s:
        if condicion():
            return time.time() - t0
        time.sleep(0.15)
    if critico:
        # sin esta senal no tiene sentido seguir (p. ej. la lista de servidores no llega si el servidor aun no esta listo): se cierra el cliente y se reintenta
        print(f"  (aviso) {descripcion}: no llego la senal en {limite_s:.0f} s; cierro el cliente para reintentar")
        cerrar()
        time.sleep(3)
        raise VentanaPerdida(descripcion)
    print(f"  (aviso) {descripcion}: no vi la senal en {limite_s:.0f} s, sigo igual")
    try:
        CAPTURAS.mkdir(exist_ok=True)
        captura().save(CAPTURAS / f"fallo_{descripcion.replace(' ', '_')}.png")   # para ver por que no llego la senal
    except Exception:
        pass
    time.sleep(respaldo_s)
    return time.time() - t0


def esperar_pantalla_nueva(hwnd, base, limite_s, respaldo_s=1.0):
    """Espera a que la pantalla cambie respecto a `base` (>6) y luego quede quieta (dos capturas seguidas casi iguales)."""
    estado = {"cambio": False, "ultima": None}

    def listo():
        actual = captura(hwnd)
        if not estado["cambio"]:
            if diferencia(actual, base) > 6:
                estado["cambio"] = True
                estado["ultima"] = actual
            return False
        quieto = diferencia(actual, estado["ultima"]) < 2.5
        estado["ultima"] = actual
        return quieto

    return espera("pantalla nueva", listo, limite_s, respaldo_s)


def lineas_log():
    return LOG_SERVIDOR.read_text(encoding="utf-8", errors="replace").splitlines() if LOG_SERVIDOR.exists() else []


def entrar(x_personaje=515, orden="/moba"):
    """Con reintento: si el cliente se cierra solo, se abre otra vez (hasta 3 intentos)."""
    for intento in range(1, 4):
        try:
            return _entrar(x_personaje, orden)
        except VentanaPerdida:
            print(f"  el cliente se cerro solo (intento {intento}/3); lo abro otra vez")
            time.sleep(2)
    sys.exit("No consegui entrar al juego en 3 intentos.")


def _entrar(x_personaje=515, orden="/moba"):
    """Del escritorio a dentro de la arena en el menor tiempo posible (cada paso espera su senal real)."""
    inicio = time.time()
    marcas = []
    pyautogui.PAUSE = 0.03   # la pausa de 0,25 s tras CADA accion de pyautogui era la que mas tiempo comia (cada clic eran 0,75 s y cada tecla 0,5 s)

    def paso(nombre, t):
        marcas.append((nombre, t))
        print(f"  {nombre}: {t:.1f} s")

    base_log = len(lineas_log())
    t = time.time()
    if not buscar_ventana():
        subprocess.Popen([str(CLIENTE / "Main.exe")], cwd=str(CLIENTE))
        espera("ventana", lambda: buscar_ventana() is not None, ESPERA_VENTANA_S)
    hwnd = ventana_viva()
    al_frente(hwnd)
    paso("ventana", time.time() - t)

    # 1) la pantalla de servidores: el boton "Valhalla" con su texto claro
    t = time.time()
    espera("boton Valhalla", lambda: cuenta_pixeles(captura(hwnd), (452, 291, 557, 312), claro) > 25, 25, 4, critico=True)
    paso("lista de servidores", time.time() - t)

    # 2) clic en el grupo: aparecen los servidores (texto naranja)
    clic_rapido(hwnd, *VALHALLA)
    t = time.time()
    espera("servidores", lambda: cuenta_pixeles(captura(hwnd), (587, 291, 777, 365), naranja) > 40, 8, 1.5, critico=True)
    paso("servidores desplegados", time.time() - t)

    # 3) clic en el servidor: aparece la ventana de acceso (su aviso naranja "Only save the password...")
    clic_rapido(hwnd, *SERVIDOR)
    t = time.time()
    espera("pantalla de acceso", lambda: cuenta_pixeles(captura(), (520, 560, 860, 640), naranja_texto) > 150, 12, 2.0, critico=True)
    paso("pantalla de acceso", time.time() - t)

    # 4) la cuenta: el servidor lo avisa con [MOBA-CLIENT] build (la primera vez, al entrar a la cuenta)
    construcciones = sum("[MOBA-CLIENT] build" in l for l in lineas_log()[base_log:])
    t = time.time()
    login(rapido=True)
    espera("acceso a la cuenta", lambda: sum("[MOBA-CLIENT] build" in l for l in lineas_log()[base_log:]) > construcciones, 20, 3)
    paso("cuenta", time.time() - t)

    # 5) aceptar (habilita el boton Iniciar, que se pone naranja), escoger el personaje, iniciar; el mundo esta listo cuando se ve el orbe rojo de la vida
    time.sleep(0.5)
    clic_rapido(hwnd, *ACEPTAR)
    t = time.time()
    espera("boton Iniciar", lambda: cuenta_pixeles(captura(), (1235, 680, 1290, 715), naranja_texto) > 100, 6, 1.5)
    clic_rapido(hwnd, x_personaje, Y_PERSONAJE)
    time.sleep(0.4)
    clic_rapido(hwnd, *INICIAR)
    # en el mundo aparece arriba el boton naranja de la barra del mapa (sirve en Lorencia y en la arena; el orbe rojo solo estaba en la arena)
    espera("entrar al mundo", lambda: cuenta_pixeles(captura(), (362, 2, 408, 26), naranja_barra) > 30, 25, 6)
    time.sleep(0.8)   # el cliente termina de aceptar el chat
    paso("personaje y mundo", time.time() - t)

    # 6) la orden (por defecto /moba) y su respuesta en el log
    if orden:
        marca = len(lineas_log())
        chat(orden)
        t = time.time()
        if orden == "/moba":
            # el servidor deja [MOBA] Clone al crear el campeon; si ya estabas dentro de una partida no hay nada que esperar
            espera("entrar al MOBA", lambda: any("[MOBA] Clone" in l and "testgm" in l for l in lineas_log()[marca:]), 4, 0)
        paso(orden, time.time() - t)
    print(f"Listo en {time.time() - inicio:.1f} s")


def clic_rapido(hwnd, cx, cy):
    """Como clic(), pero sin volver a traer la ventana al frente cada vez (ya esta)."""
    px, py = a_pantalla(ventana_viva(), cx, cy)
    pyautogui.moveTo(px, py, duration=0.08)
    time.sleep(0.08)
    pyautogui.mouseDown(button="left")
    time.sleep(PAUSA_CLIC_S)
    pyautogui.mouseUp(button="left")


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
    elif orden == "entrar":
        entrar(int(resto[0]) if resto else 515, resto[1] if len(resto) > 1 else "/moba")
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
