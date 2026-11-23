"""Edita los datos del arbol maestro del cliente MU (F3 de Rush: el nodo del Blade Master).
Archivos (BuxConvert por registro, se descifra con la clave FC CF AB desde el byte 0 de cada registro):
  MasterSkillTreeData.bmd         512 registros de 24 bytes + checksum GenerateCheckSum2(buf, 12288, 0x2BC1)
  MasterSkillTooltip_<idioma>.bmd 512 registros de 616 bytes + 4 bytes finales (el cliente no los comprueba; se conservan)
Uso:
  python -I -X utf8 tools/master_tree_fila.py prueba-arbol   <MasterSkillTreeData.bmd>
  python -I -X utf8 tools/master_tree_fila.py ver-arbol      <MasterSkillTreeData.bmd> [clase]
  python -I -X utf8 tools/master_tree_fila.py rush-arbol     <MasterSkillTreeData.bmd> [salida]   agrega/actualiza el nodo de Rush (indice 57, Blade Master, rama central)
  python -I -X utf8 tools/master_tree_fila.py prueba-tooltip <MasterSkillTooltip_xxx.bmd>
  python -I -X utf8 tools/master_tree_fila.py rush-tooltip   <MasterSkillTooltip_xxx.bmd> <eng|por|spn> [salida]
"""
import struct
import sys

BUX = (0xFC, 0xCF, 0xAB)
RUSH = 638
INDICE_RUSH = 57        # rama central (36 + 1) + fila 6 (5 * 4) + columna 1 (0)
CLASE_BM = 1            # MASTER_SKILL_TREE_CLASS_BLADEMASTER
TREE_TAM, TREE_N, TREE_CLAVE = 24, 512, 0x2BC1
TIP_TAM, TIP_N = 616, 512
CAMPOS_TIP = ((6, 64), (70, 256), (326, 32), (358, 64), (422, 64), (486, 64), (550, 64))   # Info1..Info7 (tras int + WORD)

TEXTOS = {
    "eng": ("Rank %d, Skill Level: %d/%d", "Rush to an enemy within 7 tiles and inflict a powerful blow. Cooldown: 20 s (level 1) to 16 s (level 5).",
            "Required Point: %d point", "", "Requires 40 points in this branch.", "", ""),
    "por": ("Rank %d, Nivel da habilidade: %d/%d", "Avance contra um inimigo a ate 7 casas e aplique um golpe poderoso. Recarga: 20 s (nivel 1) a 16 s (nivel 5).",
            "Ponto necessario: %d ponto", "", "Requer 40 pontos neste ramo.", "", ""),
    "spn": ("Rango %d, Nivel de habilidad: %d/%d", "Carga contra un enemigo a hasta 7 casillas y le asesta un golpe poderoso. Enfriamiento: 20 s (nivel 1) a 16 s (nivel 5).",
            "Punto necesario: %d punto", "", "Requiere 40 puntos en esta rama.", "", ""),
}


def bux(datos):
    return bytes(b ^ BUX[i % 3] for i, b in enumerate(datos))


