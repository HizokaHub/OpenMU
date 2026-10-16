"""Prototypes of the road-edge treatment on the real arena ground (Crywolf tiles + real Crywolf object models).
usage: road_edge_prototypes.py <mu-main/src/bin/Data> <out dir>   -> proto_01.png .. proto_10.png"""
import math, os, sys
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmd_face as bf
import obj_sprites as osp

data, out_dir = sys.argv[1], sys.argv[2]
os.makedirs(out_dir, exist_ok=True)
TW, TH = 22, 12            # scene size in tiles
T = 64                     # px per tile (ground, before the oblique squash)
SQ = math.sin(osp.PITCH)
W, H = TW * T, int(TH * T * SQ)


def tile_tex(name):
    t = bf.load_tex(os.path.join(data, 'World34'), name)[..., :3]
    im = Image.fromarray((t * 255).astype(np.uint8)).resize((T, T), Image.LANCZOS)
    return np.asarray(im).astype(np.float32) / 255


GROUND, GRASS, ROAD, MUD = (tile_tex(n) for n in ('TileGround01', 'TileGrass01', 'TileRock04', 'TileWood01'))


def hash01(x, y, s=0):
    h = (int(x) * 374761393 + int(y) * 668265263 + s * 1274126177) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0


def noise(fx, fy, s=0):
    x0, y0 = math.floor(fx), math.floor(fy)
    tx, ty = fx - x0, fy - y0
    sx, sy = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)
    a, b, c, d = hash01(x0, y0, s), hash01(x0 + 1, y0, s), hash01(x0, y0 + 1, s), hash01(x0 + 1, y0 + 1, s)
    return (a + (b - a) * sx) * (1 - sy) + (c + (d - c) * sx) * sy


def curve(p0, p1, p2, p3, n=200):
    t = np.linspace(0, 1, n)[:, None]
    return ((1 - t) ** 3) * p0 + 3 * ((1 - t) ** 2) * t * p1 + 3 * (1 - t) * t * t * p2 + t ** 3 * p3


# the road: two crossing curves (tile coordinates)
A = curve(np.array([-1, 8.5]), np.array([6, 3]), np.array([13, 9.5]), np.array([23, 4.0]))
B = curve(np.array([8, -1]), np.array([11, 4]), np.array([9.5, 8]), np.array([12.5, 13]))
ROADS = [A, B]
SS = 8


def road_mask(width, width_fn=None):
    """Road mask on a supersampled grid (SS px per tile): constant width or per-point width (tiles)."""
    im = Image.new('L', (TW * SS, TH * SS), 0)
    d = ImageDraw.Draw(im)
    for path in ROADS:
        for i, (x, y) in enumerate(path):
            w = width_fn(i, x, y) if width_fn else width
            r = max(0.5, w * SS / 2)
            d.ellipse([x * SS - r, y * SS - r, x * SS + r, y * SS + r], fill=255)
    return np.asarray(im).astype(np.float32) / 255


def to_grid(mask):
    return mask.reshape(TH, SS, TW, SS).mean(axis=(1, 3))


def warp(cov, shift=3.2, amp=(0.55, 0.9)):
    out = np.zeros_like(cov)

    def g(xx, yy):
        return cov[min(max(yy, 0), TH - 1), min(max(xx, 0), TW - 1)]
    for y in range(TH):
        for x in range(TW):
            sx = x + (noise(x * .13 + 17, y * .13) - .5) * shift
            sy = y + (noise(x * .13, y * .13 + 53) - .5) * shift
            x0, y0 = int(math.floor(sx)), int(math.floor(sy))
            fx, fy = sx - x0, sy - y0
            v = (g(x0, y0) * (1 - fx) + g(x0 + 1, y0) * fx) * (1 - fy) + (g(x0, y0 + 1) * (1 - fx) + g(x0 + 1, y0 + 1) * fx) * fy
            out[y, x] = min(1, v * (amp[0] + amp[1] * noise(x * .33 + 7, y * .33 + 3)))
    return out


def blend_scene(cov, mud=None):
    """Alpha per tile interpolated across tiles, like the vertex alpha of the game's terrain."""
    field_a = np.array([[0.45 * noise(x * .11, y * .11) for x in range(TW)] for y in range(TH)], np.float32)

    def up(g):
        return np.asarray(Image.fromarray(g.astype(np.float32), 'F').resize((TW * T, TH * T), Image.BILINEAR))
    fa, ra = up(field_a), np.clip(up(cov), 0, 1)
    yy, xx = np.mgrid[0:TH * T, 0:TW * T]

    def tex(t):
        return t[yy % T, xx % T]
    base = tex(GROUND) * (1 - fa[..., None]) + tex(GRASS) * fa[..., None]
    if mud is not None:
        ma = np.clip(up(mud), 0, 1)[..., None]
        base = base * (1 - ma) + tex(MUD) * ma
    ra = ra[..., None]
    return base * (1 - ra) + tex(ROAD) * ra


