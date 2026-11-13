"""Receta automatica del bot: recorre una clase de punta a punta y deja un DIARIO de que paso, cuanto tardo y donde se interrumpio.

Uso (Git Bash: export MSYS_NO_PATHCONV=1), con el servidor arriba y SIN otro Main.exe abierto:
    python -I receta.py <clase> [--fase llegada]
Clases (cuenta testgm, x en la seleccion): dk 330, dw 515, elf 700, dl 880, mg 1070.
El diario queda en capturas/receta/<clase>_<fecha>/diario.md (+ diario.json y capturas SOLO de los pasos que fallan).

Principios (para que sea rapido y repetible, igual para cualquier clase):
  - Cada paso se VERIFICA con el log del servidor o con un cambio de pantalla; no se usan esperas ciegas largas: se sondea cada 0,4 s.
  - Si un paso falla, se guarda captura + las lineas nuevas del log, se escribe el diario y se sale limpiamente (/mobaleave + cerrar).
  - Todo es codigo fijo: el modelo solo lanza la receta y lee el diario.
"""
import argparse
import json
import re
import sys
import time
import traceback
from datetime import datetime
from pathlib import Path

from PIL import ImageChops, ImageGrab

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import piloto  # noqa: E402
import prueba_tienda as t  # noqa: E402

CLASES = {"dk": (330, "Dk"), "dw": (515, "Dw"), "elf": (700, "Elf"), "dl": (880, "Dl"), "mg": (1070, "Mg")}
Y_PERSONAJE = 520
DUMMY_EN_PANTALLA = (870, 205)   # con el heroe en (120,124) y el muñeco en (120,128)
RE_DANO = re.compile(r"\[MOBA-DMG\+\] (?P<atq>.+?) -> (?P<vic>.+?) \| (?P<skill>.+?) \| hp=(?P<hp>\d+) sd=(?P<sd>\d+).*?raw=(?P<raw>\d+).*?victim (?P<vhp>[\d.]+)/(?P<vmax>[\d.]+).*?dist=(?P<dist>[\d.]+).*?lvl=(?P<lvl>[\d.]+)x")
RE_CAST = re.compile(r'\[MOBA-CAST\] "(?P<pj>[^"]+)" cast skill (?P<num>\d+) \("(?P<nombre>[^"]*)"\)')
RE_RECHAZO = re.compile(r'\[MOBA-CAST\] "(?P<pj>[^"]+)" skill #(?P<num>\d+) REJECTED: (?P<motivo>.+)')
RE_MANA = re.compile(r'\[MOBA-MANA\] "(?P<pj>[^"]+)": (?P<act>\d+)/(?P<max>\d+)')
YO_EN_PANTALLA = (685, 290)      # el heroe (para los buffs)
TOPE_GOLPE = 0.30                # el servidor limita cada golpe al 30 % de la vida maxima de la victima


# ---------------------------------------------------------------- log del servidor
def lineas_log():
    return piloto.LOG_SERVIDOR.read_text(encoding="utf-8", errors="replace").splitlines()


def marca():
    return len(lineas_log())


def nuevas(desde):
    return lineas_log()[desde:]


def esperar_log(patron, desde, timeout=8.0, intervalo=0.4):
    """Devuelve la primera linea nueva que cumpla el patron, o None si pasa el tiempo."""
    rx = re.compile(patron)
    fin = time.time() + timeout
    while True:
        for linea in nuevas(desde):
            if rx.search(linea):
                return linea
        if time.time() >= fin:
            return None
        time.sleep(intervalo)


def resumen_log(desde, maximo=8):
    utiles = [l[:200] for l in nuevas(desde) if "[MOBA" in l or "[Warning]" in l or "[Error]" in l]
    utiles = [l for l in utiles if "MOBA-SCORE" not in l and "MOBA-INCOME" not in l and "MOBA-STRUCT" not in l and "MOBA-LAG" not in l]
    return utiles[-maximo:]


# ---------------------------------------------------------------- pantalla
def imagen():
    hwnd = piloto.ventana_o_salir()
    x, y, ancho, alto = piloto.area_cliente(hwnd)
    return ImageGrab.grab(bbox=(x, y, x + ancho, y + alto)).convert("RGB")


