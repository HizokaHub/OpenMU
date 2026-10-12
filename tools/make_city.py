"""Medieval (Lorencia style) decoration around the MOBA nexuses. Visual only.

Reads the walk mask and the layout, measures the Lorencia models (BMD bounding boxes), places houses and
props in a ring around the blue nexus keeping lanes, towers, the shop and walls clear, then turns the whole
thing 180 degrees for the red base. Writes the text file the client reads (Data/World200/MobaCity.txt):

    yawoffset <degrees>               added to every yaw (fix the facing of the models here)
    <ModelName> <x> <y> <yaw> <scale> tile position (floats), yaw in degrees

usage: make_city.py <mask.att> <layout.json> <Object1 dir> <out.txt> <preview.png> [seed] [yawoffset]
"""
import json
import struct
import sys
import numpy as np
from scipy import ndimage as ndi
from PIL import Image, ImageDraw

mask_path, layout_path, obj_dir, out_txt, preview = sys.argv[1:6]
seed = int(sys.argv[6]) if len(sys.argv) > 6 else 11
yaw_offset = float(sys.argv[7]) if len(sys.argv) > 7 else 90.0
rng = np.random.default_rng(seed)

m = np.frombuffer(open(mask_path, 'rb').read()[3:], dtype=np.uint8).reshape(256, 256)
blocked = m == 4
dwall = ndi.distance_transform_edt(~blocked)
layout = json.load(open(layout_path))
NX, NY = 118.0, 20.0        # blue nexus


def bbox(name):
    d = open(f'{obj_dir}/{name}.bmd', 'rb').read()
    x = d[4:]
    nm, nb, na = struct.unpack_from('<3h', x, 32)
    p = 38
    lo = np.array([1e9] * 3); hi = np.array([-1e9] * 3)
    for _ in range(nm):
        nv, nn, nt, ntri, _t = struct.unpack_from('<5h', x, p)
        base = p + 10
        for i in range(nv):
            px, py, pz = struct.unpack_from('<3f', x, base + i * 16 + 4)
            v = np.array([px, py, pz]); lo = np.minimum(lo, v); hi = np.maximum(hi, v)
        p += 10 + nv * 16 + nn * 20 + nt * 8 + ntri * 64 + 32
    return lo, hi


names = ['House01', 'House02', 'House03', 'House04', 'House05', 'HouseEtc01', 'Tent01', 'Well01', 'Carriage01', 'Carriage02',
         'Beer01', 'Beer02', 'Sign01', 'StoneStatue01', 'StoneStatue02', 'Bonfire01', 'FireLight01', 'StreetLight01', 'Waterspout01',
         'Fence01', 'StoneWall01']
size = {}
for n in names:
    lo, hi = bbox(n)
    size[n] = (hi - lo)
    print(f'{n:14s} ancho x={hi[0]-lo[0]:6.0f} y={hi[1]-lo[1]:6.0f} alto z={hi[2]-lo[2]:6.0f}  (unidades, 100 = 1 celda)')


def foot(n, scale=1.0):
    s = size[n]
    return max(s[0], s[1]) / 100.0 * scale          # footprint side in cells


# ------------------------------------------------------------------ keep-out data
def interp(w):
    pts = []
    for (x0, y0), (x1, y1) in zip(w[:-1], w[1:]):
        n = max(1, int(np.hypot(x1 - x0, y1 - y0)))
        for i in range(n):
            pts.append((x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n))
    return pts


lane_pts = np.array([p for w in layout['lanes'].values() for p in interp(w)])
towers = [(o['x'], o['y']) for o in layout['objects'] if o['type'] == 'tower' and o['y'] < 128]
shop = [(o['x'], o['y']) for o in layout['objects'] if o['type'] == 'shop' and o['y'] < 128]
placed = []        # (x, y, half)
entries = []       # (name, x, y, yaw, scale)


def lane_dist(x, y):
    return float(np.min(np.hypot(lane_pts[:, 0] - x, lane_pts[:, 1] - y)))


def ok(x, y, half, lane_gap=3.5):
    xi, yi = int(round(x)), int(round(y))
    if not (3 <= xi < 253 and 3 <= yi < 125) or blocked[yi, xi]:
        return False
    if dwall[yi, xi] < half + 1.5:
        return False
    if lane_dist(x, y) < half + lane_gap:
        return False
    if np.hypot(x - NX, y - NY) < half + 7:
        return False
    for tx, ty in towers + shop:
        if np.hypot(x - tx, y - ty) < half + 6:
            return False
    for px, py, ph in placed:
        if np.hypot(x - px, y - py) < half + ph + 1.5:
            return False
    return True


