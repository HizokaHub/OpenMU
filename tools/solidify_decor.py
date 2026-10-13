"""Makes the decoration of the MOBA arena solid: blocks, in the walk mask, the cells under the fountains, the totems,
the city and the decoration objects (lamp posts, torches, tents, houses, trees...). Bridges, grass, flag cloths
and emitters stay walkable. Never touches key points (nexuses, shops, towers, lane waypoints) or the jungle doors.

usage: solidify_decor.py <in.att> <layout.json> <doors.txt> <MobaCity.txt> <MobaDecor.txt> <out.att> <preview.png> [decor_cells.bin]

decor_cells.bin = 256*256 bytes, 1 on the cells blocked by decoration: the client does not grow trees / rocks on them.

Run it on the mask WITHOUT solid decoration (the one produced by design_walls.py doors-only), never on its own output
(it would be harmless but the result could not be told apart from the walls).
"""
import json
import sys
import numpy as np
from scipy import ndimage as ndi
from PIL import Image, ImageDraw

mask_path, layout_path, doors_path, city_path, decor_path, out_path, preview = sys.argv[1:8]
cells_path = sys.argv[8] if len(sys.argv) > 8 else None

raw = open(mask_path, 'rb').read()
hdr = raw[:3]
m = np.frombuffer(raw[3:], dtype=np.uint8).reshape(256, 256).copy()   # [y, x]
before = m.copy()
layout = json.load(open(layout_path))
doors = [tuple(map(int, l.split())) for l in open(doors_path) if l.strip() and not l.startswith('#')]

# half side (cells, at scale 1; multiplied by the object scale) of the blocked square. 0 = walkable decoration.
HALF = {
    'House01': 2.4, 'House02': 0.8, 'House03': 3.65, 'House04': 2.0, 'House05': 4.1, 'HouseEtc01': 0.6,
    'Tent01': 1.6, 'Well01': 1.2, 'Carriage01': 1.45, 'Carriage02': 1.8, 'StoneStatue01': 0.6, 'StoneStatue02': 1.1,
    'Waterspout01': 3.0, 'Tree12': 1.4, 'Brazier': 1.0,
    'Bonfire01': 0.5, 'Beer01': 0.5, 'Beer02': 0.5, 'StreetLight01': 0.5, 'FireLight01': 0.5, 'FireLight02': 0.5,
    'DungeonTorch': 0.5, 'Fence01': 0.5, 'Straw01': 0.5, 'Tree05': 0.5, 'Tree09': 0.5, 'Tree10': 0.5,
    'Grass03': 0, 'FlagCloth': 0, 'Bridge01': 0, 'MistEmitter': 0, 'FireflyEmitter': 0,
}

protected = np.zeros((256, 256), bool)
for o in layout['objects']:
    protected[max(0, o['y'] - 2):o['y'] + 3, max(0, o['x'] - 2):o['x'] + 3] = True
for w in layout['lanes'].values():
    for x, y in w:
        protected[max(0, y - 1):y + 2, max(0, x - 1):x + 2] = True
for dx, dy in doors:
    protected[max(0, dy - 2):dy + 3, max(0, dx - 2):dx + 3] = True

blocks = []        # (x, y, half)
for path in (city_path, decor_path):
    for line in open(path):
        p = line.split()
        if len(p) < 5 or p[0] in ('yawoffset',) or line.startswith('#'):
            continue
        name, x, y, scale = p[0], float(p[1]), float(p[2]), float(p[4])
        half = HALF.get(name)
        if half is None or half == 0:
            continue
        blocks.append((x, y, half * (scale if half > 0.5 else 1.0)))
for fx, fy in [(52, 128), (184, 127)]:
    blocks.append((fx, fy, 2.4))          # fountains (Waterspout01 x 0.6)
for tx, ty in [(86, 93), (158, 105), (150, 162), (78, 150)]:
    blocks.append((tx, ty, 1.2))          # totems

placed = 0
for x, y, half in blocks:
    if half <= 0.5:
        cells = [(int(round(x)), int(round(y)))]
    else:
        cells = [(cx, cy)
                 for cy in range(int(np.floor(y - half)), int(np.ceil(y + half)) + 1)
                 for cx in range(int(np.floor(x - half)), int(np.ceil(x + half)) + 1)
                 if abs(cx - x) <= half and abs(cy - y) <= half]
    for cx, cy in cells:
        if 2 <= cx < 254 and 2 <= cy < 254 and m[cy, cx] != 4 and m[cy, cx] != 1 and not protected[cy, cx]:
            m[cy, cx] = 4
            placed += 1

# --- checks
lab_b, nb = ndi.label(before != 4)
main_b = lab_b == lab_b[20, 118]
lab_a, na = ndi.label(m != 4)
main_a = lab_a == lab_a[20, 118]
lost = main_b & ~main_a & (m != 4)      # walkable cells that were reachable and no longer are (not the blocked ones)
print('objetos con huella:', len(blocks), ' celdas bloqueadas nuevas:', placed)
print('celdas caminables que quedaron aisladas:', int(lost.sum()))
bad = []
for o in layout['objects']:
    if m[o['y'], o['x']] == 4 or lab_a[o['y'], o['x']] != lab_a[20, 118]:
        bad.append((o['type'], o['x'], o['y']))
for w in layout['lanes'].values():
    for x, y in w:
        if m[y, x] == 4 or lab_a[y, x] != lab_a[20, 118]:
            bad.append(('lane', x, y))
print('puntos clave bloqueados o aislados:', bad)
cut_doors = [d for d in doors if lab_a[d[1], d[0]] != lab_a[20, 118]]
print('puertas de jungla aisladas:', cut_doors)
open(out_path, 'wb').write(hdr + m.tobytes())
if cells_path:
    open(cells_path, 'wb').write(((m == 4) & (before != 4)).astype(np.uint8).tobytes())

S = 3
im = Image.new('RGB', (256 * S, 256 * S)); px = im.load()
for y in range(256):
    for x in range(256):
        if m[y, x] == 4 and before[y, x] != 4:
            col = (210, 70, 60)
        elif m[y, x] == 4:
            col = (40, 40, 40)
        elif lost[y, x]:
            col = (120, 120, 220)
        else:
            col = (230, 220, 190)
        for a in range(S):
            for b in range(S):
                px[x * S + a, (255 - y) * S + b] = col
im.save(preview)
