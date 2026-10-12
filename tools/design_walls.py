"""Hand-designed wall proposal for the MOBA arena (blue half), mirrored as a 180 degree turn.

Primitives: lane_wall (wall alongside a lane, with entrances), ring (oval jungle pit with doors),
pillars. Walls are only ever added on open ground, never near structures / waypoints / bases.

usage: design_walls.py <in.att> <layout.json> <out.att> <preview.png>
"""
import json
import sys
import numpy as np
from scipy import ndimage as ndi
from PIL import Image, ImageDraw

src, layout_path, dst, preview = sys.argv[1:5]
raw = open(src, 'rb').read()
hdr = raw[:3]
base = np.frombuffer(raw[3:], dtype=np.uint8).reshape(256, 256).copy()
layout = json.load(open(layout_path))
YY, XX = np.mgrid[0:256, 0:256]

keep = np.zeros((256, 256), bool)
for o in layout['objects']:
    r = 30 if o['type'] in ('nexus', 'shop') else 6
    keep |= (XX - o['x']) ** 2 + (YY - o['y']) ** 2 <= r * r
keep[base == 1] = True
keep = ndi.binary_dilation(keep, iterations=3) | keep
keep[128:, :] = True

new = base.copy()
added = np.zeros((256, 256), bool)
opened = np.zeros((256, 256), bool)


def put(mask):
    global new
    cells = mask & (new != 4) & ~keep
    new[cells] = 4
    added[cells] = True


def seg_mask(p0, p1, thick):
    """Cells within thick/2 of the segment p0-p1."""
    (x0, y0), (x1, y1) = p0, p1
    dx, dy = x1 - x0, y1 - y0
    L2 = dx * dx + dy * dy
    t = np.clip(((XX - x0) * dx + (YY - y0) * dy) / max(L2, 1e-9), 0, 1)
    px, py = x0 + t * dx, y0 + t * dy
    return (XX - px) ** 2 + (YY - py) ** 2 <= (thick / 2) ** 2


def lane_wall(a, b, thick=2.2, gaps=()):
    """Wall from a to b; gaps = [(fraction, width)] entrances along it."""
    a, b = np.array(a, float), np.array(b, float)
    length = np.hypot(*(b - a))
    u = (b - a) / length
    cuts = [(f * length - w / 2, f * length + w / 2) for f, w in gaps]
    pieces, pos = [], 0.0
    for c0, c1 in sorted(cuts) + [(length, length)]:
        if c0 > pos:
            pieces.append((pos, c0))
        pos = max(pos, c1)
    for s0, s1 in pieces:
        put(seg_mask(a + u * s0, a + u * s1, thick))


def ring(cx, cy, rx, ry, doors_deg=(), door_w=6.5, thick=2.2, rot=0.0):
    c, s = np.cos(np.radians(rot)), np.sin(np.radians(rot))
    X, Y = XX - cx, YY - cy
    u, v = X * c + Y * s, -X * s + Y * c
    t = np.sqrt((u / rx) ** 2 + (v / ry) ** 2)
    ringm = (t <= 1.0) & (t > 1.0 - thick / min(rx, ry))
    theta = np.degrees(np.arctan2(YY - cy, XX - cx))
    for dd in doors_deg:
        diff = np.abs((theta - dd + 180) % 360 - 180)
        arc = np.radians(diff) * np.sqrt(rx * ry)
        ringm &= ~(arc <= door_w / 2)
    put(ringm)


def pillar(cx, cy, r=2.4):
    put((XX - cx) ** 2 + (YY - cy) ** 2 <= r * r)


