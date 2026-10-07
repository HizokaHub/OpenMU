"""Extrae modelos BMD ya descifrados de la memoria de un proceso (solo lectura, no inyecta ni modifica nada).

Un cliente que carga un modelo cifrado lo descifra en memoria; esta herramienta busca en esa memoria estructuras con el
formato BMD "plano" (el que lee MuMain, version 0x0A), las valida recorriendolas completas y las guarda como `BMD\\x0A`.

Uso:
  python tools/bmd_memdump.py <PID | nombre.exe> -o salida [--loop 20] [--match darkness,gaion]
  python tools/bmd_memdump.py --selftest

Solo aparecen los modelos que el cliente haya cargado (los de los mapas/monstruos que hayas visto) y solo mientras el
buffer descifrado siga en memoria, asi que conviene dejarlo en --loop mientras se juega.
Uso solo para tus propios archivos y en tu maquina; los modelos siguen siendo propiedad de sus autores.
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import hashlib
import os
import re
import struct
import sys
import time

MAX_MESHES, MAX_BONES, MAX_ACTIONS = 100, 200, 400
SMD = re.compile(rb'\.[sS][mM][dD]\x00')


def parse_plain(buf, start):
    """Recorre un BMD plano que empieza en `start`. Devuelve (longitud, nombre, texturas) o None si no es valido."""
    n = len(buf)
    if start + 38 > n:
        return None
    name_raw = buf[start:start + 32]
    end = name_raw.find(b'\0')
    if end < 5:
        return None
    name = name_raw[:end]
    if not name.lower().endswith(b'.smd') or any(c < 0x20 or c > 0x7e for c in name):
        return None
    meshes, bones, actions = struct.unpack_from('<3h', buf, start + 32)
    if not (0 <= meshes <= MAX_MESHES and 1 <= bones <= MAX_BONES and 0 <= actions <= MAX_ACTIONS) or (meshes == 0 and actions == 0):
        return None
    p = start + 38
    textures = []
    try:
        for _ in range(meshes):
            nv, nn, nt, ntri, tex = struct.unpack_from('<5h', buf, p)
            if min(nv, nn, nt, ntri) < 0 or nv > 20000 or nn > 20000 or nt > 20000 or ntri > 30000:
                return None
            p += 10 + nv * 16 + nn * 20 + nt * 8 + ntri * 64
            tn = buf[p:p + 32]
            tend = tn.find(b'\0')
            if tend < 0 or any(c < 0x20 or c > 0x7e for c in tn[:tend]):
                return None
            textures.append(tn[:tend].decode('ascii'))
            p += 32
        keys = []
        for _ in range(actions):
            k, lock = struct.unpack_from('<hB', buf, p)
            if k < 0 or k > 2000 or lock > 1:
                return None
            p += 3 + (k * 12 if lock else 0)
            keys.append(k)
        for _ in range(bones):
            dummy = buf[p]
            p += 1
            if dummy > 1:
                return None
            if not dummy:
                parent = struct.unpack_from('<h', buf, p + 32)[0]
                if parent < -1 or parent >= bones:
                    return None
                p += 34 + sum(24 * k for k in keys)
        if p > n:
            return None
    except (struct.error, IndexError):
        return None
    return p - start, name.decode('ascii'), textures


def find_models(chunk, base_label=''):
    found = {}
    for m in SMD.finditer(chunk):
        for start in range(max(0, m.end() - 1 - 32), m.start() + 1):
            res = parse_plain(chunk, start)
            if res and start + res[0] <= len(chunk):
                length, name, tex = res
                data = chunk[start:start + length]
                found[hashlib.sha1(data).hexdigest()] = (name, tex, data)
                break
    return found


def save(found, outdir, seen, match):
    os.makedirs(outdir, exist_ok=True)
    new = 0
    for digest, (name, tex, data) in found.items():
        if digest in seen:
            continue
        label = ' '.join([name] + tex).lower()
        if match and not any(w in label for w in match):
            continue
        seen.add(digest)
        base = re.sub(r'[^A-Za-z0-9_.-]', '_', os.path.splitext(name)[0])
        path = os.path.join(outdir, f'{base}_{digest[:8]}.bmd')
        with open(path, 'wb') as f:
            f.write(b'BMD\x0a' + data)
        with open(os.path.join(outdir, 'manifest.txt'), 'a', encoding='utf-8') as f:
            f.write(f'{os.path.basename(path)}\t{name}\t{len(data)}\t{",".join(tex)}\n')
        print(f'  + {os.path.basename(path)}  ({len(data)} bytes)  texturas: {", ".join(tex)}')
        new += 1
    return new


# ---- acceso a memoria (Windows) ----
PROCESS_QUERY_INFORMATION, PROCESS_VM_READ = 0x0400, 0x0010
MEM_COMMIT, PAGE_NOACCESS, PAGE_GUARD = 0x1000, 0x01, 0x100


class MBI(ctypes.Structure):
    _fields_ = [('BaseAddress', ctypes.c_void_p), ('AllocationBase', ctypes.c_void_p), ('AllocationProtect', wt.DWORD),
                ('RegionSize', ctypes.c_size_t), ('State', wt.DWORD), ('Protect', wt.DWORD), ('Type', wt.DWORD)]


def pid_of(name):
    import subprocess
    out = subprocess.run(['tasklist', '/fi', f'imagename eq {name}', '/fo', 'csv', '/nh'], capture_output=True, text=True).stdout
    pids = [int(l.split('","')[1]) for l in out.splitlines() if l.startswith('"') and name.lower() in l.lower()]
    return pids[0] if pids else None


def scan_process(pid, seen, outdir, match):
    k32 = ctypes.WinDLL('kernel32', use_last_error=True)
    k32.OpenProcess.restype = wt.HANDLE
    k32.VirtualQueryEx.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.POINTER(MBI), ctypes.c_size_t]
    k32.ReadProcessMemory.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
    h = k32.OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, False, pid)
    if not h:
        raise OSError(f'No se pudo abrir el proceso {pid} (error {ctypes.get_last_error()}). Prueba ejecutando como administrador.')
    addr, mbi, total, new = 0, MBI(), 0, 0
    try:
        while addr < (1 << 47) and k32.VirtualQueryEx(h, ctypes.c_void_p(addr), ctypes.byref(mbi), ctypes.sizeof(mbi)):
            size = mbi.RegionSize
            if mbi.State == MEM_COMMIT and not (mbi.Protect & (PAGE_NOACCESS | PAGE_GUARD)) and size < (1 << 30):
                buf = ctypes.create_string_buffer(size)
                got = ctypes.c_size_t()
                if k32.ReadProcessMemory(h, ctypes.c_void_p(mbi.BaseAddress), buf, size, ctypes.byref(got)) or got.value:
                    data = buf.raw[:got.value]
                    total += len(data)
                    new += save(find_models(data), outdir, seen, match)
            addr = (mbi.BaseAddress or 0) + size
    finally:
        k32.CloseHandle(h)
    return total, new


def selftest():
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'selftest.bmd')
    src = r'C:\Users\aruiz\OneDrive\Desktop\RadicaL\Nexus MU Online\Data\Monster\Monster01.bmd'
    plain = open(src, 'rb').read()
    assert plain[3] == 10
    keep = b'junk' * 1000 + plain[4:] + b'tail' * 100  # copia "descifrada" viviendo en la memoria de este proceso
    out = os.path.join(os.environ.get('TEMP', '.'), 'bmd_memdump_selftest')
    seen = set()
    total, new = scan_process(os.getpid(), seen, out, [])
    ok = any(open(os.path.join(out, f), 'rb').read() == plain for f in os.listdir(out) if f.endswith('.bmd'))
    print(f'bytes leidos: {total}, modelos nuevos: {new}, coincide byte a byte con Monster01.bmd: {ok}')
    del keep
    return ok


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('target', nargs='?', help='PID o nombre del exe (p. ej. main.exe)')
    ap.add_argument('-o', '--out', default='bmd_dump')
    ap.add_argument('--loop', type=int, default=0, help='repetir el escaneo cada N segundos hasta Ctrl+C')
    ap.add_argument('--match', default='', help='guardar solo los que contengan alguna de estas palabras (en nombre o texturas)')
    ap.add_argument('--selftest', action='store_true')
    a = ap.parse_args()
    if a.selftest:
        sys.exit(0 if selftest() else 1)
    pid = int(a.target) if a.target and a.target.isdigit() else pid_of(a.target or 'main.exe')
    if not pid:
        sys.exit('Proceso no encontrado.')
    words = [w.strip().lower() for w in a.match.split(',') if w.strip()]
    seen = set()
    while True:
        total, new = scan_process(pid, seen, a.out, words)
        print(f'[{time.strftime("%H:%M:%S")}] leidos {total / 1e6:.0f} MB, modelos nuevos: {new}, total: {len(seen)}')
        if not a.loop:
            break
        time.sleep(a.loop)
