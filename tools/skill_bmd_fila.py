"""Edita skill_eng.bmd del cliente (650 filas de 108 bytes, BuxConvert, checksum 0x5A18 al final).
Uso:
  python -I -X utf8 tools/skill_bmd_fila.py ver   <archivo.bmd> <numero>     muestra una fila
  python -I -X utf8 tools/skill_bmd_fila.py prueba <archivo.bmd>             comprueba que descifrar y recifrar da los mismos bytes
  python -I -X utf8 tools/skill_bmd_fila.py rush  <archivo.bmd> [salida.bmd] agrega/actualiza la fila 638 (Rush) a partir de la de Crescent Moon Slash (44), con el icono 338 (el de Rush de S21, hoja new_Master_Icon: ver rush_icono.py) y rango 6 (fila del arbol maestro)
"""
import struct
import sys

FILAS, TAM, CLAVE = 650, 108, 0x5A18
BUX = (0xFC, 0xCF, 0xAB)
# (nombre, formato struct, desplazamiento) segun SkillFieldDefs.h con la alineacion natural de MSVC (el tamano total 108 lo confirma).
CAMPOS = [("Level", "<H", 50), ("Damage", "<H", 52), ("Mana", "<H", 54), ("AbilityGuage", "<H", 56), ("Distance", "<I", 60),
          ("Delay", "<i", 64), ("Energy", "<i", 68), ("Charisma", "<H", 72), ("MasteryType", "<B", 74), ("SkillUseType", "<B", 75),
          ("SkillBrand", "<I", 76), ("KillCount", "<B", 80), ("SkillRank", "<B", 91), ("Magic_Icon", "<H", 92), ("TypeSkill", "<B", 94),
          ("Strength", "<i", 96), ("Dexterity", "<i", 100), ("ItemSkill", "<B", 104), ("IsDamage", "<B", 105), ("Effect", "<H", 106)]


def bux(datos):
    return bytes(b ^ BUX[i % 3] for i, b in enumerate(datos))


def checksum(buf, clave=CLAVE):
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


def leer(ruta):
    d = open(ruta, "rb").read()
    assert len(d) == FILAS * TAM + 4, f"tamano inesperado {len(d)}"
    cifrado, cs = d[:-4], struct.unpack("<I", d[-4:])[0]
    assert checksum(cifrado) == cs, "checksum no coincide"
    return [bytearray(bux(cifrado[i * TAM:(i + 1) * TAM])) for i in range(FILAS)]


def guardar(filas, ruta):
    cifrado = b"".join(bux(bytes(f)) for f in filas)
    open(ruta, "wb").write(cifrado + struct.pack("<I", checksum(cifrado)))


def nombre(f):
    return bytes(f[:50]).split(b"\0")[0].decode("latin-1")


def mostrar(f):
    return {"Name": nombre(f), **{n: struct.unpack_from(fm, f, o)[0] for n, fm, o in CAMPOS},
            "RequireDutyClass": list(f[81:84]), "RequireClass": list(f[84:91])}


def poner(f, **kw):
    for n, fm, o in CAMPOS:
        if n in kw:
            struct.pack_into(fm, f, o, kw[n])


if __name__ == "__main__":
    modo, ruta = sys.argv[1], sys.argv[2]
    filas = leer(ruta)
    if modo == "ver":
        print(mostrar(filas[int(sys.argv[3])]))
    elif modo == "prueba":
        orig = open(ruta, "rb").read()
        guardar(filas, ruta + ".tmp")
        igual = open(ruta + ".tmp", "rb").read() == orig
        import os
        os.remove(ruta + ".tmp")
        print("ida y vuelta identica:", igual)
        sys.exit(0 if igual else 1)
    elif modo == "rush":
        salida = sys.argv[3] if len(sys.argv) > 3 else ruta
        fila = bytearray(filas[44])                 # Crescent Moon Slash: un ataque de espada normal (uso 0, no de objeto)
        fila[:50] = b"Rush".ljust(50, b"\0")
        poner(fila, Level=0, Damage=70, Mana=0, AbilityGuage=0, Distance=7, Delay=0, Magic_Icon=338, SkillRank=6, SkillBrand=0, Energy=0, Charisma=0, Strength=0, Dexterity=0)
        fila[81:84] = bytes(3)
        clases = bytearray(7)
        clases[1] = filas[20][85]                   # solo la familia Knight (DKBK)
        fila[84:91] = clases
        filas[638] = fila
        guardar(filas, salida)
        print("fila 638:", mostrar(leer(salida)[638]))
