"""Software renderer for the MU character-select busts (Data/Logo/NewFace01..07.bmd). Read-only.

Decrypts a BMD (version 10 plain, version 12 map-file XOR), poses it at action 0 frame 0 and rasterises a
textured front view (no lighting, z-buffered, alpha-tested at 0.25 like the client). Used by
make_minimap_atlases.py to cut the champion portraits; it can also be run on its own:

usage: bmd_face.py <model.bmd> <texture dir> <out.png> [size]
"""
import io
import math
import os
import struct
import sys

import numpy as np
from PIL import Image

XOR_KEYS = [0xD1, 0x73, 0x52, 0xF6, 0xD2, 0x9A, 0xCB, 0x27, 0x3E, 0xAF, 0x59, 0x31, 0x37, 0xB3, 0xE7, 0xA2]
BACKGROUND = (0.08, 0.08, 0.1)


def map_file_decrypt(src):
    out = bytearray()
    key = 0x5E
    for i, b in enumerate(src):
        out.append(((b ^ XOR_KEYS[i % 16]) - key) & 255)
        key = (b + 0x3D) & 255
    return bytes(out)


def load_plain(path):
    data = open(path, 'rb').read()
    if data[:3] != b'BMD':
        raise ValueError('%s: not a BMD file' % path)
    if data[3] == 10:
        return data[4:]
    if data[3] != 12:
        raise ValueError('%s: BMD version %d is not supported here (use bmd_convert.py first)' % (path, data[3]))
    size = struct.unpack_from('<i', data, 4)[0]
    return map_file_decrypt(data[8:8 + size])


def parse(x):
    """Returns (meshes, bones, action key counts). Only what the renderer needs is kept."""
    nm, nb, na = struct.unpack_from('<3h', x, 32)
    p = 38
    meshes = []
    for _ in range(nm):
        nv, nn, nt, ntri, _texidx = struct.unpack_from('<5h', x, p)
        p += 10
        verts = []
        for _ in range(nv):
            node = struct.unpack_from('<h', x, p)[0]
            pos = struct.unpack_from('<3f', x, p + 4)
            verts.append((node, pos))
            p += 16
        p += nn * 20
        uvs = [struct.unpack_from('<2f', x, p + i * 8) for i in range(nt)]
        p += nt * 8
        tris = []
        for _ in range(ntri):
            poly = x[p]
            vi = struct.unpack_from('<4h', x, p + 2)
            ti = struct.unpack_from('<4h', x, p + 18)
            tris.append((poly, vi, ti))
            p += 64
        tex = x[p:p + 32].split(b'\0')[0].decode('latin1')
        p += 32
        meshes.append(dict(verts=verts, uvs=uvs, tris=tris, tex=tex))
    actions = []
    for _ in range(na):
        k, lock = struct.unpack_from('<hB', x, p)
        p += 3 + (k * 12 if lock else 0)
        actions.append(k)
    bones = []
    for _ in range(nb):
        dummy = x[p]
        p += 1
        if dummy:
            bones.append(None)
            continue
        name = x[p:p + 32].split(b'\0')[0].decode('latin1')
        parent = struct.unpack_from('<h', x, p + 32)[0]
        p += 34
        frames = []
        for k in actions:
            pos = [struct.unpack_from('<3f', x, p + i * 12) for i in range(k)]
            p += k * 12
            rot = [struct.unpack_from('<3f', x, p + i * 12) for i in range(k)]
            p += k * 12
            frames.append((pos, rot))
        bones.append(dict(name=name, parent=parent, frames=frames))
    return meshes, bones, actions


def angle_quat(a):
    sy, cy = math.sin(a[2] * .5), math.cos(a[2] * .5)
    sp, cp = math.sin(a[1] * .5), math.cos(a[1] * .5)
    sr, cr = math.sin(a[0] * .5), math.cos(a[0] * .5)
    return (sr * cp * cy - cr * sp * sy, cr * sp * cy + sr * cp * sy, cr * cp * sy - sr * sp * cy, cr * cp * cy + sr * sp * sy)


def quat_mat(q, t):
    x, y, z, w = q
    m = np.eye(4)
    m[0, 0] = 1 - 2 * y * y - 2 * z * z; m[1, 0] = 2 * x * y + 2 * w * z; m[2, 0] = 2 * x * z - 2 * w * y
    m[0, 1] = 2 * x * y - 2 * w * z; m[1, 1] = 1 - 2 * x * x - 2 * z * z; m[2, 1] = 2 * y * z + 2 * w * x
    m[0, 2] = 2 * x * z + 2 * w * y; m[1, 2] = 2 * y * z - 2 * w * x; m[2, 2] = 1 - 2 * x * x - 2 * y * y
    m[0:3, 3] = t
    return m


def bone_mats(bones, action=0, frame=0):
    mats = [np.eye(4) for _ in bones]
    for i, b in enumerate(bones):
        if b is None or not b['frames'] or not b['frames'][action][0]:
            continue
        pos, rot = b['frames'][action]
        local = quat_mat(angle_quat(rot[frame]), pos[frame])
        mats[i] = mats[b['parent']] @ local if b['parent'] >= 0 else local
    return mats


