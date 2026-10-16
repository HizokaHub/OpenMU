"""Prototype of the MOBA minimap art (map 200): flat road ground + clean light lane tracks drawn from the
real lanes (PlugIns/Moba/MobaLayout.json) instead of the Crywolf roads of mini_map.OZT.

usage: make_minimap_art.py <mu-main/src/bin/Data> <out_dir> [variant]
  variant A = tracks along the editor lanes, B = tracks along the medial axis of the walkable area.
Writes <out_dir>/minimap_art_<variant>.png  (rows = tile y, columns = tile x, blue base at the top)
"""
import json, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage as ndi

N, S = 256, 4           # tiles, pixels per tile
data_dir, out_dir = sys.argv[1], sys.argv[2]
variants = sys.argv[3:] or ['A', 'B']
repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

raw = open(os.path.join(data_dir, 'World200', 'MobaWalkMask.att'), 'rb').read()
m = np.frombuffer(raw[3:3 + N * N], dtype=np.uint8).reshape(N, N)          # [y, x]
decor = np.zeros((N, N), bool)
p = os.path.join(data_dir, 'World200', 'MobaDecorCells.bin')
if os.path.exists(p):
    d = np.frombuffer(open(p, 'rb').read()[:N * N], dtype=np.uint8)
    if d.size == N * N:
        decor = d.reshape(N, N) != 0       # assumed [y, x] like the mask
blocked = ((m & 4) != 0) & ~decor

lab, n = ndi.label(~blocked)
sizes = ndi.sum(~blocked, lab, range(1, n + 1))
main = lab == (1 + int(np.argmax(sizes)))
blocked = blocked | (~main)                                                 # unreachable pockets read as walls

bl, _ = ndi.label(blocked)
border = (set(bl[0, :]) | set(bl[-1, :]) | set(bl[:, 0]) | set(bl[:, -1])) - {0}
exterior = np.isin(bl, list(border)) & blocked
near = ndi.binary_dilation(~blocked, structure=np.ones((5, 5)))
outline = blocked & near
interior = blocked & ~near & ~exterior

layout = json.load(open(os.path.join(repo, 'src', 'GameLogic', 'PlugIns', 'Moba', 'MobaLayout.json'), encoding='utf-8-sig'))

rng = np.random.default_rng(7)
def up(a):
    return np.kron(a.astype(np.uint8), np.ones((S, S), np.uint8)).astype(bool)

def base_image():
    w = N * S
    ground = np.zeros((w, w, 3), np.float32)
    ground[:] = (112, 96, 72)
    grain = ndi.gaussian_filter(rng.normal(0, 1, (w, w)), 1.2) * 14
    ground += grain[..., None]
    img = ground.copy()
    img[up(outline)] = (24, 18, 12)
    img[up(interior)] = (58, 46, 32)
    img[up(exterior)] = (6, 6, 6)
    return img

def soften(mask, sigma=1.1):
    return ndi.gaussian_filter(mask.astype(np.float32), sigma)

def tracks_from_lanes():
    im = Image.new('L', (N * S, N * S), 0)
    dr = ImageDraw.Draw(im)
    for name in ('top', 'mid', 'bot'):
        pts = [(x * S + S / 2, y * S + S / 2) for x, y in layout['lanes'][name]]
        dr.line(pts, fill=255, width=5 * S, joint='curve')
    return np.asarray(im) > 0

def tracks_from_axis():
    from skimage.morphology import skeletonize
    walk = ~blocked
    dist = ndi.distance_transform_edt(walk)
    sk = skeletonize(walk)
    sk &= dist > 2.5
    return ndi.binary_dilation(up(sk), iterations=int(2.2 * S))

# ---- C family: roads only, never into a jungle camp nor through its entrances -------------------------------
from skimage.graph import route_through_array
doors = []
dp = os.path.join(data_dir, 'World200', 'MobaJungleDoors.txt')
if os.path.exists(dp):
    for line in open(dp):
        if line.strip() and not line.startswith('#'):
            a = line.split()
            doors.append((int(a[0]), int(a[1])))
yy, xx = np.mgrid[0:N, 0:N]
door_zone = np.zeros((N, N), bool)
for dx_, dy_ in doors:
    door_zone |= (xx - dx_) ** 2 + (yy - dy_) ** 2 <= 4.5 ** 2
cand = ~blocked & ~door_zone
cl, cn = ndi.label(cand)
csz = ndi.sum(cand, cl, range(1, cn + 1))
field = cl == (1 + int(np.argmax(csz)))            # the open field: no camp interiors, no entrances
wall_dist = ndi.distance_transform_edt(~blocked)
COST = np.where(field, 1.0 + 9.0 / (1.0 + wall_dist) ** 1.5, 1e6)

def snap(pt):
    x, y = pt
    if field[y, x]:
        return (y, x)
    ys, xs = np.nonzero(field)
    i = np.argmin((xs - x) ** 2 + (ys - y) ** 2)
    return (int(ys[i]), int(xs[i]))

def route(points):
    out = []
    for a, b in zip(points, points[1:]):
        path, _ = route_through_array(COST, snap(a), snap(b), fully_connected=True, geometric=True)
        out.extend(path)
    return out

def smooth(path, sigma=2.0):
    a = np.array(path, np.float32)
    a[:, 0] = ndi.gaussian_filter1d(a[:, 0], sigma, mode='nearest')
    a[:, 1] = ndi.gaussian_filter1d(a[:, 1], sigma, mode='nearest')
    return a