def cambio(antes, despues, umbral=4.0):
    """True si las dos imagenes difieren de forma visible (media por canal en 0-255)."""
    diff = ImageChops.difference(antes.resize((160, 90)), despues.resize((160, 90))).convert("L")
    media = sum(diff.tobytes()) / (160 * 90)
    return media > umbral


def esperar_cambio(antes, timeout=10.0):
    fin = time.time() + timeout
    while time.time() < fin:
        time.sleep(0.5)
        if cambio(antes, imagen()):
            return True
    return False


def chat_abierto():
    """La caja de chat abierta es un campo negro liso sobre la barra inferior (y ~655-682); cerrada se ve el mundo."""
    img = imagen().crop((160, 657, 590, 681)).convert("L")
    datos = img.tobytes()
    return (sum(datos) / len(datos)) < 18 and max(datos) < 70


def hay_barra_roja(img, region=(740, 80, 1010, 140)):
    """La barra de vida del muñeco enemigo es roja; cuenta pixeles rojos en la region."""
    x0, y0, x1, y1 = region
    px = img.load()
    rojos = sum(1 for x in range(x0, x1, 2) for y in range(y0, y1, 2) if px[x, y][0] > 170 and px[x, y][1] < 70 and px[x, y][2] < 70)
    return rojos


# ---------------------------------------------------------------- diario
class Interrumpido(Exception):
    pass


class Diario:
    def __init__(self, clase):
        self.dir = piloto.CAPTURAS / "receta" / f"{clase}_{datetime.now():%Y%m%d_%H%M%S}"
        self.dir.mkdir(parents=True, exist_ok=True)
        self.clase = clase
        self.pasos = []
        self.inicio = time.time()

    def paso(self, nombre, fn):
        """Ejecuta fn() -> (ok, detalle). Registra tiempo y detalle; si falla, captura y levanta Interrumpido."""
        t0 = time.time()
        m0 = marca()
        try:
            ok, detalle = fn()
        except SystemExit as e:
            ok, detalle = False, f"salida del bot: {e}"
        except Exception as e:  # noqa: BLE001
            ok, detalle = False, f"{type(e).__name__}: {e} | {traceback.format_exc().splitlines()[-3].strip()}"
        dur = time.time() - t0
        registro = {"paso": nombre, "ok": bool(ok), "segundos": round(dur, 1), "detalle": detalle}
        if not ok:
            captura = self.dir / f"fallo_{len(self.pasos) + 1:02d}_{re.sub(r'[^A-Za-z0-9_-]', '_', nombre)}.png"
            try:
                imagen().save(captura)
                registro["captura"] = str(captura)
            except BaseException as e:  # noqa: BLE001
                registro["captura"] = f"sin captura ({e})"
            registro["log"] = resumen_log(m0)
        self.pasos.append(registro)
        print(f"[{'OK' if ok else 'FALLO'}] {nombre} ({dur:.1f}s) {detalle}")
        if not ok:
            raise Interrumpido(nombre)
        return detalle

    def nota(self, texto):
        self.pasos.append({"paso": "nota", "ok": True, "segundos": 0, "detalle": texto})
        print(f"[nota] {texto}")

    def guardar(self, estado):
        total = time.time() - self.inicio
        (self.dir / "diario.json").write_text(json.dumps({"clase": self.clase, "estado": estado, "segundos": round(total, 1), "pasos": self.pasos}, ensure_ascii=False, indent=1), encoding="utf-8")
        lineas = [f"# Diario de la receta ({self.clase}) - {estado} en {total:.0f} s", "", "| # | Paso | Resultado | s | Detalle |", "|---|---|---|---|---|"]
        for i, p in enumerate(self.pasos, 1):
            lineas.append(f"| {i} | {p['paso']} | {'OK' if p['ok'] else 'FALLO'} | {p['segundos']} | {str(p['detalle']).replace('|', '/')[:220]} |")
        for p in self.pasos:
            if not p["ok"]:
                lineas += ["", f"## Interrumpido en: {p['paso']}", f"Motivo: {p['detalle']}", f"Captura: {p.get('captura', '-')}", "Log del servidor en ese paso:"] + [f"- `{l}`" for l in p.get("log", [])]
        (self.dir / "diario.md").write_text("\n".join(lineas) + "\n", encoding="utf-8")
        print(f"Diario: {self.dir / 'diario.md'}")