# ---------------------------------------------------------------- zone 1: bot corner (blue half), x~183 y~57
# the lane runs SW->NE through it; u = along the lane, n = to the map interior (NW)
u = np.array([0.696, 0.718]); n = np.array([-0.718, 0.696])
C = np.array([183.0, 57.0])
HALF = 10.8
# interior-side lane wall with an entrance toward the pit, outer-side lane wall with two small gaps
lane_wall(C + n * HALF - u * 21, C + n * HALF + u * 21, gaps=[(0.5, 7.0)])
lane_wall(C - n * HALF - u * 21, C - n * HALF + u * 21, gaps=[(0.28, 6.0), (0.74, 6.0)])
# outer pocket (decoration + flank): a short wall splitting the dead corner
lane_wall(C - n * 19 - u * 14, C - n * 19 + u * 14, gaps=[(0.5, 6.0)])

# ---------------------------------------------------------------- zone 6: mid in front of the base, x~117 y~68
# standard 21-wide lane: walls at x = 117 +- 10.8 in pieces with 6-wide gaps (flank entrances)
cx = 118.0
lane_wall((cx - 10.8, 56), (cx - 10.8, 82), gaps=[(0.42, 6.0), (0.85, 5.0)])
lane_wall((cx + 10.8, 56), (cx + 10.8, 82), gaps=[(0.30, 6.0), (0.72, 6.0)])
for dx, dy in [(-17, 66), (17, 72)]:
    pillar(cx + dx, dy)

# ---------------------------------------------------------------- top lane, west corner of the blue base (68,43)
lane_wall((52, 36), (66, 56), gaps=[(0.5, 6.0)])
pillar(70, 46); pillar(60, 30)

# ---------------------------------------------------------------- west edge on the axis (24,127): lane stays, small plaza walls
lane_wall((13, 112), (13, 126), gaps=[(0.5, 6.0)])

# ---------------------------------------------------------------- jungle: open the existing closed islands
# Every enclosed pocket (island) of the blue half becomes a jungle camp: two doors, one toward the nearest lane and
# one on the far side, so it connects to the main roads and can also be entered from the jungle.
blocked0 = base == 4
lab0, n0 = ndi.label(~blocked0)
sizes0 = ndi.sum(~blocked0, lab0, range(1, n0 + 1))
main_label = 1 + int(np.argmax(sizes0))
main0 = lab0 == main_label
lane_cells = np.zeros((256, 256), bool)
for w in layout['lanes'].values():
    for (x0, y0), (x1, y1) in zip(w[:-1], w[1:]):
        lane_cells |= seg_mask((x0, y0), (x1, y1), 1.5)
lane_d, lane_idx = ndi.distance_transform_edt(~lane_cells, return_indices=True)
camps = []
for i in range(1, n0 + 1):
    if i == main_label:
        continue
    pocket = lab0 == i
    area = int(pocket.sum())
    ys, xs = np.where(pocket)
    if area < 110 or area > 1200 or ys.max() > 126:
        continue
    if np.hypot(xs.mean() - 118, ys.mean() - 20) < 45:
        continue                                   # base surroundings
    lab_e, _ = ndi.label(blocked0)
    border_lbl = set(lab_e[0, :]) | set(lab_e[-1, :]) | set(lab_e[:, 0]) | set(lab_e[:, -1])
    border_lbl.discard(0)
    exterior0 = np.isin(lab_e, list(border_lbl))
    if (ndi.binary_dilation(pocket, iterations=3) & exterior0).sum() > 40:
        continue                                   # pocket outside the arena ring
    cy, cx = ys.mean(), xs.mean()
    ring_w = ndi.binary_dilation(pocket, iterations=3) & blocked0
    near_main = ndi.binary_dilation(main0, iterations=3)
    sites = np.argwhere(ring_w & near_main & (YY <= 127))
    if len(sites) == 0:
        continue
    # door 1: wall site closest to the nearest lane cell
    ly, lx = lane_idx[0][int(cy), int(cx)], lane_idx[1][int(cy), int(cx)]
    d1 = np.hypot(sites[:, 1] - lx, sites[:, 0] - ly)
    s1 = sites[int(np.argmin(d1))]
    # door 2: site far from door 1 (>= 16 cells), closest to the island side facing the map centre
    far = sites[np.hypot(sites[:, 1] - s1[1], sites[:, 0] - s1[0]) >= 16]
    doors = [s1]
    if len(far):
        d2 = np.hypot(far[:, 1] - 118, far[:, 0] - 127)
        doors.append(far[int(np.argmin(d2))])
    for (dy_, dx_) in doors:
        cut = ((XX - dx_) ** 2 + (YY - dy_) ** 2 <= 3.3 ** 2) & (new == 4) & (YY <= 127) & ~(base == 1)
        new[cut] = 0
        opened[cut] = True
    camps.append((int(cx), int(cy), area, len(doors)))
