"""Lights, low ruin walls and relics along the MOBA lanes and at the jungle entrances. Visual only.

Replaces the solid lane-guide walls: every lane gets its own light style (so players name them), the wide
stretches get broken low walls with relics in the gaps, and every jungle entrance gets two torches.
Writes Data/World200/MobaDecor.txt for the client (same format as MobaCity.txt, yawoffset 0, long axis = local X).

usage: make_decor.py <mask.att> <layout.json> <doors.txt> <MobaCity.txt> <out.txt> <preview.png> [seed] [with-ruins]

By default only the lights and the entrance torches are written; the ruin walls and relics (rejected by the user)
return only with the "with-ruins" argument.
"""
import json
import sys
import numpy as np
from scipy import ndimage as ndi
from PIL import Image, ImageDraw

mask_path, layout_path, doors_path, city_path, out_txt, preview = sys.argv[1:7]
seed = int(sys.argv[7]) if len(sys.argv) > 7 else 5
rng = np.random.default_rng(seed)
WITH_RUINS = len(sys.argv) > 8 and sys.argv[8] == 'with-ruins'

m = np.frombuffer(open(mask_path, 'rb').read()[3:], dtype=np.uint8).reshape(256, 256)
blocked = m == 4
dwall = ndi.distance_transform_edt(~blocked)
layout = json.load(open(layout_path))
doors = [tuple(map(int, l.split())) for l in open(doors_path) if l.strip() and not l.startswith('#')]

taken = []                                    # (x, y, radius)
for l in open(city_path):
    p = l.split()
    if len(p) == 5 and p[0] != 'yawoffset':
        taken.append((float(p[1]), float(p[2]), 3.0 if p[0].startswith('House') else 1.5))
for o in layout['objects']:
    taken.append((o['x'], o['y'], 7.0 if o['type'] in ('nexus', 'shop') else 5.0))
for x, y, r in [(86, 93, 3), (158, 105, 3), (150, 162, 3), (78, 150, 3)]:    # totems
    taken.append((x, y, r))
base_pts = [(118, 20), (118, 235)]

entries = []        # (name, x, y, yaw, scale, kind)


def free(x, y, r, margin=1.5):
    xi, yi = int(round(x)), int(round(y))
    if not (3 <= xi < 253 and 3 <= yi < 253) or blocked[yi, xi] or dwall[yi, xi] < margin:
        return False
    for bx, by in base_pts:
        if np.hypot(x - bx, y - by) < 26:        # the city owns the base surroundings
            return False
    for tx, ty, tr in taken:
        if np.hypot(x - tx, y - ty) < r + tr:
            return False
    return True


def put(name, x, y, yaw, scale, r, kind):
    if free(x, y, r):
        entries.append((name, x, y, yaw, scale, kind))
        taken.append((x, y, r))
        return True
    return False


def resample(w, step=1.0):
    pts = []
    for (x0, y0), (x1, y1) in zip(w[:-1], w[1:]):
        n = max(1, int(np.hypot(x1 - x0, y1 - y0) / step))
        for i in range(n):
            pts.append((x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n))
    pts.append(tuple(w[-1]))
    return np.array(pts)


OFF = 11.0
STYLE = {                                   # per lane: light, relic
    'top': ('StreetLight01', 1.0, 'StoneStatue03', 1.4),
    'mid': ('FireLight01', 1.2, 'SteelStatue01', 1.3),
    'bot': ('DungeonTorch', 1.3, 'Cannon01', 1.4),
}
WALLS = [('StoneWall05', 4.0), ('StoneMuWall03', 3.0), ('StoneWall05', 4.0), ('StoneMuWall04', 3.3)]