# ---------------------------------------------------------------- pasos reutilizables (iguales para toda clase)
def abrir_juego():
    piloto.lanzar()
    return (piloto.buscar_ventana() is not None), "ventana abierta"


def clic_con_efecto(xy, nombre, espera=6.0):
    antes = imagen()
    piloto.clic(*xy)
    ok = esperar_cambio(antes, espera)
    return ok, f"clic {nombre} {'cambio la pantalla' if ok else 'SIN cambio de pantalla'}"


def entrar_al_servidor():
    antes = imagen()
    piloto.clic(*t.CLIC_VALHALLA)
    if not esperar_cambio(antes, 8):
        return False, "Valhalla: la pantalla no cambio"
    antes = imagen()
    piloto.clic(*t.CLIC_VALHALLA_1)
    if not esperar_cambio(antes, 8):
        return False, "Valhalla-1: no salio la ventana de acceso"
    return True, "ventana de acceso abierta"


def acceder():
    antes = imagen()
    piloto.login()
    time.sleep(0.8)
    piloto.clic(*t.CLIC_OK_ACCESO)
    return (esperar_cambio(antes, 12)), "login enviado (cambio de pantalla = llego a la seleccion)"


def elegir_y_conectar(x_personaje):
    antes = imagen()
    piloto.clic(int(x_personaje), Y_PERSONAJE)
    time.sleep(0.8)
    m0 = marca()
    piloto.clic(*t.CLIC_CONECTAR)
    # entrar a Lorencia: la pantalla cambia (carga) y el servidor registra al jugador
    ok = esperar_cambio(antes, 25)
    time.sleep(3)  # la carga del mapa termina de dibujar
    return ok, f"conectar {'ok' if ok else 'sin cambio'}; log nuevo: {resumen_log(m0, 3)}"


def comando(texto, patron_log, timeout=10.0, nombre=None):
    """Escribe un comando de chat y espera su senal en el log del servidor (None = solo lo envia)."""
    m0 = marca()
    if chat_abierto():
        piloto.tecla("enter")   # caja abierta y vacia: se cierra para que el Enter del chat la abra de nuevo
        time.sleep(0.6)
    piloto.chat(texto)
    if patron_log is None:
        time.sleep(1.0)
        return True, f"{texto} enviado; log: {resumen_log(m0, 2)}"
    linea = esperar_log(patron_log, m0, timeout)
    return (linea is not None), (f"{texto} -> {linea[:150]}" if linea else f"{texto}: sin senal '{patron_log}' en {timeout:.0f}s; log: {resumen_log(m0, 4)}")


def verificar_llegada(nombre_pj):
    """El heroe esta a 4 casillas del muñeco: un clic izquierdo sobre el debe producir un golpe con dist <= ~4."""
    img = imagen()
    rojo = hay_barra_roja(img)
    if rojo < 20:
        return False, f"no se ve la barra roja del muñeco en pantalla (pixeles rojos={rojo}): el heroe no llego o la camara esta distinta"
    m0 = marca()
    piloto.clic(*DUMMY_EN_PANTALLA)
    linea = esperar_log(r"\[MOBA-DMG\+\].*dummy0", m0, 10)
    if linea is None:
        return False, f"se ve el muñeco (rojo={rojo}) pero el clic no produjo golpe en 10 s; log: {resumen_log(m0, 4)}"
    m = RE_DANO.search(linea)
    if m:
        return True, f"golpe confirmado: {m['skill']} hp={m['hp']} dist={m['dist']} vida del muñeco {m['vhp']}/{m['vmax']}"
    return True, f"golpe confirmado (no pude leer la distancia): {linea[:160]}"


def salir(diario):
    if piloto.buscar_ventana() is None:
        diario.nota("el cliente ya estaba cerrado")
        return
    try:
        m0 = marca()
        piloto.chat("/mobaleave")
        time.sleep(3)
        piloto.cerrar()
        time.sleep(3)
        cerrado = piloto.buscar_ventana() is None
        diario.nota(f"salida limpia: {'cliente cerrado' if cerrado else 'EL CLIENTE SIGUE ABIERTO'}; log: {resumen_log(m0, 2)}")
    except BaseException as e:  # noqa: BLE001
        diario.nota(f"salida con problemas: {e}")


