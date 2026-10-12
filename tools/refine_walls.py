"""Closes the oversized open fields of the MOBA walk mask (blue half), then mirrors it.

Rules (all on the blue half y <= 127; the red half is the blue half turned 180 degrees about
(118, 127.5): (x, y) -> (236 - x, 255 - y), so each lane is the same for both teams):
  C  lane edges   - where a lane runs through open ground wider than the standard width, a wall band
                    is traced at LANE_HALF cells from the lane polyline, in segments with gaps
                    (entrances) of different lengths, so the lane reads as a 18-22 wide corridor.
  B  jungle rings - leftover open pockets away from the lanes get an oval ring wall with two doors.
  A  pillars      - smaller leftovers get one to three rock pillars.
Nothing is ever removed, nothing is placed on or near structures / lane waypoints / base plazas.

usage: refine_walls.py <in.att> <layout.json> <out.att> <preview.png> [seed]
"""
import json
import sys
import numpy as np
from scipy import ndimage as ndi
from PIL import Image, ImageDraw

LANE_HALF = 10.5          # standard lane = 21 wide
BAND = 2.2                # wall thickness of the lane edge
BASE_KEEP = 30            # cells around each nexus that stay open (base plaza)
STRUCT_KEEP = 5           # cells around every structure / lane waypoint

src, layout_path, dst, preview = sys.argv[1:5]
seed = int(sys.argv[5]) if len(sys.argv) > 5 else 7
rng = np.random.default_rng(seed)

raw = open(src, 'rb').read()
hdr = raw[:3]
m = np.frombuffer(raw[3:], dtype=np.uint8).reshape(256, 256).copy()   # [y, x]
before = m.copy()
layout = json.load(open(layout_path))
YY, XX = np.mgrid[0:256, 0:256]

blocked = m == 4
wl, k = ndi.label(~blocked)
sizes = ndi.sum(~blocked, wl, range(1, k + 1))
main = wl == 1 + int(np.argmax(sizes))
dwall = ndi.distance_transform_edt(~blocked)

# --- lane polylines -> distance field and arc-length parameter
def interp(wps):
    pts = []
    for (x0, y0), (x1, y1) in zip(wps[:-1], wps[1:]):
        n = max(1, int(np.hypot(x1 - x0, y1 - y0)))
        for i in range(n):
            pts.append((x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n))
    pts.append(tuple(wps[-1]))
    return pts

lane_dist = np.full((256, 256), 1e9)
lane_s = np.zeros((256, 256))
lane_id = np.zeros((256, 256), int)
for li, (name, wps) in enumerate(layout['lanes'].items()):
    pts = interp(wps)
    raster = np.zeros((256, 256), bool)
    S = np.zeros((256, 256))
    for si, (x, y) in enumerate(pts):
        xi, yi = int(round(x)), int(round(y))
        if 0 <= xi < 256 and 0 <= yi < 256:
            raster[yi, xi] = True
            S[yi, xi] = si
    d, idx = ndi.distance_transform_edt(~raster, return_indices=True)
    s = S[idx[0], idx[1]]
    better = d < lane_dist
    lane_dist = np.where(better, d, lane_dist)
    lane_s = np.where(better, s, lane_s)
    lane_id = np.where(better, li, lane_id)

# --- keep-out zones
keep = np.zeros((256, 256), bool)
for o in layout['objects']:
    r = BASE_KEEP if o['type'] in ('nexus', 'shop') else STRUCT_KEEP
    keep |= (XX - o['x']) ** 2 + (YY - o['y']) ** 2 <= r * r
for wps in layout['lanes'].values():
    for x, y in wps:
        keep |= (XX - x) ** 2 + (YY - y) ** 2 <= STRUCT_KEEP ** 2
keep |= (m == 1)
for yy, xx in zip(*np.where(m == 1)):
    keep[max(0, yy - 3):yy + 4, max(0, xx - 3):xx + 4] = True
keep[128:, :] = True                     # only the blue half is designed; the red half is mirrored
free = main & ~keep & ~blocked

new = m.copy()
placed_band = placed_ring = placed_pillar = 0

# --- C: lane edge bands with gaps
band = free & (lane_dist >= LANE_HALF) & (lane_dist < LANE_HALF + BAND)
# only where the ground beyond is really open (otherwise a wall is already confining the lane)
band &= dwall >= 4
gap_state = {}
for li in range(len(layout['lanes'])):
    smax = int(lane_s[lane_id == li].max()) + 1
    pos, segs = 0, []
    while pos < smax:
        seg = int(rng.integers(14, 27))
        gap = int(rng.integers(5, 9))
        segs.append((pos, pos + seg))
        pos += seg + gap
    gap_state[li] = segs
for yy, xx in zip(*np.where(band)):
    li = lane_id[yy, xx]
    s = lane_s[yy, xx]
    if any(a <= s < b for a, b in gap_state[li]):
        new[yy, xx] = 4
        placed_band += 1