for name, w in layout['lanes'].items():
    light, lscale, relic, rscale = STYLE[name]
    pts = resample(w)
    seg = np.hypot(*np.diff(pts, axis=0).T)
    arc = np.concatenate([[0], np.cumsum(seg)])
    tang = np.gradient(pts, axis=0)
    tang /= np.maximum(np.hypot(tang[:, 0], tang[:, 1])[:, None], 1e-9)
    normal = np.stack([-tang[:, 1], tang[:, 0]], axis=1)

    # lights in pairs every 16 cells
    s = 30.0
    while s < arc[-1] - 30:
        i = int(np.searchsorted(arc, s))
        for side in (-1, 1):
            x, y = pts[i] + normal[i] * OFF * side
            put(light, x, y, rng.uniform(0, 360), lscale, 0.8, 'light')
        s += 16.0

    # broken low walls + relics where the lane runs through wide open ground
    s = 34.0
    side = 1
    k = 0
    while s < arc[-1] - 34:
        i = int(np.searchsorted(arc, s))
        cx, cy = pts[i]
        wide = dwall[int(round(cy)), int(round(cx))] >= 10.0
        if wide and WITH_RUINS:
            wname, wlen = WALLS[k % len(WALLS)]
            yaw = np.degrees(np.arctan2(tang[i, 1], tang[i, 0]))
            for sd in (-1, 1):          # both sides of the lane; the far side is staggered by half a piece
                shift = 0 if sd == side else int(wlen / 2) + 2
                j = min(i + shift, len(pts) - 1)
                x, y = pts[j] + normal[j] * (OFF + 2.5) * sd
                ok = put(wname, x, y, yaw, 1.0, wlen / 2 + 0.5, 'wall')
                if ok and k % 2 == 1:
                    gx, gy = pts[min(j + int(wlen) + 3, len(pts) - 1)] + normal[j] * (OFF + 2.5) * sd
                    put(relic, gx, gy, rng.uniform(0, 360), rscale, 1.5, 'relic')
            k += 1
            side = -side
            s += wlen + float(rng.uniform(4, 7))
        else:
            s += 6.0   # (no ruins: just keep walking)

# torches at both sides of every jungle entrance
for dx, dy in doors:
    cands = []
    for a in np.radians(np.arange(0, 360, 15)):
        x, y = dx + 4.0 * np.cos(a), dy + 4.0 * np.sin(a)
        xi, yi = int(round(x)), int(round(y))
        if 0 <= xi < 256 and 0 <= yi < 256 and not blocked[yi, xi] and 1.6 <= dwall[yi, xi] <= 3.5:
            cands.append((dwall[yi, xi], x, y))
    cands.sort()
    chosen = []
    for _, x, y in cands:
        if all(np.hypot(x - a, y - b) >= 6 for a, b in chosen):
            chosen.append((x, y))
        if len(chosen) == 2:
            break
    for j, (x, y) in enumerate(chosen):
        put('Brazier' if j == 0 else 'FireLight02', x, y, rng.uniform(0, 360), 1.0, 1.2, 'door')

with open(out_txt, 'w', newline=chr(10)) as f:
    f.write('# MOBA lane lights, ruin walls and relics (visual only). generated by tools/make_decor.py' + chr(10))
    f.write('yawoffset 0' + chr(10))
    for n, x, y, yaw, sc, _ in entries:
        f.write('%s %.1f %.1f %.0f %.2f' % (n, x, y, yaw % 360, sc) + chr(10))
from collections import Counter
print('objetos:', len(entries), dict(Counter(e[5] for e in entries)))
print(dict(Counter(e[0] for e in entries)))

S = 3
im = Image.new('RGB', (256 * S, 256 * S)); px = im.load()
for y in range(256):
    for x in range(256):
        col = (40, 40, 40) if blocked[y, x] else (120, 200, 120) if m[y, x] == 1 else (230, 220, 190)
        for a in range(S):
            for b in range(S):
                px[x * S + a, (255 - y) * S + b] = col
d = ImageDraw.Draw(im)
colr = {'light': (255, 200, 0), 'wall': (110, 110, 120), 'relic': (180, 60, 200), 'door': (255, 110, 0)}
for n, x, y, yaw, sc, kind in entries:
    r = 3 if kind in ('wall', 'relic') else 2
    d.ellipse((x * S - r, (255 - y) * S - r, x * S + r, (255 - y) * S + r), fill=colr[kind])
    if kind == 'wall':
        a = np.radians(yaw)
        d.line((x * S - np.cos(a) * 6, (255 - y) * S + np.sin(a) * 6, x * S + np.cos(a) * 6, (255 - y) * S - np.sin(a) * 6), fill=(80, 80, 90), width=3)
for w in layout['lanes'].values():
    d.line([(x * S, (255 - y) * S) for x, y in w], fill=(70, 100, 220), width=1)
im.save(preview)