def draw_paths(paths, width_tiles, extra=None):
    im = Image.new('L', (N * S, N * S), 0)
    dr = ImageDraw.Draw(im)
    for path in paths:
        pts = [(float(x) * S + S / 2, float(y) * S + S / 2) for y, x in path]
        dr.line(pts, fill=255, width=max(1, int(width_tiles * S)), joint='curve')
    return np.asarray(im) > 0

def lane_routes():
    return [smooth(route([tuple(p) for p in layout['lanes'][n]])) for n in ('top', 'mid', 'bot')]

def river_route():
    mid = N // 2
    xs = np.nonzero(field[mid])[0]
    return smooth(route([(int(xs.min()), mid), (52, 128), (116, mid), (184, 127), (int(xs.max()), mid)]))

def base_routes():
    objs = layout['objects']
    out = []
    for team in ('blue', 'red'):
        nexus = next(o for o in objs if o['type'] == 'nexus' and o['team'] == team)
        for t in [o for o in objs if o['type'] == 'tower' and o['team'] == team]:
            out.append(smooth(route([(nexus['x'], nexus['y']), (t['x'], t['y'])])))
    return out

def mid_links():
    # fountains <-> the lanes next to them
    return [smooth(route([(25, 128), (52, 128)])), smooth(route([(184, 127), (218, 128)]))]

def field_roads(min_wall, prune, min_len=40):
    """Roads in the gaps between the jungle camps: the skeleton of the open field away from walls, spurs pruned."""
    from skimage.morphology import skeletonize
    sk = skeletonize(field & (wall_dist >= min_wall))
    k = np.ones((3, 3), int)
    for _ in range(prune):
        nb = ndi.convolve(sk.astype(int), k, mode='constant') - sk
        sk = sk & ~(sk & (nb <= 1))
    lab, n = ndi.label(sk, structure=np.ones((3, 3)))
    if n:
        sizes = ndi.sum(sk, lab, range(1, n + 1))
        keep = np.isin(lab, [i + 1 for i, v in enumerate(sizes) if v >= min_len])
        sk = sk & keep
    return sk

def draw_mask(mask, width_tiles):
    return ndi.binary_dilation(up(mask), iterations=max(1, int(width_tiles * S / 2)))

def paint(img, mask, color, strength, sigma=1.6):
    a = soften(mask & up(~blocked), sigma)[..., None] * strength
    return img * (1 - a) + np.array(color, np.float32) * a

for v in variants:
    img = base_image()
    if v == 'A':
        img = paint(img, tracks_from_lanes(), (158, 140, 108), 0.85, 2.2)
    elif v == 'B':
        img = paint(img, tracks_from_axis(), (158, 140, 108), 0.85, 2.2)
    elif v == 'C1':     # thin: lanes + river, nothing else
        img = paint(img, draw_paths(lane_routes() + [river_route()], 2.2), (170, 150, 112), 0.9, 1.4)
    elif v == 'C2':     # medium: lanes + river + base spurs + fountain links, with a dark edge
        paths = lane_routes() + [river_route()] + base_routes() + mid_links()
        edge = draw_paths(paths, 4.6)
        img = paint(img, edge, (84, 70, 50), 0.8, 1.2)
        img = paint(img, draw_paths(paths, 3.0), (172, 152, 114), 0.95, 1.2)
    elif v == 'C3':     # wide worn roads: the whole corridor around the route, soft edge, grain
        paths = lane_routes() + [river_route()] + base_routes() + mid_links()
        img = paint(img, draw_paths(paths, 8.0), (128, 110, 82), 0.75, 3.0)
        img = paint(img, draw_paths(paths, 4.0), (176, 156, 118), 0.9, 2.0)
    elif v == 'F':      # final: D3 network, every road (lanes, river, base spurs, camp gaps) with the same size and look
        paths = lane_routes() + [river_route()] + base_routes() + mid_links()
        roads = draw_paths(paths, 3.0) | draw_mask(field_roads(3.5, 6), 3.0)
        edge = draw_paths(paths, 4.6) | draw_mask(field_roads(3.5, 6), 4.6)
        img = paint(img, edge, (84, 70, 50), 0.8, 1.2)
        img = paint(img, roads, (172, 152, 114), 0.95, 1.2)
    elif v in ('D1', 'D2', 'D3'):   # C2 + roads between the jungle camps
        cfg = {'D1': (3.0, 20), 'D2': (2.5, 10), 'D3': (3.5, 6)}[v]
        paths = lane_routes() + [river_route()] + base_routes() + mid_links()
        gap = draw_mask(field_roads(*cfg), 2.6)
        img = paint(img, gap, (84, 70, 50), 0.7, 1.2)
        img = paint(img, draw_mask(field_roads(*cfg), 1.6), (150, 132, 98), 0.9, 1.0)
        edge = draw_paths(paths, 4.6)
        img = paint(img, edge, (84, 70, 50), 0.8, 1.2)
        img = paint(img, draw_paths(paths, 3.0), (172, 152, 114), 0.95, 1.2)
    out = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, f'minimap_art_{v}.png')
    out.save(path)
    print(path)
    if v == 'F' and os.environ.get('MOBA_ART_OZT'):
        # The client samples the art with rows = tile x and columns = tile y (see UpdateBlockedTexture).
        sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
        import make_minimap_atlases as atl
        rgba = np.concatenate([np.asarray(out), np.full((N * S, N * S, 1), 255, np.uint8)], axis=2)
        atl.write_ozt(os.environ['MOBA_ART_OZT'], np.ascontiguousarray(rgba.transpose(1, 0, 2)))
        print(os.environ['MOBA_ART_OZT'])
