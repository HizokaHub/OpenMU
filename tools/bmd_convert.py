"""Convierte BMD cifrados (version 0x0F = 15, y 0x0C = 12) a BMD plano 0x0A que MuMain lee sin cambios.

Uso:
  python tools/bmd_convert.py <entrada.bmd | carpeta> [-o salida] [--match texto,...]

Version 15: LEA-256 en modo ECB con la clave binaria de los clientes de Season 15/16 (la conocida "webzen#@!01..." no sirve);
la clave la publica el visor https://github.com/xulek/muonline-bmd-viewer. La salida descifrada tiene la misma longitud que
la entrada cifrada y el mismo formato que el BMD 0x0A (nombre[32], contadores, mallas, acciones, huesos).
Version 12: descifrado de mapas de MuMain (MapFileDecrypt). Version 10 se copia tal cual. La version 14 (0x0E) todavia no esta
resuelta: los archivos que vimos miden 34 bytes mas que el modelo y no descifran con esta clave.
"""
import argparse
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmd_lea  # noqa: E402

LEA_KEY_V15 = bytes([
    0xcc, 0x50, 0x45, 0x13, 0xc2, 0xa6, 0x57, 0x4e, 0xd6, 0x9a, 0x45, 0x89, 0xbf, 0x2f, 0xbc, 0xd9,
    0x39, 0xb3, 0xb3, 0xbd, 0x50, 0xbd, 0xcc, 0xb6, 0x85, 0x46, 0xd1, 0xd6, 0x16, 0x54, 0xe0, 0x87])
XOR_KEYS = [0xD1, 0x73, 0x52, 0xF6, 0xD2, 0x9A, 0xCB, 0x27, 0x3E, 0xAF, 0x59, 0x31, 0x37, 0xB3, 0xE7, 0xA2]


def map_file_decrypt(src):
    out = bytearray()
    key = 0x5E
    for i, b in enumerate(src):
        out.append(((b ^ XOR_KEYS[i % 16]) - key) & 255)
        key = (b + 0x3D) & 255
    return bytes(out)


def parse_plain(x):
    """Valida el formato plano; devuelve (nombre, [texturas]) o None."""
    try:
        nm, nb, na = struct.unpack_from('<3h', x, 32)
        if not (0 <= nm <= 200 and 0 <= nb <= 500 and 0 <= na <= 500):
            return None
        p = 38
        tex = []
        for _ in range(nm):
            nv, nn, nt, ntri, _t = struct.unpack_from('<5h', x, p)
            if min(nv, nn, nt, ntri) < 0:
                return None
            p += 10 + nv * 16 + nn * 20 + nt * 8 + ntri * 64
            tex.append(x[p:p + 32].split(b'\0')[0].decode('latin1'))
            p += 32
        keys = []
        for _ in range(na):
            k, lock = struct.unpack_from('<hB', x, p)
            if k < 0 or lock > 1:
                return None
            p += 3 + (k * 12 if lock else 0)
            keys.append(k)
        for _ in range(nb):
            dummy = x[p]
            p += 1
            if dummy > 1:
                return None
            if not dummy:
                p += 34 + sum(24 * k for k in keys)
        if p > len(x):
            return None
        return x[:32].split(b'\0')[0].decode('latin1'), tex
    except (struct.error, IndexError):
        return None


def to_plain(data):
    """Devuelve el contenido plano (sin cabecera) de un BMD, o None si no se sabe leer."""
    if data[:3] != b'BMD':
        return None
    version = data[3]
    if version == 10:
        return data[4:]
    if version in (12, 15):
        size = struct.unpack_from('<i', data, 4)[0]
        enc = data[8:8 + size]
        if version == 12:
            return map_file_decrypt(enc)
        n = len(enc) // 16 * 16
        return bmd_lea.decrypt_ecb_fast(enc, LEA_KEY_V15) + enc[n:]
    return None


def convert_file(path, outdir, match):
    data = open(path, 'rb').read()
    plain = to_plain(data)
    if plain is None:
        return 'skip', f'version {data[3] if data[:3] == b"BMD" else "?"}'
    info = parse_plain(plain)
    if info is None:
        return 'bad', 'no valida como BMD plano'
    name, tex = info
    label = (os.path.basename(path) + ' ' + name + ' ' + ' '.join(tex)).lower()
    if match and not any(m in label for m in match):
        return 'filtered', ''
    if outdir:
        os.makedirs(outdir, exist_ok=True)
        with open(os.path.join(outdir, os.path.basename(path)), 'wb') as f:
            f.write(b'BMD\x0a' + plain)
    return 'ok', f'{name} | {", ".join(tex[:4])}'


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('src')
    ap.add_argument('-o', '--out')
    ap.add_argument('--match', default='')
    ap.add_argument('--quiet', action='store_true')
    a = ap.parse_args()
    words = [w.strip().lower() for w in a.match.split(',') if w.strip()]
    files = [a.src] if os.path.isfile(a.src) else [os.path.join(dp, f) for dp, _, fn in os.walk(a.src) for f in fn if f.lower().endswith('.bmd')]
    stats = {}
    for path in files:
        status, note = convert_file(path, a.out, words)
        stats[status] = stats.get(status, 0) + 1
        if status in ('ok', 'bad') and not a.quiet:
            print(status, os.path.relpath(path, a.src) if os.path.isdir(a.src) else path, '|', note)
    print(stats)
