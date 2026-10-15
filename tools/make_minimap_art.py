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
    im = ndi.binary_dilation(up(sk), iterations=int(2.2 * S))
    return im

for v in variants:
    img = base_image()
    walk_px = up(~blocked)
    if v == 'A':
        t = tracks_from_lanes()
    else:
        t = tracks_from_axis()
    t &= walk_px
    a = soften(t, 2.2)[..., None]
    track_col = np.array((158, 140, 108), np.float32)
    img = img * (1 - a * 0.85) + track_col * a * 0.85
    out = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, f'minimap_art_{v}.png')
    out.save(path)
    print(path)