# ---------------------------------------------------------------- recetas
def fase_llegada(d, x_personaje, nombre_pj):
    """Entra, sube a nivel 30, pone un muñeco y verifica que el heroe llego y puede golpearlo."""
    d.paso("abrir_juego", abrir_juego)
    t.esperar(3)
    d.paso("entrar_servidor", entrar_al_servidor)
    d.paso("acceder", acceder)
    d.paso("elegir_personaje_y_conectar", lambda: elegir_y_conectar(x_personaje))
    d.paso("entrar_arena_/moba", lambda: comando("/moba", r"\[MOBA", 40))
    d.paso("nivel_30", lambda: comando("/mobalevel 30", None))
    d.paso("mover_al_punto_del_muñeco", lambda: comando(f"/move {nombre_pj} 200 120 124", None))
    time.sleep(3)
    d.paso("crear_muñeco", lambda: comando("/mobadummy", r"\[MOBA-BOT\].*dummy0.*spawned", 12))
    time.sleep(2)
    d.paso("verificar_llegada_y_golpe", lambda: verificar_llegada(nombre_pj))


def leer_kit(nombre_pj):
    """Las skills aprendidas por el campeon (numero, nombre) segun la linea de loadout del servidor."""
    for linea in reversed(lineas_log()):
        if f"Clone '\"{nombre_pj}\"'" in linea and "loadout:" in linea:
            m = re.search(r'skills=\["(.+?)"\]', linea)
            if m:
                return [(int(n), nom) for nom, n in re.findall(r"([^,#]+)#(\d+)", m.group(1))]
    return []


def esperado_plano(numero, rango, nivel_escala):
    """Daño esperado sin stats ni items: tabla plana x escala de nivel (el servidor lo reparte con +-10 %)."""
    import skills_registro as sr  # noqa: E402  (tools/)
    tabla = sr.tabla_dano()
    base, por_rango, _ = tabla.get(numero, (70, 18, 0.5))
    return (base + por_rango * (min(max(rango, 1), 5) - 1)) * sr.FLAT * nivel_escala


def lanzar_slot(slot, nombre_pj, esperar_enfriamiento=True):
    """Pulsa la tecla del slot y hace clic derecho en el muñeco (y, si no se lanza, en el propio heroe: es un buff).

    Devuelve un dict con lo medido: skill lanzada, golpes, daño sin tope (raw), distancia, rechazos y avisos."""
    res = {"slot": slot, "lanzada": False, "objetivo": None}
    piloto.tecla(str(slot))
    time.sleep(0.15)
    for intento in range(3):
        for objetivo, xy in (("muñeco", DUMMY_EN_PANTALLA), ("heroe", YO_EN_PANTALLA)):
            m0 = marca()
            piloto.clic(*xy, derecho=True)
            linea = esperar_log(RE_CAST.pattern, m0, 2.0, 0.2)
            if linea:
                c = RE_CAST.search(linea)
                res.update(lanzada=True, objetivo=objetivo, numero=int(c["num"]), nombre=c["nombre"])
                time.sleep(1.3)  # deja llegar los golpes de la skill al log
                golpes = [RE_DANO.search(l) for l in nuevas(m0) if "[MOBA-DMG+]" in l and f"{nombre_pj}" in l.split("->")[0]]
                golpes = [g for g in golpes if g]
                res["golpes"] = len(golpes)
                res["raw_total"] = sum(int(g["raw"]) for g in golpes)
                res["raw_por_golpe"] = round(res["raw_total"] / len(golpes)) if golpes else 0
                res["hp_total"] = sum(int(g["hp"]) for g in golpes)
                res["dist"] = golpes[0]["dist"] if golpes else None
                res["escala_nivel"] = float(golpes[0]["lvl"]) if golpes else None
                res["etiqueta_golpe"] = golpes[0]["skill"] if golpes else None
                res["avisos"] = [l[l.find("]") + 2:][:120] for l in nuevas(m0) if "[Warning]" in l or "[Error]" in l][:2]
                return res
            rechazo = [RE_RECHAZO.search(l) for l in nuevas(m0)]
            rechazo = [r for r in rechazo if r]
            if rechazo:
                res["rechazo"] = rechazo[-1]["motivo"]
                if esperar_enfriamiento and "cooldown" in rechazo[-1]["motivo"]:
                    espera = re.search(r"([\d.]+)s left", rechazo[-1]["motivo"])
                    time.sleep(float(espera.group(1)) + 0.4 if espera else 3)
                    break  # reintenta el slot
                return res
        else:
            continue
    return res