def load_tex(texdir, name):
    """MU texture (OZJ = 24-byte prefix + JPEG, OZT = 4-byte prefix + TGA) as float RGBA, or None."""
    base = os.path.splitext(name)[0]
    for ext, kind in (('.OZJ', 'j'), ('.ozj', 'j'), ('.OZT', 't'), ('.ozt', 't')):
        path = os.path.join(texdir, base + ext)
        if not os.path.exists(path):
            continue
        data = open(path, 'rb').read()
        if kind == 'j':
            start = 0
            while True:
                i = data.find(b'\xff\xd8', start)
                if i < 0:
                    return None
                try:
                    img = Image.open(io.BytesIO(data[i:]))
                    img.load()
                    break
                except Exception:
                    start = i + 1
        else:
            img = Image.open(io.BytesIO(data[4:]), formats=['TGA'])
        return np.asarray(img.convert('RGBA')).astype(np.float32) / 255.0
    return None


def posed_points(meshes, bones):
    mats = bone_mats(bones)
    world = []
    for m in meshes:
        pts = [(mats[node] if 0 <= node < len(mats) else np.eye(4)) @ np.array([pos[0], pos[1], pos[2], 1.0])
               for node, pos in m['verts']]
        world.append(np.array(pts)[:, :3] if pts else np.zeros((0, 3)))
    return world


def render_bust(path, texdir, size=512):
    """Front view (screen x = world x, screen y = world z up, camera at -y) fitted to a size x size square.

    Returns (rgb uint8 [size, size, 3], face box (x0, y0, x1, y1) in pixels): the box bounds the vertices of
    the meshes whose texture name contains 'face', so a caller can crop the head."""
    meshes, bones, _ = parse(load_plain(path))
    world = posed_points(meshes, bones)
    P = np.concatenate([p for p in world if len(p)])
    xmin, xmax = P[:, 0].min(), P[:, 0].max()
    zmin, zmax = P[:, 2].min(), P[:, 2].max()
    span = max(xmax - xmin, zmax - zmin) * 1.04
    cx, cz = (xmin + xmax) / 2, (zmin + zmax) / 2
    img = np.zeros((size, size, 3), np.float32)
    img[:] = BACKGROUND
    zbuf = np.full((size, size), np.inf, np.float32)
    for m, pts in zip(meshes, world):
        tex = load_tex(texdir, m['tex'])
        if tex is None or not len(pts):
            continue
        th, tw = tex.shape[:2]
        sx = (pts[:, 0] - cx) / span * size + size / 2
        sy = size / 2 - (pts[:, 2] - cz) / span * size
        dz = pts[:, 1]
        for poly, vi, ti in m['tris']:
            corners = [(0, 1, 2)] + ([(0, 2, 3)] if poly == 4 else [])
            for a, b, c in corners:
                ia, ib, ic = vi[a], vi[b], vi[c]
                ua, ub, uc = m['uvs'][ti[a]], m['uvs'][ti[b]], m['uvs'][ti[c]]
                x0, y0, x1, y1, x2, y2 = sx[ia], sy[ia], sx[ib], sy[ib], sx[ic], sy[ic]
                den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
                if abs(den) < 1e-9:
                    continue
                bx0, bx1 = int(max(0, math.floor(min(x0, x1, x2)))), int(min(size - 1, math.ceil(max(x0, x1, x2))))
                by0, by1 = int(max(0, math.floor(min(y0, y1, y2)))), int(min(size - 1, math.ceil(max(y0, y1, y2))))
                if bx0 > bx1 or by0 > by1:
                    continue
                gx, gy = np.meshgrid(np.arange(bx0, bx1 + 1) + 0.5, np.arange(by0, by1 + 1) + 0.5)
                l0 = ((y1 - y2) * (gx - x2) + (x2 - x1) * (gy - y2)) / den
                l1 = ((y2 - y0) * (gx - x2) + (x0 - x2) * (gy - y2)) / den
                l2 = 1 - l0 - l1
                inside = (l0 >= -1e-4) & (l1 >= -1e-4) & (l2 >= -1e-4)
                if not inside.any():
                    continue
                z = l0 * dz[ia] + l1 * dz[ib] + l2 * dz[ic]
                u = l0 * ua[0] + l1 * ub[0] + l2 * uc[0]
                v = l0 * ua[1] + l1 * ub[1] + l2 * uc[1]
                tx = (np.mod(u, 1.0) * tw).astype(int).clip(0, tw - 1)
                ty = (np.mod(v, 1.0) * th).astype(int).clip(0, th - 1)
                col = tex[ty, tx]
                sub = zbuf[by0:by1 + 1, bx0:bx1 + 1]
                ok = inside & (z < sub) & (col[..., 3] > 0.25)
                sub[ok] = z[ok]
                img[by0:by1 + 1, bx0:bx1 + 1][ok] = col[..., :3][ok]

    face = [pts for m, pts in zip(meshes, world) if 'face' in m['tex'].lower() and len(pts)]
    if not face:
        raise ValueError('%s: no mesh with a face texture' % path)
    F = np.concatenate(face)
    fx0 = (F[:, 0].min() - cx) / span * size + size / 2
    fx1 = (F[:, 0].max() - cx) / span * size + size / 2
    fy0 = size / 2 - (F[:, 2].max() - cz) / span * size
    fy1 = size / 2 - (F[:, 2].min() - cz) / span * size
    return (img.clip(0, 1) * 255).astype(np.uint8), (fx0, fy0, fx1, fy1)


if __name__ == '__main__':
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    rgb, box = render_bust(sys.argv[1], sys.argv[2], int(sys.argv[4]) if len(sys.argv) > 4 else 512)
    Image.fromarray(rgb).save(sys.argv[3])
    print(os.path.basename(sys.argv[1]), 'face box', [round(v, 1) for v in box])