def place(objs, canvas):
    """objs: list of (type, tile_x, tile_y, scale, yaw). Pastes oblique sprites, back to front."""
    for (t, x, y, sc, yaw) in sorted(objs, key=lambda o: o[2]):
        spr, (ax, ay) = osp.render_sprite(data, t, T / 100.0, yaw, sc)
        if spr is None:
            continue
        im = Image.fromarray(spr, 'RGBA')
        canvas.paste(im, (int(x * T - ax), int(y * T * SQ - ay)), im)


def scatter(cov, types, chance, scale_fn, seed, lo=0.08, hi=0.65):
    objs = []
    for y in range(TH):
        for x in range(TW):
            if not (lo <= cov[y, x] <= hi) or hash01(x, y, seed) > chance:
                continue
            k = int(hash01(x, y, seed + 1) * 1000)
            px = x + .1 + .8 * hash01(x, y, seed + 2)
            py = y + .1 + .8 * hash01(x, y, seed + 3)
            objs.append((types[k % len(types)], px, py, scale_fn(hash01(x, y, seed + 4)), hash01(x, y, seed + 5) * 6.28))
    return objs


def varwidth(i, x, y):
    return 0.55 + 1.75 * noise(x * 0.9 + 3, y * 0.9) ** 1.3 + (1.2 if noise(x * 0.35, y * 0.35, 9) > 0.78 else 0)


def render(name, label, cov, objs_fn=None, mud=None):
    img = blend_scene(cov, mud)
    squashed = Image.fromarray((img.clip(0, 1) * 255).astype(np.uint8)).resize((W, H), Image.LANCZOS)
    if objs_fn:
        place(objs_fn(cov), squashed)
    d = ImageDraw.Draw(squashed)
    d.rectangle([0, 0, 640, 22], fill=(0, 0, 0))
    d.text((6, 5), label, fill=(255, 255, 255))
    squashed.save(os.path.join(out_dir, name))
    print(name)


base_cov = to_grid(road_mask(1.1))
wob = warp(base_cov)
vw_cov = np.clip(warp(to_grid(road_mask(None, varwidth)), 2.4, (0.8, 0.5)) * 1.1, 0, 1)
dither = np.zeros_like(base_cov)
for y in range(TH):
    for x in range(TW):
        c = vw_cov[y, x]
        dither[y, x] = 1.0 if hash01(x, y, 5) < c ** 0.8 else (0.55 if hash01(x, y, 6) < c * 0.7 else 0.0)
mud_band = np.clip(ndi.gaussian_filter((vw_cov > 0.05).astype(np.float32), 1.0) - (vw_cov > 0.3), 0, 1) * 0.9


def low_grass(r):
    return 1.0 + 0.8 * r


def mix_grass(r):
    return (1.8 + 0.8 * r) if r > 0.85 else 0.9 + 0.8 * r


render('proto_01.png', '1  Borde con ruido (solo suelo)', wob)
render('proto_02.png', '2  Ancho variable: estrecha y ensancha', vw_cov)
render('proto_03.png', '3  Migas: bordes con dientes y manchas sueltas', dither)
render('proto_04.png', '4  Pasto bajo en las orillas', wob, lambda c: scatter(c, [15, 16, 17, 18], 0.55, low_grass, 10))
render('proto_05.png', '5  Pasto bajo + matas altas ocasionales', wob, lambda c: scatter(c, [15, 16, 17, 18], 0.5, mix_grass, 20))
render('proto_06.png', '6  Piedras pequenas en el borde', wob, lambda c: scatter(c, [19, 20, 22, 33], 0.4, lambda r: 0.45 + 0.6 * r, 30))
render('proto_07.png', '7  Piedras + pasto bajo', wob,
       lambda c: scatter(c, [15, 16, 17, 18], 0.4, low_grass, 40) + scatter(c, [19, 20, 22], 0.22, lambda r: 0.4 + 0.5 * r, 41))
render('proto_08.png', '8  Arbustos y ramas secas bajos', wob, lambda c: scatter(c, [26, 27, 24, 28, 35], 0.25, lambda r: 0.05 + 0.07 * r, 50))
render('proto_09.png', '9  Banda de tierra oscura en el borde + pasto', vw_cov, lambda c: scatter(c, [15, 16, 17, 18], 0.4, low_grass, 60), mud=mud_band)
render('proto_10.png', '10 Combinado: ancho variable + migas + pasto + piedras + arbustos', dither,
       lambda c: scatter(c, [15, 16, 17, 18], 0.45, mix_grass, 70) + scatter(c, [19, 20, 22, 33], 0.18, lambda r: 0.4 + 0.5 * r, 71)
       + scatter(c, [26, 27, 28], 0.1, lambda r: 0.05 + 0.06 * r, 72), mud=mud_band * 0.6)