def ronda_skills(d, nombre_pj, rango, etiqueta):
    resultados = []
    for slot in range(1, 10):
        t0 = time.time()
        r = lanzar_slot(slot, nombre_pj)
        r["rango"] = rango
        r["segundos"] = round(time.time() - t0, 1)
        if r.get("lanzada") and r.get("raw_por_golpe") and r.get("escala_nivel"):
            esp = esperado_plano(r["numero"], rango, r["escala_nivel"])
            r["x_esperado"] = round(r["raw_por_golpe"] / esp, 2) if esp else None
        resultados.append(r)
        print(f"  slot {slot}: " + (f"#{r.get('numero')} {r.get('nombre')} -> {r.get('golpes', 0)} golpes raw/golpe={r.get('raw_por_golpe')} x_esperado={r.get('x_esperado')}" if r["lanzada"] else f"NO lanzada ({r.get('rechazo', 'sin senal')})"))
    d.nota(f"ronda {etiqueta}: " + ", ".join(f"{r['slot']}={'#' + str(r['numero']) if r['lanzada'] else 'NO'}" for r in resultados))
    return resultados


def fase_skills(d, x_personaje, nombre_pj):
    fase_llegada(d, x_personaje, nombre_pj)
    kit = leer_kit(nombre_pj)
    d.nota(f"kit del servidor ({len(kit)}): " + ", ".join(f"{n} {nom}" for n, nom in kit))
    todo = {}
    d.paso("rango_1_a_todas", lambda: comando("/mobaskillset 0 1", r"MOBA-SKILLSET", 8))
    todo["r1"] = ronda_skills(d, nombre_pj, 1, "rango 1")
    d.paso("rango_5_a_todas", lambda: comando("/mobaskillset 0 5", r"MOBA-SKILLSET", 8))
    todo["r5"] = ronda_skills(d, nombre_pj, 5, "rango 5")
    (d.dir / "skills.json").write_text(json.dumps({"kit": kit, "rondas": todo}, ensure_ascii=False, indent=1), encoding="utf-8")
    informe = ["# Skills medidas (" + nombre_pj + ")", "", "`x esperado` = daño real sin tope / (tabla plana x escala de nivel): ~1,0 = como en la tabla (±10 %); mucho más o menos = revisar.", "",
               "| Slot | Skill | Rango | ¿Lanzó? | Objetivo | Golpes | Daño/golpe (sin tope) | x esperado | Dist | Avisos / rechazo |", "|---|---|---|---|---|---|---|---|---|---|"]
    for clave in ("r1", "r5"):
        for r in todo[clave]:
            informe.append(f"| {r['slot']} | {r.get('numero', '?')} {r.get('nombre', '')} | {r['rango']} | {'sí' if r['lanzada'] else 'NO'} | {r.get('objetivo') or '-'} | {r.get('golpes', '-')} | {r.get('raw_por_golpe', '-')} | {r.get('x_esperado', '-')} | {r.get('dist', '-')} | {'; '.join(r.get('avisos', [])) or r.get('rechazo', '')} |")
    (d.dir / "skills.md").write_text("\n".join(informe) + "\n", encoding="utf-8")
    d.nota(f"informe de skills: {d.dir / 'skills.md'}")


def comando_con_reintento(texto, patron_log, timeout=10.0, intentos=3):
    """Como comando(), pero si el chat perdio el foco (caja abierta y vacia) cierra la caja con Enter y repite."""
    ultimo = (False, "sin intentos")
    for i in range(intentos):
        ultimo = comando(texto, patron_log, timeout)
        if ultimo[0]:
            return ultimo[0], f"{ultimo[1]} (intento {i + 1})"
        time.sleep(1.0)
    return ultimo[0], f"{ultimo[1]} (tras {intentos} intentos)"


