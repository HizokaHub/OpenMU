"""Oblique software renderer of Crywolf object models (Data/Object35/ObjectNN.bmd, type t = Object(t+1)) into RGBA sprites.
Shared by road_edge_prototypes.py; run alone it writes a contact sheet of the small candidates:
usage: obj_sprites.py <Data dir> <out.png> [type ...]"""
import math, os, sys
import numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmd_face as bf

PITCH = math.radians(52)       # camera elevation: 90 = straight down
_cache = {}

def load_model(data, t):
    key = (data, t)
    if key not in _cache:
        path = os.path.join(data, 'Object35', 'Object%02d.bmd' % (t + 1))
        meshes, bones, _ = bf.parse(bf.load_plain(path))
        _cache[key] = (meshes, bones, bf.posed_points(meshes, bones))
    return _cache[key]

def render_sprite(data, t, px_per_unit, yaw=0.0, scale=1.0):
    """Returns (RGBA uint8 [h, w, 4], anchor (ax, ay)) where the anchor is the pixel of the model origin on the ground."""
    meshes, bones, world = load_model(data, t)
    texdir = os.path.join(data, 'Object35')
    cy, sy_ = math.cos(yaw), math.sin(yaw)
    pts_all = []
    for pts in world:
        p = pts * scale
        x = p[:, 0] * cy - p[:, 1] * sy_
        y = p[:, 0] * sy_ + p[:, 1] * cy
        pts_all.append(np.stack([x, y, p[:, 2]], 1))
    P = np.concatenate([p for p in pts_all if len(p)])
    sxs = [p[:, 0] * px_per_unit for p in pts_all]
    sys_ = [-(p[:, 2] * math.cos(PITCH) + p[:, 1] * math.sin(PITCH)) * px_per_unit for p in pts_all]
    allx = np.concatenate([s for s in sxs if len(s)]); ally = np.concatenate([s for s in sys_ if len(s)])
    x0, x1, y0, y1 = math.floor(allx.min()) - 2, math.ceil(allx.max()) + 2, math.floor(ally.min()) - 2, math.ceil(ally.max()) + 2
    w, h = int(x1 - x0), int(y1 - y0)
    if w <= 0 or h <= 0 or w > 1500 or h > 1500:
        return None, (0, 0)
    img = np.zeros((h, w, 4), np.float32)
    zbuf = np.full((h, w), np.inf, np.float32)
    for m, pts, sx, sy in zip(meshes, pts_all, sxs, sys_):
        tex = bf.load_tex(texdir, m['tex'])
        if tex is None or not len(pts):
            continue
        th, tw = tex.shape[:2]
        sx = sx - x0; sy = sy - y0
        dz = pts[:, 1] * math.cos(PITCH) - pts[:, 2] * math.sin(PITCH)
        for poly, vi, ti in m['tris']:
            for a, b, c in [(0, 1, 2)] + ([(0, 2, 3)] if poly == 4 else []):
                ia, ib, ic = vi[a], vi[b], vi[c]
                ua, ub, uc = m['uvs'][ti[a]], m['uvs'][ti[b]], m['uvs'][ti[c]]
                X0, Y0, X1, Y1, X2, Y2 = sx[ia], sy[ia], sx[ib], sy[ib], sx[ic], sy[ic]
                den = (Y1 - Y2) * (X0 - X2) + (X2 - X1) * (Y0 - Y2)
                if abs(den) < 1e-9:
                    continue
                bx0, bx1 = int(max(0, math.floor(min(X0, X1, X2)))), int(min(w - 1, math.ceil(max(X0, X1, X2))))
                by0, by1 = int(max(0, math.floor(min(Y0, Y1, Y2)))), int(min(h - 1, math.ceil(max(Y0, Y1, Y2))))
                if bx0 > bx1 or by0 > by1:
                    continue
                gx, gy = np.meshgrid(np.arange(bx0, bx1 + 1) + 0.5, np.arange(by0, by1 + 1) + 0.5)
                l0 = ((Y1 - Y2) * (gx - X2) + (X2 - X1) * (gy - Y2)) / den
                l1 = ((Y2 - Y0) * (gx - X2) + (X0 - X2) * (gy - Y2)) / den
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
                img[by0:by1 + 1, bx0:bx1 + 1][ok] = np.concatenate([col[..., :3], np.ones_like(col[..., :1])], -1)[ok]
    return (img.clip(0, 1) * 255).astype(np.uint8), (-x0, -y0)

if __name__ == '__main__':
    data, out = sys.argv[1], sys.argv[2]
    types = [int(a) for a in sys.argv[3:]] or list(range(0, 34))
    cell = 150
    cols = 8
    sheet = Image.new('RGB', (cols * cell, ((len(types) + cols - 1) // cols) * (cell + 16)), (60, 52, 44))
    d = ImageDraw.Draw(sheet)
    for k, t in enumerate(types):
        try:
            spr, (ax, ay) = render_sprite(data, t, 0.2)
        except Exception as e:
            print(t, 'fail', e); continue
        x, y = (k % cols) * cell, (k // cols) * (cell + 16)
        d.text((x + 3, y + 1), 'type %d' % t, fill=(255, 255, 255))
        if spr is None:
            continue
        im = Image.fromarray(spr, 'RGBA')
        if max(im.size) > cell - 6:
            f = (cell - 6) / max(im.size); im = im.resize((max(1, int(im.width * f)), max(1, int(im.height * f))))
        sheet.paste(im, (x + 3, y + 16), im)
    sheet.save(out); print(out)