def add(name, x, y, scale=1.0, face=True, yaw=None, lane_gap=3.5):
    half = foot(name, scale) / 2
    if not ok(x, y, half, lane_gap):
        return False
    a = np.degrees(np.arctan2(NY - y, NX - x))
    entries.append((name, x, y, (a if yaw is None else yaw) if face else (yaw or 0.0), scale))
    placed.append((x, y, half))
    return True


# ------------------------------------------------------------------ plaza around the nexus
for k in range(4):                       # bonfires on the plaza ring
    a = np.radians(45 + 90 * k)
    add('Bonfire01', NX + 9 * np.cos(a), NY + 9 * np.sin(a), 1.0, face=False, lane_gap=0)
for k in range(8):                       # street lights
    a = np.radians(22.5 + 45 * k)
    add('StreetLight01', NX + 14 * np.cos(a), NY + 14 * np.sin(a), 1.0, face=False, lane_gap=0)
add('Waterspout01', 104.0, 23.0, 1.0, face=False, lane_gap=0)
add('StoneStatue01', 132.0, 24.0, 1.0, face=True, lane_gap=0)

# ------------------------------------------------------------------ houses on a ring
house_types = ['House01', 'House03', 'House04', 'House01', 'House04']
tries = 0
count = 0
add('House05', 118.0, 8.0, 0.7, face=True, lane_gap=0)       # town hall behind the nexus, if it fits
while count < 14 and tries < 6000:
    tries += 1
    r = rng.uniform(13, 44)
    a = np.radians(rng.uniform(-30, 210))
    x, y = NX + r * np.cos(a), NY + r * np.sin(a)
    n = house_types[count % 5]
    if add(n, x, y, 0.9, face=True, lane_gap=3.5):
        count += 1
print('casas colocadas:', count)

# ------------------------------------------------------------------ props next to houses and the shop
for n in ['Carriage01', 'Carriage02', 'Beer01', 'Beer02', 'Beer01', 'Sign01', 'Well01', 'Tent01', 'Tent01', 'FireLight01', 'FireLight01', 'StoneStatue02']:
    for _ in range(300):
        a = np.radians(rng.uniform(-30, 210)); r = rng.uniform(13, 44)
        if add(n, NX + r * np.cos(a), NY + r * np.sin(a), 1.0, face=True, lane_gap=3.0):
            break

# ------------------------------------------------------------------ red base = blue base turned 180 degrees
all_entries = list(entries)
for n, x, y, yaw, sc in entries:
    all_entries.append((n, 236 - x, 255 - y, yaw + 180.0, sc))

with open(out_txt, 'w', newline=chr(10)) as f:
    f.write('# MOBA medieval city (visual only). generated by tools/make_city.py' + chr(10))
    f.write('yawoffset %g' % yaw_offset + chr(10))
    for n, x, y, yaw, sc in all_entries:
        f.write('%s %.1f %.1f %.0f %.2f' % (n, x, y, yaw % 360, sc) + chr(10))
print('objetos escritos:', len(all_entries))

S = 3
im = Image.new('RGB', (256 * S, 256 * S)); px = im.load()
for y in range(256):
    for x in range(256):
        col = (40, 40, 40) if blocked[y, x] else (120, 200, 120) if m[y, x] == 1 else (230, 220, 190)
        for a in range(S):
            for b in range(S):
                px[x * S + a, (255 - y) * S + b] = col
d = ImageDraw.Draw(im)
colors = {'House': (170, 80, 50), 'Bonfire': (255, 140, 0), 'FireLight': (255, 140, 0), 'StreetLight': (255, 220, 40), 'Water': (60, 140, 235)}
for n, x, y, yaw, sc in all_entries:
    c = (110, 110, 110)
    for k, v in colors.items():
        if n.startswith(k):
            c = v
    r = max(2, foot(n, sc) / 2 * S)
    d.rectangle((x * S - r, (255 - y) * S - r, x * S + r, (255 - y) * S + r), outline=c, fill=c if r < 6 else None, width=2)
    ang = np.radians(yaw)
    d.line((x * S, (255 - y) * S, x * S + np.cos(ang) * r * 1.4, (255 - y) * S - np.sin(ang) * r * 1.4), fill=(0, 0, 0), width=1)
for w in layout['lanes'].values():
    d.line([(x * S, (255 - y) * S) for x, y in w], fill=(70, 100, 220), width=1)
for o in layout['objects']:
    if o['type'] == 'nexus':
        d.ellipse((o['x'] * S - 6, (255 - o['y']) * S - 6, o['x'] * S + 6, (255 - o['y']) * S + 6), fill=(40, 80, 255) if o['team'] == 'blue' else (230, 40, 40))
im.save(preview)
