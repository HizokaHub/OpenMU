"""Lights, low ruin walls and relics along the MOBA lanes and at the jungle entrances. Visual only.

Replaces the solid lane-guide walls: every lane gets its own light style (so players name them), the wide
stretches get broken low walls with relics in the gaps, and every jungle entrance gets two torches.
Writes Data/World200/MobaDecor.txt for the client (same format as MobaCity.txt, yawoffset 0, long axis = local X).

usage: make_decor.py <mask.att> <layout.json> <doors.txt> <MobaCity.txt> <out.txt> <preview.png> [seed] [with-ruins|no-ruins] [base_mask.att]

base_mask.att = the mask before the jungle islands were opened (rotation only): its enclosed pockets are the jungle camps.

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
BASE_MASK = sys.argv[9] if len(sys.argv) > 9 else None

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

entries = []        # (name, x, y, yaw, scale, kind, z, sub)


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


def put(name, x, y, yaw, scale, r, kind, z=0, sub=0, margin=1.5):
    if free(x, y, r, margin):
        entries.append((name, x, y, yaw, scale, kind, z, sub))
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

    # team flags between the lights: a pole with a tinted cloth and a small coloured light (blue half = blue flags)
    s = 38.0
    while s < arc[-1] - 30:
        i = int(np.searchsorted(arc, s))
        for side in (-1, 1):
            x, y = pts[i] + normal[i] * OFF * side
            if free(x, y, 0.9):
                yaw = float(np.degrees(np.arctan2(tang[i, 1], tang[i, 0])))
                sub = 1010 if y < 127.5 else 1011
                entries.append(('Fence01', x, y, yaw, 1.8, 'flag', 0, 0))
                entries.append(('FlagCloth', x, y, yaw, 1.6, 'flag', 152, sub))
                taken.append((x, y, 0.9))
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

# ---------------------------------------------------------------- jungle camps inside the islands (decor only)
camp_centres = []
if BASE_MASK:
    bm = np.frombuffer(open(BASE_MASK, 'rb').read()[3:], dtype=np.uint8).reshape(256, 256)
    bblocked = bm == 4
    lab_b, nb = ndi.label(~bblocked)
    main_b = 1 + int(np.argmax(ndi.sum(~bblocked, lab_b, range(1, nb + 1))))
    lab_e, _ = ndi.label(bblocked)
    border_l = set(lab_e[0, :]) | set(lab_e[-1, :]) | set(lab_e[:, 0]) | set(lab_e[:, -1])
    border_l.discard(0)
    exterior_b = np.isin(lab_e, list(border_l))
    bdw = ndi.distance_transform_edt(~bblocked)
    for i in range(1, nb + 1):
        if i == main_b:
            continue
        pocket = lab_b == i
        area = int(pocket.sum())
        ys, xs = np.where(pocket)
        if area < 110 or area > 1200:
            continue
        if min(np.hypot(xs.mean() - bx, ys.mean() - by) for bx, by in base_pts) < 45:
            continue
        if (ndi.binary_dilation(pocket, iterations=3) & exterior_b).sum() > 40:
            continue
        cells = [(int(x), int(y)) for x, y in zip(xs, ys) if bdw[y, x] >= 2.5]
        if len(cells) < 6:
            continue
        camp_centres.append((float(xs.mean()), float(ys.mean())))
        order = rng.permutation(len(cells))
        fire = max(cells, key=lambda c: bdw[c[1], c[0]])
        put('Bonfire01', fire[0] + 0.5, fire[1] + 0.5, 0, 1.0, 1.0, 'camp')
        wanted = int(np.clip(area / 110, 3, 8))
        kinds = ['Tent01', 'Carriage01', 'Beer01', 'Straw01', 'Tent01', 'Beer02', 'Carriage02', 'Straw01']
        placed_here = 0
        for j in order:
            if placed_here >= wanted:
                break
            cx, cy = cells[j]
            if np.hypot(cx - fire[0], cy - fire[1]) < 3.0:
                continue
            nme = kinds[placed_here % len(kinds)]
            yaw = float(np.degrees(np.arctan2(fire[1] - cy, fire[0] - cx)))
            if put(nme, cx + 0.5, cy + 0.5, yaw, 1.0, 1.6 if nme.startswith(('Tent', 'Carr')) else 0.8, 'camp', margin=1.0):
                placed_here += 1

# ---------------------------------------------------------------- bridges with torches over eight jungle entrances
for dx, dy in doors[::3][:8]:
    if camp_centres:
        cx, cy = min(camp_centres, key=lambda c: np.hypot(c[0] - dx, c[1] - dy))
        yaw = float(np.degrees(np.arctan2(cy - dy, cx - dx))) + 90.0
    else:
        yaw = 0.0
    put('Bridge01', dx + 0.5, dy + 0.5, yaw, 0.45, 1.2, 'bridge', margin=0.8)

# ---------------------------------------------------------------- soft vegetation in the open ground, away from the lanes
lane_all = np.zeros((256, 256), bool)
for w in layout['lanes'].values():
    for x, y in resample(w):
        lane_all[int(round(y)), int(round(x))] = True
lane_d = ndi.distance_transform_edt(~lane_all)
seeds = []
cands = np.argwhere((~blocked) & (dwall >= 6) & (lane_d >= 14))
rng.shuffle(cands)
for cy, cx in cands:
    if len(seeds) >= 20:
        break
    if all(np.hypot(cx - sx_, cy - sy_) >= 22 for sx_, sy_ in seeds) and free(cx, cy, 2.5, 4.0):
        seeds.append((cx, cy))
plants = [('Tree12', 1.8), ('Tree09', 0.9), ('Tree10', 0.8), ('Tree05', 0.6), ('Grass03', 0.7), ('Grass03', 0.7)]
for cx, cy in seeds:
    n = 0
    for _ in range(60):
        if n >= 5:
            break
        a = rng.uniform(0, 2 * np.pi)
        r = rng.uniform(0, 3.4)
        nme, rad = plants[int(rng.integers(0, len(plants)))]
        if put(nme, cx + r * np.cos(a), cy + r * np.sin(a), rng.uniform(0, 360), float(rng.uniform(0.85, 1.15)), rad, 'plant', margin=1.5):
            n += 1

# ---------------------------------------------------------------- emitters: mist around the fountains / totems, fireflies in the groves
for fx, fy in [(52, 128), (184, 127), (86, 93), (158, 105), (150, 162), (78, 150)]:
    entries.append(('MistEmitter', fx + 0.5, fy + 0.5, 0, 0.02, 'emitter', 0, 0))
for cx, cy in seeds[:10]:
    entries.append(('FireflyEmitter', cx + 0.5, cy + 0.5, 0, 0.02, 'emitter', 0, 0))

with open(out_txt, 'w', newline=chr(10)) as f:
    f.write('# MOBA lane lights, ruin walls and relics (visual only). generated by tools/make_decor.py' + chr(10))
    f.write('yawoffset 0' + chr(10))
    for n, x, y, yaw, sc, _, z, sub in entries:
        if z or sub:
            f.write('%s %.1f %.1f %.0f %.2f %d %d' % (n, x, y, yaw % 360, sc, z, sub) + chr(10))
        else:
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
colr = {'light': (255, 200, 0), 'wall': (110, 110, 120), 'relic': (180, 60, 200), 'door': (255, 110, 0), 'flag': (60, 120, 255), 'camp': (150, 90, 40), 'bridge': (0, 160, 160), 'plant': (60, 160, 60), 'emitter': (200, 200, 255)}
for n, x, y, yaw, sc, kind, _z, _s in entries:
    r = 3 if kind in ('wall', 'relic') else 2
    d.ellipse((x * S - r, (255 - y) * S - r, x * S + r, (255 - y) * S + r), fill=colr[kind])
    if kind == 'wall':
        a = np.radians(yaw)
        d.line((x * S - np.cos(a) * 6, (255 - y) * S + np.sin(a) * 6, x * S + np.cos(a) * 6, (255 - y) * S - np.sin(a) * 6), fill=(80, 80, 90), width=3)
for w in layout['lanes'].values():
    d.line([(x * S, (255 - y) * S) for x, y in w], fill=(70, 100, 220), width=1)
im.save(preview)
