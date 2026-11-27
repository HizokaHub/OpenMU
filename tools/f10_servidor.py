"""Duelos de balance SIN CLIENTE: arranca el servidor con la carpeta de pedidos (MOBA_DUEL_JOBS) y le manda los comandos /mobaf10 y /mobaduel como archivos.
No usa el mouse ni el teclado, y no hace falta abrir el juego. Lo usan los scripts de medicion y calibracion.
Uso suelto: python -I -X utf8 tools/f10_servidor.py arrancar | parar | mandar "/mobaf10 status" ..."""
import glob, os, re, subprocess, sys, time
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
INICIAR = RAIZ / "tools/iniciar-todo.ps1"
DEBUG = RAIZ / "src/Startup/bin/Debug"
LOG = RAIZ / "logs/servidor.log"
PEDIDOS = RAIZ / "logs/duel-jobs"
SALIDA_ARRANQUE = RAIZ / "logs/f10_servidor_arranque.log"
_numero = 0


class Detenida(Exception):
    """La medicion no puede seguir."""


def avisar(texto):
    print(f"{time.strftime('%H:%M:%S')} {texto}", flush=True)


def _iniciar_todo(*opciones, extra=None):
    SALIDA_ARRANQUE.parent.mkdir(exist_ok=True)
    entorno = {**os.environ, "MOBA_F10": "1", "MOBA_DUEL_JOBS": str(PEDIDOS), **(extra or {})}
    with open(SALIDA_ARRANQUE, "a", encoding="utf-8") as f:
        return subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(INICIAR), *opciones],
                              stdin=subprocess.DEVNULL, stdout=f, stderr=f, env=entorno, timeout=240).returncode == 0


def log_desde(tam):
    with open(LOG, "rb") as f:
        f.seek(tam)
        return f.read().decode("utf-8", errors="replace")


def limpiar_pedidos():
    PEDIDOS.mkdir(parents=True, exist_ok=True)
    for viejo in PEDIDOS.iterdir():
        if viejo.is_file():
            viejo.unlink()


def parar():
    _iniciar_todo("-Parar", "-Forzar")


def arrancar(extra=None):
    """(Re)inicia el servidor en modo de pedidos y espera a que atienda el primero."""
    parar()
    limpiar_pedidos()
    time.sleep(3)
    if not _iniciar_todo("-SinPortal", extra=extra):
        raise Detenida("el servidor no arranco (ver logs/f10_servidor_arranque.log)")
    time.sleep(5)
    for _ in range(3):
        try:
            mandar(["/mobaf10 on"], limite_s=60, senal="F10 ACTIV")
            return
        except Detenida:
            time.sleep(10)
    raise Detenida("el servidor no atiende los pedidos")


def encolar(comandos):
    """Deja un pedido en la carpeta sin esperar a que termine. Devuelve su nombre."""
    global _numero
    _numero += 1
    nombre = f"{int(time.time())}_{_numero:04d}"
    temporal = PEDIDOS / f"{nombre}.tmp"
    temporal.write_text("\n".join(comandos) + "\n", encoding="utf-8")
    temporal.rename(PEDIDOS / f"{nombre}.job")
    return nombre


def mandar(comandos, limite_s=2700, senal=None):
    """Ejecuta los comandos en el servidor, en orden (un /mobaduel espera a que terminen sus duelos). Devuelve lo que el servidor escribio en el log."""
    inicio = LOG.stat().st_size
    nombre = encolar(comandos)
    limite = time.time() + limite_s
    while time.time() < limite:
        if (PEDIDOS / f"{nombre}.done").exists():
            time.sleep(0.5)   # el registro puede ir un instante por detras
            nuevo = log_desde(inicio)
            if senal and senal not in nuevo:
                raise Detenida(f"el pedido termino sin la respuesta esperada ({senal}): {comandos}")
            return nuevo
        if (PEDIDOS / f"{nombre}.error").exists():
            raise Detenida(f"el pedido fallo en el servidor (ver [MOBA-JOB] en logs/servidor.log): {comandos}")
        time.sleep(1)
    (PEDIDOS / "stop").write_text("stop", encoding="utf-8")
    raise Detenida(f"el pedido no termino en {limite_s} s: {comandos}")


def duelos(cruces, cantidad, nivel, antes=(), paralelo=60, limite_s=2700):
    """Una tanda de duelos con la build real. `cruces` es "all", "espejo" o una lista como "bk-rf,gm-dl"; `antes` son comandos /mobaf10 previos.
    Devuelve (informe, retraso_ms): la separacion media entre acciones de los bots (ideal 280)."""
    patron = str(DEBUG / f"duelos_nivel{nivel}_*.json")
    previos = set(glob.glob(patron))
    salida = mandar([*antes, f"/mobaduel {cruces} {cantidad} real {paralelo} {nivel}"], limite_s=limite_s)
    nuevos = set(glob.glob(patron)) - previos
    if not nuevos:
        raise Detenida(f"la tanda no dejo informe (nivel {nivel}, cruces {cruces})")
    retraso = re.findall(r"salud del servidor: .*?separacion media (\d+) ms", salida)
    return max(nuevos, key=os.path.getmtime), int(retraso[-1]) if retraso else None


def proceso():
    """(MB de memoria, segundos de CPU, hilos, manejadores) del servidor, o None."""
    guion = ("$p = Get-Process MUnique.OpenMU.Startup -ErrorAction SilentlyContinue | Select-Object -First 1; "
             "if ($p) { '{0} {1} {2} {3}' -f [int]($p.WorkingSet64/1MB), [int]$p.CPU, $p.Threads.Count, $p.HandleCount }")
    texto = subprocess.run(["powershell", "-NoProfile", "-Command", guion], capture_output=True, text=True, timeout=60).stdout.split()
    return tuple(int(x) for x in texto) if len(texto) == 4 else None


if __name__ == "__main__":
    orden = sys.argv[1] if len(sys.argv) > 1 else ""
    if orden == "arrancar":
        arrancar()
        avisar("servidor listo para pedidos de duelos")
    elif orden == "parar":
        parar()
    elif orden == "mandar" and len(sys.argv) > 2:
        print(mandar(sys.argv[2:]))
    else:
        print(__doc__)
