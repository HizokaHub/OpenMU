"""Calibra el balance 1v1 de F10 por nivel con duelos SIN CLIENTE (tools/f10_servidor.py). Reemplaza a calibrar_nivel_f10.py.

Diferencias con el metodo anterior (psi por raza + cruces, midiendo victorias, siempre los 28 cruces):
  * Se ajusta cada cruce POR SEPARADO: en un 1v1 el valor de un cruce solo cambia ese duelo. Los espejos no se corren.
  * La senal es la VIDA con signo que le queda al ganador (vida + SD, promedio del cruce; positiva si gana el primero). Con las mismas peleas tiene la mitad
    de error que contar victorias (medicion del 2026-11-27: 0,035 contra 0,061 de error en su escala) y predice las victorias casi en linea recta.
  * Solo se repiten los cruces que siguen desparejos; el paso sale de lo que ese cruce ya mostro que responde (secante).
  * Al final, una pasada de comprobacion con todos los cruces; los que fallen vuelven a ajustarse.
Meta: |vida| <= 0,10 en la comprobacion (equivale a ~30-70 % de victorias) y duelo medio de 10 a 17 s. Mientras ajusta apunta a 0,06 para dejar margen al azar.

Uso: python -I -X utf8 tools/calibrar_rapido_f10.py "1 2 3" [--duelos 24] [--comprobar 40] [--manos escudo|doble|uno] [--sin-reiniciar]
Detener al terminar la tanda en curso: crear tools/f10_parar.flag. Estado en tools/f10_calibracion.json (lo lee generar_tablas_f10.py)."""
import argparse, ctypes, json, os, subprocess, sys, time
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(RAIZ / "tools"))
import f10_servidor as servidor
from f10_tablas import calibrados, interpolar

ESTADO = RAIZ / "tools/f10_calibracion.json"
PARAR = RAIZ / "tools/f10_parar.flag"
FAM = {3: "wiz", 6: "kn", 11: "elf", 12: "mg", 16: "dl", 20: "sum", 24: "rf"}
ALIAS = {3: "gm", 6: "bk", 11: "he", 12: "mg", 16: "dl", 20: "sum", 24: "rf"}   # alias de /mobaduel: "fe" es la elfa inicial (clase 8), no la High Elf de los duelos
NOM = {3: "GM", 6: "BK", 11: "FE", 12: "MG", 16: "DL", 20: "SUM", 24: "RF"}
CLASES = list(FAM)
CRUCES = [(a, b) for i, a in enumerate(CLASES) for b in CLASES[i + 1:]]
DUR_MIN, DUR_MAX, DUR_META = 10.3, 16.7, 12.5      # se acepta de 10 a 17 s (usuario, 2026-11-27: hasta 17 s el duelo es valido)
META_AJUSTE, META_FINAL = 0.06, 0.10
PENDIENTE_INICIAL = 2.0        # cambio de la vida del ganador por unidad del valor del cruce (teoria: ~2; despues manda lo medido)
PASO_MAXIMO, VALOR_MAXIMO = 0.25, 1.8
RETRASO_MAX_MS = 320


class Parada(Exception):
    pass


def analizar(informe):
    """Por cruce: (vida con signo, victorias del primero); y la duracion media."""
    d = json.load(open(informe, encoding="utf-8"))
    cruces, segundos, n = {}, 0.0, 0
    for p in d["pairs"]:
        filas = p["duels"]
        if not filas:
            continue
        vida = sum(x["aLeft"] if x["winner"] == "a" else -x["bLeft"] if x["winner"] == "b" else 0.0 for x in filas) / len(filas)
        victorias = sum({"a": 1.0, "b": 0.0}.get(x["winner"], 0.5) for x in filas) / len(filas)
        cruces[(p["a"], p["b"])] = (vida, victorias)
        segundos += sum(x["seconds"] for x in filas)
        n += len(filas)
    return cruces, segundos / max(n, 1)