print('jungle camps opened (x, y, area, doors):', camps)

# ---------------------------------------------------------------- turn the blue half 180 degrees into the red half
final = new.copy()
for y in range(128, 256):
    for x in range(256):
        xs = 236 - x
        final[y, x] = new[255 - y, xs] if 0 <= xs < 256 else 4
added_final = added.copy()
opened_final = opened.copy()
for y in range(128, 256):
    for x in range(256):
        xs = 236 - x
        added_final[y, x] = added[255 - y, xs] if 0 <= xs < 256 else False
        opened_final[y, x] = opened[255 - y, xs] if 0 <= xs < 256 else False

# ---------------------------------------------------------------- checks
lab, k = ndi.label(final != 4)
nex = lab[20, 118]
bad = []
pts = [(o['type'] + ' ' + o['team'], o['x'], o['y']) for o in layout['objects']] + \
      [('lane', x, y) for w in layout['lanes'].values() for x, y in w]
for nme, x, y in pts:
    if final[y, x] == 4 or lab[y, x] != nex:
        bad.append((nme, x, y))
print('walls added (cells):', int(added_final.sum()))
print('key points blocked or cut off:', bad)
before_main = ndi.label(base != 4)[0] == ndi.label(base != 4)[0][20, 118]
lost = int((before_main & (lab != nex)).sum())
print('cells that were reachable and no longer are:', lost)
open(dst, 'wb').write(hdr + final.tobytes())

dist = ndi.distance_transform_edt(final != 4)
big = ((dist >= 15) & (final != 4))
l3, c3 = ndi.label(big)
print('open fields with width >= 30 left:', sum(1 for i in range(1, c3 + 1) if (l3 == i).sum() >= 20), 'area', int(big.sum()))

S = 3
def render(a, mark=None, doors=None):
    im = Image.new('RGB', (256 * S, 256 * S)); px = im.load()
    for y in range(256):
        for x in range(256):
            v = a[y, x]
            col = (40, 40, 40) if v == 4 else (120, 200, 120) if v == 1 else (230, 220, 190)
            if mark is not None and mark[y, x]:
                col = (210, 70, 60)
            if doors is not None and doors[y, x]:
                col = (60, 140, 235)
            for dy in range(S):
                for dx in range(S):
                    px[x * S + dx, (255 - y) * S + dy] = col
    d = ImageDraw.Draw(im)
    for w in layout['lanes'].values():
        d.line([(x * S, (255 - y) * S) for x, y in w], fill=(70, 100, 220), width=1)
    for o in layout['objects']:
        if o['type'] == 'nexus':
            c_ = (40, 80, 255) if o['team'] == 'blue' else (230, 40, 40)
            d.ellipse((o['x'] * S - 6, (255 - o['y']) * S - 6, o['x'] * S + 6, (255 - o['y']) * S + 6), fill=c_)
    return im

W = Image.new('RGB', (256 * S * 2 + 20, 256 * S), (255, 255, 255))
W.paste(render(base), (0, 0))
W.paste(render(final, added_final, opened_final), (256 * S + 20, 0))
ImageDraw.Draw(W).text((10, 4), 'ANTES', fill=(255, 255, 255))
ImageDraw.Draw(W).text((256 * S + 30, 4), 'PROPUESTA (rojo = muros nuevos, azul = puertas de jungla)', fill=(255, 255, 255))
W.save(preview)