# --- B / A: leftover open pockets away from the lanes
dnew = ndi.distance_transform_edt(new != 4)
pocket = (new != 4) & free & (lane_dist > LANE_HALF + BAND + 1) & (dnew >= 6)
pl, pn = ndi.label(pocket)
for i in range(1, pn + 1):
    comp = pl == i
    area = int(comp.sum())
    if area < 20:
        continue
    cy, cx = ndi.center_of_mass(comp)
    rmax = float(dnew[comp].max())
    if area >= 260 and rmax >= 9:
        # B: oval ring with two doors (toward the nearest lane and the opposite side)
        r = min(rmax * 0.9, 14.0)
        rx, ry = r * rng.uniform(0.95, 1.25), r * rng.uniform(0.8, 1.05)
        ang_lane = np.arctan2(*(np.array(np.unravel_index(np.argmin(np.where(lane_dist < 1.5, np.hypot(XX - cx, YY - cy), 1e9)), (256, 256))) - np.array([cy, cx])))
        ring = np.zeros((256, 256), bool)
        t = np.sqrt(((XX - cx) / rx) ** 2 + ((YY - cy) / ry) ** 2)
        thick = 2.0 / min(rx, ry)
        ring = (t <= 1.0) & (t > 1.0 - thick)
        theta = np.arctan2(YY - cy, XX - cx)
        for door in (ang_lane, ang_lane + np.pi * rng.uniform(0.8, 1.2)):
            dd = np.abs(((theta - door + np.pi) % (2 * np.pi)) - np.pi) * np.sqrt(rx * ry)
            ring &= ~(dd <= 3.2)
        cells = ring & free & (new != 4)
        # skip rings that would touch existing walls (they would read as scribbles)
        if (dwall[ring] < 3).mean() > 0.08:
            continue
        new[cells] = 4
        placed_ring += int(cells.sum())
        # a pillar or two just outside the ring (A)
        for _ in range(int(rng.integers(1, 3))):
            a = rng.uniform(0, 2 * np.pi)
            px, py = cx + np.cos(a) * rx * 1.7, cy + np.sin(a) * ry * 1.7
            disc = ((XX - px) ** 2 + (YY - py) ** 2 <= rng.uniform(2.0, 3.0) ** 2) & free & pocket & (new != 4)
            new[disc] = 4
            placed_pillar += int(disc.sum())
    elif area >= 60:
        # A: one to three pillars
        for _ in range(int(rng.integers(1, 4))):
            ys, xs = np.where(comp)
            j = int(rng.integers(0, len(ys)))
            disc = ((XX - xs[j]) ** 2 + (YY - ys[j]) ** 2 <= rng.uniform(1.8, 3.0) ** 2) & free & comp
            new[disc] = 4
            placed_pillar += int(disc.sum())

def rotate_red(a):
    out = a.copy()
    for y in range(128, 256):
        for x in range(256):
            xs = 236 - x
            out[y, x] = a[255 - y, xs] if 0 <= xs < 256 else 4
    return out

final = rotate_red(new)

# --- checks: no key point blocked, nothing newly cut off except rings' interiors with doors
bad = []
pts = [(o['x'], o['y'], o['type']) for o in layout['objects']] + [(x, y, 'lane') for w in layout['lanes'].values() for x, y in w]
for x, y, n_ in pts:
    if final[y, x] == 4:
        bad.append((n_, x, y))
wl2, k2 = ndi.label(final != 4)
sizes2 = ndi.sum(final != 4, wl2, range(1, k2 + 1))
main2 = wl2 == 1 + int(np.argmax(sizes2))
lost = int((main & ~main2).sum())
lost_cells = (main & ~main2)
print(f'paredes nuevas: bordes de carril {placed_band}, anillos {placed_ring}, pilares {placed_pillar}')
print('puntos clave bloqueados:', bad)
print('celdas caminables que quedaron aisladas:', lost)

open(dst, 'wb').write(hdr + final.tobytes())

# --- width report after
dist = ndi.distance_transform_edt(final != 4)
for name, wps in layout['lanes'].items():
    ws = np.array([2 * dist[int(round(y)), int(round(x))] for x, y in interp(wps)])
    print(f'{name:4s} ancho min={ws.min():5.1f} p10={np.percentile(ws, 10):5.1f} mediana={np.median(ws):5.1f} p90={np.percentile(ws, 90):5.1f} max={ws.max():5.1f}')
f = (dist >= 15) & (final != 4)
l3, c3 = ndi.label(f)
print('campos con ancho >= 30 restantes:', sum(1 for i in range(1, c3 + 1) if (l3 == i).sum() >= 20), ' area', int(f.sum()))

def render(a, ref=None):
    im = Image.new('RGB', (256, 256)); px = im.load()
    for y in range(256):
        for x in range(256):
            v = a[y, x]
            if v == 4:
                c = (40, 40, 40)
                if ref is not None and ref[y, x] != 4:
                    c = (200, 60, 60)          # new wall
            elif v == 1:
                c = (120, 200, 120)
            else:
                c = (230, 220, 190)
            px[x, 255 - y] = c
    return im

W = Image.new('RGB', (256 * 2 + 10, 256), (255, 255, 255))
W.paste(render(before), (0, 0)); W.paste(render(final, before), (266, 0))
W.resize((W.width * 3, W.height * 3), Image.NEAREST).save(preview)