class Nivel:
    def __init__(self, nivel, args):
        self.nivel, self.args = nivel, args
        # parte de los niveles ya calibrados con este metodo (el mas cercano o el punto medio entre dos); las tablas viejas solo si no hay ninguno
        todos = calibrados()
        nuevos = {k: v for k, v in todos.items() if v.get("metodo") == 2}
        base = interpolar(nuevos or todos, nivel)
        self.psi = {int(k): v for k, v in base["psi"].items()}
        self.pares = {tuple(int(x) for x in k.split("-")): v for k, v in base["pares"].items()}
        self.dano, self.vida = base["dano"], base.get("vida", 1.0)
        # la parte antisimetrica de cada cruce es lo que se ajusta; la simetrica (cambia cuanto dura, no quien gana) se conserva
        self.x = {k: (self.pares.get(k, 0.0) - self.pares.get(k[::-1], 0.0)) / 2 for k in CRUCES}
        self.simetrica = {k: (self.pares.get(k, 0.0) + self.pares.get(k[::-1], 0.0)) / 2 for k in CRUCES}
        self.puntos = {k: [] for k in CRUCES}      # (valor, vida medida, duelos) con la escala de vida actual
        self.ultimo = {}                            # cruce -> (vida, victorias) de su ultima medicion
        self.registro = []
        self.duelos = 0
        self.retraso_malo = 0

    def valores_pares(self):
        pares = dict(self.pares)
        for (a, b) in CRUCES:
            pares[(a, b)] = self.simetrica[(a, b)] + self.x[(a, b)]
            pares[(b, a)] = self.simetrica[(a, b)] - self.x[(a, b)]
        return pares

    def comandos(self, cruces):
        n = self.nivel
        lista = [f"/mobaf10 psi {FAM[c]} {self.psi[c]:.4f} {n}" for c in CLASES]
        lista.append(f"/mobaf10 pair clear x {n}")
        lista += [f"/mobaf10 pair {FAM[a]}-{FAM[b]} {v:.4f} {n}" for (a, b), v in self.valores_pares().items() if abs(v) > 1e-6]
        lista += [f"/mobaf10 dmgk {n} {self.dano:.4f}", f"/mobaf10 hpk {n} {self.vida:.4f}", f"/mobaf10 manos {self.args.manos}"]
        return lista

    def correr(self, cruces, cantidad, fase):
        if PARAR.exists():
            PARAR.unlink()
            raise Parada("parada pedida")
        texto = ",".join(f"{ALIAS[a]}-{ALIAS[b]}" for a, b in cruces)
        informe, retraso = servidor.duelos(texto, cantidad, self.nivel, antes=self.comandos(cruces))
        medido, duracion = analizar(informe)
        faltan = [c for c in cruces if c not in medido]
        if faltan:
            raise servidor.Detenida(f"el informe no trae {len(faltan)} cruces")
        self.duelos += len(cruces) * cantidad
        if retraso is None or retraso > RETRASO_MAX_MS:
            self.retraso_malo += 1
            servidor.avisar(f"AVISO nivel {self.nivel}: servidor atrasado o sin dato ({retraso} ms por accion de bot; lo normal es 280)")
        for c in cruces:
            self.ultimo[c] = medido[c]
            self.puntos[c].append((self.x[c], medido[c][0], cantidad))
        self.registro.append({"fase": fase, "cruces": len(cruces), "duelos": cantidad, "duracion": round(duracion, 1), "retraso_ms": retraso,
                              "informe": Path(informe).name,
                              "medido": {f"{NOM[a]}-{NOM[b]}": [round(self.x[(a, b)], 4), round(medido[(a, b)][0], 3), round(medido[(a, b)][1], 3)] for a, b in cruces}})
        return medido, duracion

    def paso(self, c):
        valor, vida, _ = self.puntos[c][-1]
        pendiente = PENDIENTE_INICIAL
        # secante contra la medicion anterior mas separada: si el cruce respondio poco o al reves (azar), se usa un limite
        otros = [p for p in self.puntos[c][:-1] if abs(p[0] - valor) >= 0.03]
        if otros:
            x0, m0, _ = max(otros, key=lambda p: abs(p[0] - valor))
            pendiente = max(0.7, min(6.0, (vida - m0) / (valor - x0)))
        paso = max(-PASO_MAXIMO, min(PASO_MAXIMO, -0.85 * vida / pendiente))
        # si ya hay mediciones a los dos lados del cero, el valor nuevo no se sale del tramo que las separa
        abajo = [p[0] for p in self.puntos[c] if p[1] < 0]
        arriba = [p[0] for p in self.puntos[c] if p[1] > 0]
        nuevo = valor + paso
        if abajo and arriba and max(abajo) < min(arriba) and not max(abajo) < nuevo < min(arriba):
            nuevo = (max(abajo) + min(arriba)) / 2
        return max(-VALOR_MAXIMO, min(VALOR_MAXIMO, nuevo))

    def guardar(self, resultado=None):
        estado = json.load(open(ESTADO)) if ESTADO.exists() else {}
        entrada = {"psi": {str(c): v for c, v in self.psi.items()}, "pares": {f"{a}-{b}": round(v, 4) for (a, b), v in self.valores_pares().items()},
                   "dano": self.dano, "vida": round(self.vida, 4), "registro": self.registro, "metodo": 2}
        if resultado:
            entrada["mejor"] = resultado
        else:
            entrada["metodo"] = "en curso"
        estado[str(self.nivel)] = entrada
        json.dump(estado, open(ESTADO, "w"), indent=1)

    def calibrar(self):
        n, args, t0 = self.nivel, self.args, time.time()
        # 1) todos los cruces, y la duracion (con la VIDA del nivel: el dano casi no acorta el duelo, por el tope por golpe y el freno de rafaga)
        for it in range(3):
            medido, duracion = self.correr(CRUCES, args.duelos, "inicio")
            servidor.avisar(f"nivel {n} inicio: {duracion:.1f} s | fuera de la meta: {sum(abs(m[0]) > META_FINAL for m in medido.values())}/21")
            if DUR_MIN <= duracion <= DUR_MAX:
                break
            self.vida = max(0.15, min(6.0, self.vida * max(0.7, min(1.4, DUR_META / duracion))))
            self.puntos = {k: [] for k in CRUCES}
        else:
            for c in CRUCES:
                self.puntos[c] = [(self.x[c], self.ultimo[c][0], args.duelos)]
        # 2) ajustar solo los desparejos  3) comprobar todos
        for ronda in (1, 2, 3):
            for it in range(6):
                activos = [c for c in CRUCES if abs(self.ultimo[c][0]) > META_AJUSTE]
                if not activos:
                    break
                for c in activos:
                    self.x[c] = self.paso(c)
                medido, duracion = self.correr(activos, args.duelos, f"ajuste {ronda}.{it}")
                quedan = sum(abs(m[0]) > META_AJUSTE for m in medido.values())
                servidor.avisar(f"nivel {n} ajuste {ronda}.{it}: {len(activos)} cruces, quedan {quedan} | {duracion:.1f} s")
                self.guardar()
            medido, duracion = self.correr(CRUCES, args.comprobar, f"comprobacion {ronda}")
            malos = [c for c in CRUCES if abs(medido[c][0]) > META_FINAL]
            fuera = sum(abs(medido[c][1] - 0.5) > 0.2 for c in CRUCES)
            sobre = sum(abs(medido[c][1] - 0.5) > 0.3 for c in CRUCES)
            peor = max(CRUCES, key=lambda c: abs(medido[c][0]))
            resultado = {"pasada": f"comprobacion {ronda}", "fuera_vida": len(malos), "fuera3070": fuera, "sobre80": sobre, "duracion": round(duracion, 1),
                         "peor": f"{NOM[peor[0]]}-{NOM[peor[1]]} {medido[peor][0]:+.2f}", "duelos": self.duelos, "minutos": round((time.time() - t0) / 60, 1),
                         "tandas_atrasadas": self.retraso_malo}
            self.guardar(resultado)
            servidor.avisar(f"nivel {n} COMPROBACION {ronda}: {duracion:.1f} s | desparejos por vida: {len(malos)}/21 | victorias fuera de 30-70: {fuera}, sobre 80: {sobre} "
                            f"| peor {resultado['peor']} | {self.duelos} duelos, {resultado['minutos']} min")
            if len(malos) <= 1 and DUR_MIN - 0.3 <= duracion <= DUR_MAX + 0.3:
                return resultado
        return resultado


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("niveles")
    ap.add_argument("--duelos", type=int, default=24)
    ap.add_argument("--comprobar", type=int, default=40)
    ap.add_argument("--manos", choices=("escudo", "doble"), default="escudo")
    ap.add_argument("--reinicio", type=int, default=4, help="niveles entre reinicios del servidor")
    ap.add_argument("--sin-reiniciar", action="store_true", help="usar el servidor que ya esta en modo de pedidos")
    args = ap.parse_args()
    if os.name == "nt":
        ctypes.windll.kernel32.SetThreadExecutionState(0x80000000 | 0x00000001)   # el PC no se suspende mientras calibra
    PARAR.unlink(missing_ok=True)
    niveles = [int(x) for x in args.niveles.split()]
    codigo = 0
    try:
        en_sesion = args.reinicio
        for i, nivel in enumerate(niveles):
            for intento in (1, 2):
                try:
                    if en_sesion >= args.reinicio and not (args.sin_reiniciar and i == 0 and intento == 1):
                        servidor.arrancar()
                        en_sesion = 0
                    servidor.avisar(f"[{i + 1}/{len(niveles)}] calibrando el nivel {nivel}")
                    Nivel(nivel, args).calibrar()
                    en_sesion += 1
                    break
                except (servidor.Detenida, OSError, subprocess.SubprocessError) as error:
                    if intento == 2:
                        raise
                    servidor.avisar(f"AVISO nivel {nivel}: {error}; reinicio el servidor y lo repito")
                    en_sesion = args.reinicio
    except (Parada, KeyboardInterrupt) as error:
        servidor.avisar(f"DETENIDA: {error}")
    except Exception as error:
        servidor.avisar(f"DETENIDA con error: {error!r}")
        codigo = 1
    finally:
        if not args.sin_reiniciar:
            servidor.parar()
    servidor.avisar(f"FIN ({'con error' if codigo else 'sin errores'})")
    return codigo


if __name__ == "__main__":
    sys.exit(main())