def fase_probe(d, x_personaje, nombre_pj, clases, duelo=None):
    """Entra a la arena, lanza /mobaskilltest (o /mobaduel si se pasa `duelo`) y espera el informe sin tocar mouse ni teclado.

    La medicion corre en el SERVIDOR: solo hacen falta /moba y el comando; el heroe es el GM que da contexto, no participa. El cliente se cierra
    al lanzar la prueba y el PC queda libre. `duelo` = los argumentos de /mobaduel (ej. "all 10 both 6"); espera un informe por pasada."""
    d.paso("abrir_juego", abrir_juego)
    t.esperar(3)
    d.paso("entrar_servidor", entrar_al_servidor)
    d.paso("acceder", acceder)
    d.paso("elegir_personaje_y_conectar", lambda: elegir_y_conectar(x_personaje))
    time.sleep(4)
    m0 = marca()
    etiqueta = "[MOBA-DUEL]" if duelo is not None else "[MOBA-PROBE]"

    def lanzar_prueba():
        # /moba primero (si ya hay un campeon en una partida, la cuenta se reengancha y no hace dano); luego la prueba
        comando("/moba", r"\[MOBA\] Clone|\[MOBA-CLIENT\]", 15)
        time.sleep(8)
        if duelo is not None:
            return comando_con_reintento(f"/mobaduel {duelo}", r"MOBA-DUEL\] ", 30, intentos=2)
        return comando_con_reintento(f"/mobaskilltest {clases}", r"MOBA-PROBE\] class", 30, intentos=2)

    d.paso("lanzar_mobaduel" if duelo is not None else "lanzar_mobaskilltest", lanzar_prueba)
    piloto.cerrar()   # el GM queda como fantasma en la partida y la medicion sigue en el servidor: ya no depende del cliente
    time.sleep(3)
    d.nota("cliente cerrado a proposito: la prueba corre sola en el servidor y el PC queda libre")
    pasadas = 1
    if duelo is not None:
        modo = (duelo.split() + ["all", "10", "both"])[2] if len(duelo.split()) >= 3 else "both"
        pasadas = 2 if modo == "both" else 1
    limite = time.time() + (150 if duelo is not None else 30) * 60
    ultimo = 0
    rutas = []
    while time.time() < limite and len(rutas) < pasadas:
        time.sleep(5)
        for linea in nuevas(m0)[ultimo:]:
            if etiqueta in linea:
                print(linea[linea.find(etiqueta):][:160])
                m = re.search(r"report=(.+)$", linea)
                if m:
                    rutas.append(m.group(1).strip())
        ultimo = len(nuevas(m0))
    d.paso("informe_listo", lambda: (len(rutas) >= pasadas, f"informes: {rutas}" if rutas else "no aparecio report= a tiempo"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("clase", choices=sorted(CLASES))
    ap.add_argument("--fase", default="llegada", choices=["llegada", "skills", "probe"])
    ap.add_argument("--clases", default="all", help="para --fase probe: all, o un alias (dw, bk, fe, mg, dl, sum, rf)")
    ap.add_argument("--duelo", default=None, help="para --fase probe: argumentos de /mobaduel (ej. \"all 10 both 6\"); lanza duelos en vez de /mobaskilltest")
    args = ap.parse_args()
    x, sufijo = CLASES[args.clase]
    nombre_pj = f"testgm{sufijo}"
    if piloto.buscar_ventana():
        sys.exit("Ya hay un Main.exe abierto: cierralo con /mobaleave + piloto.py cerrar antes de correr la receta.")
    d = Diario(args.clase)
    estado = "COMPLETA"
    try:
        if args.fase == "llegada":
            fase_llegada(d, x, nombre_pj)
        elif args.fase == "skills":
            fase_skills(d, x, nombre_pj)
        elif args.fase == "probe":
            fase_probe(d, x, nombre_pj, args.clases, args.duelo)
    except Interrumpido as e:
        estado = f"INTERRUMPIDA en {e}"
    finally:
        salir(d)
        d.guardar(estado)
    print(estado)


if __name__ == "__main__":
    main()