def checksum(buf, clave=TREE_CLAVE):
    r = (clave << 9) & 0xFFFFFFFF
    for c in range(0, len(buf) - 3, 4):
        t = struct.unpack_from("<I", buf, c)[0]
        if (c // 4 + clave) % 2 == 0:
            r ^= t
        else:
            r = (r + t) & 0xFFFFFFFF
        if c % 16 == 0:
            r ^= ((clave + r) & 0xFFFFFFFF) >> ((c // 4) % 8 + 1)
    return r & 0xFFFFFFFF


# --- arbol ---
def leer_arbol(ruta):
    d = open(ruta, "rb").read()
    assert len(d) == TREE_TAM * TREE_N + 4, f"tamano inesperado {len(d)}"
    cifrado = d[:-4]
    assert checksum(cifrado) == struct.unpack("<I", d[-4:])[0], "checksum no coincide"
    return [bytearray(bux(cifrado[i * TREE_TAM:(i + 1) * TREE_TAM])) for i in range(TREE_N)]


def guardar_arbol(filas, ruta):
    cifrado = b"".join(bux(bytes(f)) for f in filas)
    open(ruta, "wb").write(cifrado + struct.pack("<I", checksum(cifrado)))


def campos_arbol(f):
    indice, clase, grupo, puntos, maximo, flecha, r1, r2, skill, valor = struct.unpack("<HHBBBBiiif", bytes(f))
    return dict(Index=indice, Clase=clase, Grupo=grupo, Puntos=puntos, Max=maximo, Flecha=flecha, Req=(r1, r2), Skill=skill, Valor=valor)


def fila_rush():
    return bytearray(struct.pack("<HHBBBBiiif", INDICE_RUSH, CLASE_BM, 1, 1, 5, 0, 0, 0, RUSH, 0.0))


# --- tooltip ---
def leer_tooltip(ruta):
    d = open(ruta, "rb").read()
    assert len(d) == TIP_TAM * TIP_N + 4, f"tamano inesperado {len(d)}"
    return [bytearray(bux(d[i * TIP_TAM:(i + 1) * TIP_TAM])) for i in range(TIP_N)], d[-4:]


def guardar_tooltip(filas, cola, ruta):
    open(ruta, "wb").write(b"".join(bux(bytes(f)) for f in filas) + cola)


def fila_tooltip(idioma):
    f = bytearray(TIP_TAM)
    struct.pack_into("<iH", f, 0, RUSH, CLASE_BM)
    for (off, largo), texto in zip(CAMPOS_TIP, TEXTOS[idioma]):
        b = texto.encode("utf-8")
        assert len(b) < largo, (idioma, texto)
        f[off:off + len(b)] = b
    return f


def main():
    modo, ruta = sys.argv[1], sys.argv[2]
    if modo in ("prueba-arbol", "ver-arbol", "rush-arbol"):
        filas = leer_arbol(ruta)
        if modo == "prueba-arbol":
            guardar_arbol(filas, ruta + ".tmp")
            import os
            igual = open(ruta + ".tmp", "rb").read() == open(ruta, "rb").read()
            os.remove(ruta + ".tmp")
            print("ida y vuelta identica:", igual)
            sys.exit(0 if igual else 1)
        usados = [i for i, f in enumerate(filas) if campos_arbol(f)["Index"] != 0]
        if modo == "ver-arbol":
            clase = int(sys.argv[3]) if len(sys.argv) > 3 else None
            for i in usados:
                c = campos_arbol(filas[i])
                if clase is None or c["Clase"] & clase:
                    print(i, c)
            print(len(usados), "usados")
            return
        salida = sys.argv[3] if len(sys.argv) > 3 else ruta
        for i in usados:                              # si ya esta, se actualiza en su sitio
            if campos_arbol(filas[i])["Skill"] == RUSH:
                filas[i] = fila_rush()
                break
        else:
            assert len(usados) == usados[-1] + 1, "los registros usados no son contiguos"
            ocupado = [campos_arbol(filas[i]) for i in usados if campos_arbol(filas[i])["Clase"] & CLASE_BM and campos_arbol(filas[i])["Index"] == INDICE_RUSH]
            assert not ocupado, f"el indice {INDICE_RUSH} ya lo usa otro nodo del Blade Master: {ocupado}"
            filas[len(usados)] = fila_rush()
        guardar_arbol(filas, salida)
        print("nodo de Rush:", campos_arbol(leer_arbol(salida)[[campos_arbol(f)["Skill"] for f in leer_arbol(salida)].index(RUSH)]))
    elif modo in ("prueba-tooltip", "rush-tooltip"):
        filas, cola = leer_tooltip(ruta)
        if modo == "prueba-tooltip":
            guardar_tooltip(filas, cola, ruta + ".tmp")
            import os
            igual = open(ruta + ".tmp", "rb").read() == open(ruta, "rb").read()
            os.remove(ruta + ".tmp")
            print("ida y vuelta identica:", igual)
            sys.exit(0 if igual else 1)
        idioma = sys.argv[3]
        salida = sys.argv[4] if len(sys.argv) > 4 else ruta
        numeros = [struct.unpack_from("<i", f, 0)[0] for f in filas]
        if RUSH in numeros:
            filas[numeros.index(RUSH)] = fila_tooltip(idioma)
        else:
            vacio = numeros.index(0)
            assert all(n == 0 for n in numeros[vacio:]), "los registros usados no son contiguos"
            filas[vacio] = fila_tooltip(idioma)
        guardar_tooltip(filas, cola, salida)
        print("tooltip de Rush:", idioma, "en el registro", numeros.index(RUSH) if RUSH in numeros else numeros.index(0))
    else:
        print(__doc__)
        sys.exit(2)


if __name__ == "__main__":
    main()
