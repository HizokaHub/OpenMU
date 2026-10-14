"""Builds the two MOBA minimap texture atlases the client loads from Data/Interface. Reproducible, no randomness.

MobaPortraits.OZT (512x128, IMAGE_MINIMAP_INTERFACE + 7): 8 x 2 cells of 64x64 (u step 0.125, v step 0.5).
    Row 0, cells 0..6 : colour circular portraits by base class (CharacterClass.Number >> 2):
                        0 Dark Wizard, 1 Dark Knight, 2 Elf, 3 Magic Gladiator, 4 Dark Lord, 5 Summoner, 6 Rage Fighter.
                        Cut from the character-select busts Data/Logo/NewFace01..07.bmd (MODEL_FACE + class), circle
                        alpha r 30.5 with a 1.5 px darker band at the edge.
    Row 0, cell 7     : white ring (outer r 31.5, inner r 26.5), tinted with glColor for the team ring.
    Row 1, cells 8..14: the same portraits in greyscale (dead state, full brightness: darken with glColor).
    Row 1, cell 15    : white filled disc r 31.5 (tint black for the backing disc).

MobaMinimapIcons.OZT (256x256, IMAGE_MINIMAP_INTERFACE + 8): 8 x 8 cells of 32x32, index = row * 8 + col.
     0..3 totems blue / green / red / yellow (pre-coloured)   4 soft white glow        5 crossed swords on a black disc
     6 white '+' with dark outline                            7 white water drop       8 fountain basin, neutral (pale water, drop)
     9 fountain basin, blue water + '+'                       10 fountain basin, red water + '+'
    11 jungle entrance arch (sand)    12 turret (white, tint by team)    13 nexus crystal (white, tint by team)
    14 ward eye (white, tint green)   15 small white dot r 6             16 red X (dead mark)
    17 thin white ring (outer r 15.5, inner r 14)            18..63 transparent.

Image row 0 is the TOP of every cell, and the client samples it at v = 0: the files are written as bottom-up
TGAs (descriptor 0x08, like every Interface OZT) and CGlobalBitmap::OpenTga copies file row y to texture row
ny - 1 - y, so the top image row lands in texture row 0. Colours are straight (not premultiplied) alpha; the RGB of
fully transparent texels is bled from the nearest visible texel so GL_LINEAR does not pull dark fringes in.

usage: make_minimap_atlases.py <mu-main/src/bin/Data> [out dir, default <Data>/Interface] [--preview <dir>]
"""
import argparse
import io
import math
import os
import struct
import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmd_face  # noqa: E402

OZT_PREFIX = b'\x00\x00\x02\x00'
TGA_FOOTER = b'\x00' * 8 + b'TRUEVISION-XFILE.\x00'   # TGA 2.0 footer, present in the stock OZT files too

PORTRAIT_FILE = 'MobaPortraits.OZT'
ICON_FILE = 'MobaMinimapIcons.OZT'
CLASS_NAMES = ['Dark Wizard', 'Dark Knight', 'Elf', 'Magic Gladiator', 'Dark Lord', 'Summoner', 'Rage Fighter']

PCELL = 64          # portrait cell
ICELL = 32          # icon cell
SS = 4              # icon supersampling factor
N = ICELL * SS

OUTLINE = (14, 11, 9)
WHITE = (255, 255, 255)
TOTEM_COLOURS = [(60, 140, 255), (70, 230, 90), (255, 60, 50), (255, 220, 50)]   # blue, green, red, yellow
BLUE = (70, 150, 255)
RED = (255, 75, 65)
SAND = (225, 200, 140)


def mix(c, t, k):
    return tuple(int(round(a + (b - a) * k)) for a, b in zip(c, t))


# ---------------------------------------------------------------- OZT read / write
def write_ozt(path, rgba):
    """rgba: uint8 [h, w, 4], row 0 = top of the image. Writes the OZT prefix + a bottom-up 32-bpp TGA."""
    h, w = rgba.shape[:2]
    header = struct.pack('<BBBHHBHHHHBB', 0, 0, 2, 0, 0, 0, 0, 0, w, h, 32, 0x08)
    pixels = np.ascontiguousarray(rgba[::-1, :, [2, 1, 0, 3]]).tobytes()
    with open(path, 'wb') as f:
        f.write(OZT_PREFIX + header + pixels + TGA_FOOTER)


def read_ozt_like_client(path):
    """Mirror of CGlobalBitmap::OpenTga: returns the RGBA texture buffer exactly as uploaded (row 0 = v 0)."""
    buf = open(path, 'rb').read()
    nx, ny = struct.unpack_from('<hh', buf, 16)
    if buf[20] != 32:
        raise ValueError('not 32 bpp')
    src = np.frombuffer(buf, np.uint8, nx * ny * 4, 22).reshape(ny, nx, 4)
    tex = np.empty_like(src)
    for y in range(ny):
        tex[ny - 1 - y] = src[y][:, [2, 1, 0, 3]]
    return tex


def read_ozt_pil(path):
    return np.asarray(Image.open(io.BytesIO(open(path, 'rb').read()[4:]), formats=['TGA']).convert('RGBA'))


# ---------------------------------------------------------------- generic helpers
def coverage(n, inside, ss=8):
    """Exact-ish area coverage (ss x ss samples per pixel) of inside(x, y) in pixel units, origin at the top-left corner."""
    o = (np.arange(n * ss) + 0.5) / ss
    xx, yy = np.meshgrid(o, o)
    return inside(xx, yy).reshape(n, ss, n, ss).mean(axis=(1, 3)).astype(np.float32)


def disc_cov(n, r, cx=None, cy=None):
    cx = n / 2 if cx is None else cx
    cy = n / 2 if cy is None else cy
    return coverage(n, lambda x, y: np.hypot(x - cx, y - cy) <= r)


def ring_cov(n, r_out, r_in):
    return coverage(n, lambda x, y: (np.hypot(x - n / 2, y - n / 2) <= r_out) & (np.hypot(x - n / 2, y - n / 2) >= r_in))


def to_rgba8(rgb, alpha):
    """rgb: [h, w, 3] or a tuple, values 0..255; alpha: [h, w] 0..1."""
    out = np.zeros(alpha.shape + (4,), np.float32)
    out[..., :3] = rgb
    out[..., 3] = alpha * 255
    return out.round().clip(0, 255).astype(np.uint8)


def bleed(cell, min_alpha=3):
    """Drop near-invisible ringing and copy the colour of the nearest visible texel into every transparent texel."""
    cell = cell.copy()
    cell[cell[..., 3] < min_alpha] = 0
    empty = cell[..., 3] == 0
    if empty.all() or not empty.any():
        return cell
    iy, ix = ndi.distance_transform_edt(empty, return_distances=False, return_indices=True)
    cell[..., :3] = cell[iy, ix, :3]
    return cell


# ---------------------------------------------------------------- portraits
def portrait_cells(data_dir):
    logo = os.path.join(data_dir, 'Logo')
    circle = disc_cov(PCELL, 30.5)
    band = ring_cov(PCELL, 30.5, 29.0)
    colour, grey = [], []
    for k in range(7):
        path = os.path.join(logo, 'NewFace%02d.bmd' % (k + 1))
        rgb, (fx0, fy0, fx1, fy1) = bmd_face.render_bust(path, logo, 512)
        fw, fh = fx1 - fx0, fy1 - fy0
        side = max(fw, fh) * 1.35
        ccx, ccy = (fx0 + fx1) / 2, (fy0 + fy1) / 2 + fh * 0.02
        box = (int(ccx - side / 2), int(ccy - side / 2), int(ccx + side / 2), int(ccy + side / 2))
        pad = 256                                       # some crops (long hair) reach past the render: pad with its background
        canvas = np.empty((rgb.shape[0] + 2 * pad, rgb.shape[1] + 2 * pad, 3), np.uint8)
        canvas[:] = [round(v * 255) for v in bmd_face.BACKGROUND]
        canvas[pad:-pad, pad:-pad] = rgb
        img = Image.fromarray(canvas).crop(tuple(v + pad for v in box))
        img = img.resize((PCELL * 4, PCELL * 4), Image.LANCZOS).resize((PCELL, PCELL), Image.LANCZOS)
        c = np.asarray(img).astype(np.float32) * (1 - 0.6 * band)[..., None]
        g = c[..., 0] * 0.299 + c[..., 1] * 0.587 + c[..., 2] * 0.114
        colour.append(bleed(to_rgba8(c, circle)))
        grey.append(bleed(to_rgba8(np.dstack([g, g, g]), circle)))
        print('  portrait %d %-15s NewFace%02d.bmd crop %s' % (k, CLASS_NAMES[k], k + 1, box))
    ring = bleed(to_rgba8(WHITE, ring_cov(PCELL, 31.5, 26.5)))
    disc = bleed(to_rgba8(WHITE, disc_cov(PCELL, 31.5)))
    return colour + [ring] + grey + [disc]


def build_portrait_atlas(data_dir):
    cells = portrait_cells(data_dir)
    atlas = np.zeros((2 * PCELL, 8 * PCELL, 4), np.uint8)
    for i, c in enumerate(cells):
        atlas[(i // 8) * PCELL:(i // 8 + 1) * PCELL, (i % 8) * PCELL:(i % 8 + 1) * PCELL] = c
    return atlas


# ---------------------------------------------------------------- icon drawing (4x supersampled, premultiplied float canvas)
def new_canvas():
    return np.zeros((N, N, 4), np.float32)


def poly(points):
    im = Image.new('L', (N, N), 0)
    ImageDraw.Draw(im).polygon([(x * SS, y * SS) for x, y in points], fill=255)
    return np.asarray(im) > 127


def shape(inside):
    """Boolean mask at supersampled resolution from inside(x, y) in cell pixel units."""
    o = (np.arange(N) + 0.5) / SS
    xx, yy = np.meshgrid(o, o)
    return inside(xx, yy)


def circle(cx, cy, r):
    return shape(lambda x, y: np.hypot(x - cx, y - cy) <= r)


def ellipse(x0, y0, x1, y1):
    cx, cy, rx, ry = (x0 + x1) / 2, (y0 + y1) / 2, (x1 - x0) / 2, (y1 - y0) / 2
    return shape(lambda x, y: ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1)


def rect(x0, y0, x1, y1):
    return shape(lambda x, y: (x >= x0) & (x <= x1) & (y >= y0) & (y <= y1))


def grow(mask, w):
    """Mask dilated by w cell pixels (round corners)."""
    return ndi.distance_transform_edt(~mask) <= w * SS


def paint(cv, mask, rgb, alpha=1.0):
    a = mask.astype(np.float32) * alpha
    cv[..., :3] = np.asarray(rgb, np.float32) / 255 * a[..., None] + cv[..., :3] * (1 - a[..., None])
    cv[..., 3] = a + cv[..., 3] * (1 - a)


def outlined(cv, mask, rgb, w=1.5, outline=OUTLINE):
    paint(cv, grow(mask, w), outline)
    paint(cv, mask, rgb)


def finish(cv):
    a = cv[..., 3]
    rgb = np.where(a[..., None] > 0, cv[..., :3] / np.maximum(a[..., None], 1e-6), 0)
    big = Image.fromarray(to_rgba8(rgb * 255, a), 'RGBA')
    return np.asarray(big.resize((ICELL, ICELL), Image.LANCZOS))


# ---------------------------------------------------------------- icons
def icon_totem(col):
    """Short dark stone pillar under a large flame of the totem colour with a near-white core."""
    cv = new_canvas()
    paint(cv, ellipse(8.5, 26.5, 23.5, 30), (0, 0, 0), 0.5)                          # ground shadow
    body = poly([(11.5, 28.5), (20.5, 28.5), (19, 19), (13, 19)])
    paint(cv, grow(body, 1.5), OUTLINE)
    paint(cv, body, (100, 94, 86))
    paint(cv, body & poly([(16, 28.5), (21, 28.5), (21, 18), (16, 18)]), (64, 60, 54))
    flame = [(16, 3.2), (19, 7), (21, 5.6), (24.5, 12.3), (23, 17.6), (16, 21.5), (9, 17.6), (7.5, 12.3), (11, 5.6), (13, 7)]
    outlined(cv, poly(flame), col)
    inner = [(16 + (x - 16) * 0.62, 21.5 + (y - 21.5) * 0.62) for x, y in flame]
    paint(cv, poly(inner), mix(col, WHITE, 0.45))
    paint(cv, poly([(16, 10.8), (19.2, 15.1), (16, 19.5), (12.8, 15.1)]), mix(col, WHITE, 0.85))
    return finish(cv)


def icon_glow():
    """Soft white radial gradient: windowed gaussian, exactly 0 at r = 16."""
    yy, xx = np.mgrid[0:ICELL, 0:ICELL] + 0.5
    t = np.hypot(xx - ICELL / 2, yy - ICELL / 2) / (ICELL / 2)
    a = np.where(t < 1, (np.exp(-4 * t * t) - np.exp(-4)) / (1 - np.exp(-4)), 0)
    return to_rgba8(WHITE, a.astype(np.float32))


def sword_parts(direction):
    """Polygons of one sword crossing the cell centre; direction = unit vector pommel -> tip (screen, y down)."""
    dx, dy = direction
    nx, ny = -dy, dx

    def pt(t, s):
        return (16 + t * dx + s * nx, 16 + t * dy + s * ny)

    blade = [pt(-3.2, -1.6), pt(8.2, -1.6), pt(11, 0), pt(8.2, 1.6), pt(-3.2, 1.6)]
    edge = [pt(-3.2, 0), pt(8.2, 0), pt(11, 0), pt(8.2, 1.6), pt(-3.2, 1.6)]
    guard = [pt(-4.6, -4.3), pt(-3.2, -4.3), pt(-3.2, 4.3), pt(-4.6, 4.3)]
    grip = [pt(-8.4, -1.0), pt(-4.6, -1.0), pt(-4.6, 1.0), pt(-8.4, 1.0)]
    pommel = pt(-9.4, 0)
    return blade, edge, guard, grip, pommel


def icon_swords():
    cv = new_canvas()
    paint(cv, circle(16, 16, 13.6), (10, 8, 6))
    paint(cv, circle(16, 16, 13.6) & ~circle(16, 16, 12.6), (70, 64, 58))
    for d in ((-0.7071, -0.7071), (0.7071, -0.7071)):
        blade, edge, guard, grip, (px, py) = sword_parts(d)
        parts = poly(blade) | poly(guard) | poly(grip) | circle(px, py, 1.7)
        paint(cv, grow(parts, 1.0), (0, 0, 0))
        paint(cv, poly(blade), (240, 242, 248))
        paint(cv, poly(edge), (170, 178, 195))
        paint(cv, poly(grip), (120, 74, 40))
        paint(cv, poly(guard), (235, 185, 60))
        paint(cv, circle(px, py, 1.7), (235, 185, 60))
    return finish(cv)


def plus_mask(cx, cy, length, width):
    h, w = length / 2, width / 2
    return rect(cx - w, cy - h, cx + w, cy + h) | rect(cx - h, cy - w, cx + h, cy + w)


def icon_plus():
    cv = new_canvas()
    outlined(cv, plus_mask(16, 16, 22, 6), WHITE)
    return finish(cv)


def drop_mask(cx, tip_y, cy, r):
    """Teardrop: circle (cx, cy, r) plus the two tangents to the tip point."""
    dist = cy - tip_y
    alpha = math.asin(r / dist)                       # half-angle at the tip
    tx = r * math.cos(alpha)
    ty = cy - r * math.sin(alpha)
    tri = poly([(cx, tip_y), (cx + tx, ty), (cx - tx, ty)])
    return tri | circle(cx, cy, r)


def icon_drop():
    cv = new_canvas()
    outlined(cv, drop_mask(16, 4.5, 19.5, 7.5), WHITE)
    return finish(cv)


def icon_fountain(water, glyph):
    """Top-down stone basin: dark outline, stone rim with block joints, dark inner lip, water, highlight, glyph."""
    cv = new_canvas()
    paint(cv, circle(16, 16, 14.5), OUTLINE)
    rim = circle(16, 16, 13.0)
    paint(cv, rim, (172, 162, 142))
    joints = np.zeros_like(rim)
    for k in range(8):
        a = math.radians(22.5 + 45 * k)
        dx, dy = math.sin(a), -math.cos(a)
        nx, ny = -dy * 0.45, dx * 0.45
        joints |= poly([(16 + dx * 9 + nx, 16 + dy * 9 + ny), (16 + dx * 14 + nx, 16 + dy * 14 + ny),
                        (16 + dx * 14 - nx, 16 + dy * 14 - ny), (16 + dx * 9 - nx, 16 + dy * 9 - ny)])
    paint(cv, joints & rim, (118, 108, 94))
    paint(cv, circle(16, 16, 9.7), (40, 34, 28))
    paint(cv, circle(16, 16, 8.7), water)
    paint(cv, ellipse(9.8, 9.3, 18, 14), mix(water, WHITE, 0.55), 0.8)
    if glyph == 'drop':
        paint(cv, drop_mask(16, 10, 17.5, 3.6), WHITE, 0.95)
    else:
        paint(cv, plus_mask(16, 16, 12, 4), WHITE)
    return finish(cv)


def arch_mask(x0, x1, top, bottom):
    """Rectangle x0..x1 / top..bottom whose top is a half circle (an upright 'n' / upside-down U outline)."""
    r = (x1 - x0) / 2
    cx = (x0 + x1) / 2
    return shape(lambda x, y: (x >= x0) & (x <= x1) & (y <= bottom) &
                 ((y >= top + r) | (np.hypot(x - cx, y - (top + r)) <= r)))


def icon_arch():
    """Jungle entrance: sand stone arch (upside-down U) about 20x20 px with a dark doorway and dark outline."""
    cv = new_canvas()
    outer = arch_mask(7, 25, 7, 25.5)
    door = arch_mask(12, 20, 12.5, 25.5)
    paint(cv, grow(outer, 1.5), OUTLINE)
    paint(cv, outer & ~door, SAND)
    paint(cv, outer & ~door & shape(lambda x, y: x > 20.6), mix(SAND, (0, 0, 0), 0.22))   # shaded right pier
    paint(cv, grow(door, 0.9) & outer, OUTLINE)                                           # outline inside the opening
    paint(cv, door, (30, 24, 18))
    return finish(cv)


def icon_turret():
    """Crenellated tower, white with a light shade on the right half (tinted by team), dark outline and window."""
    cv = new_canvas()
    merlons = rect(7.5, 4.5, 11.5, 9) | rect(14, 4.5, 18, 9) | rect(20.5, 4.5, 24.5, 9)
    top = rect(7.5, 8.5, 24.5, 12.5)
    body = poly([(10.5, 12.5), (21.5, 12.5), (23, 25.5), (9, 25.5)])
    base = rect(7, 25, 25, 28.5)
    tower = merlons | top | body | base
    paint(cv, grow(tower, 1.5), OUTLINE)
    paint(cv, tower, WHITE)
    paint(cv, tower & shape(lambda x, y: x > 16), (212, 212, 212))
    paint(cv, rect(7.5, 12.3, 24.5, 13.1) & ~body, OUTLINE)                               # ledge under the platform
    paint(cv, rect(7, 24.6, 25, 25.4), OUTLINE)                                           # line above the base
    paint(cv, rect(14.8, 15.5, 17.2, 21) | circle(16, 15.5, 1.2), OUTLINE)                # window slit
    return finish(cv)


def icon_nexus():
    """Tall crystal with two side shards, white / light grey facets (tinted by team) and dark outline."""
    cv = new_canvas()
    for sign in (-1, 1):                                                                  # small leaning crystals
        bx, by, tx, ty, w = 16 + sign * 6.0, 27.3, 16 + sign * 11.0, 13.5, 2.5
        L = math.hypot(tx - bx, ty - by)
        dx, dy = (tx - bx) / L, (ty - by) / L
        nx, ny = -dy * w, dx * w
        sx, sy = tx - dx * 3.2, ty - dy * 3.2
        m = poly([(bx + nx, by + ny), (sx + nx, sy + ny), (tx, ty), (sx - nx, sy - ny), (bx - nx, by - ny)])
        ridge = poly([(bx, by), (sx, sy), (tx, ty), (sx - nx, sy - ny), (bx - nx, by - ny)]) if sign > 0 else \
            poly([(bx, by), (sx, sy), (tx, ty), (sx + nx, sy + ny), (bx + nx, by + ny)])
        paint(cv, grow(m, 1.5), OUTLINE)
        paint(cv, m, (232, 232, 232))
        paint(cv, m & ridge, (182, 182, 182))
    main = poly([(16, 3.2), (22.5, 10.4), (22.5, 21.6), (16, 28.6), (9.5, 21.6), (9.5, 10.4)])
    paint(cv, grow(main, 1.5), OUTLINE)
    paint(cv, main, WHITE)
    paint(cv, main & shape(lambda x, y: x > 16), (200, 200, 200))
    paint(cv, poly([(16, 3.2), (22.5, 10.4), (16, 12.4), (9.5, 10.4)]) & main, (236, 236, 236))   # top facet
    paint(cv, poly([(16, 3.2), (22.5, 10.4), (16, 12.4)]) & main, (222, 222, 222))
    paint(cv, poly([(9.5, 10.4), (16, 12.4), (22.5, 10.4), (22.5, 11.1), (16, 13.1), (9.5, 11.1)]), OUTLINE, 0.55)
    return finish(cv)


def icon_ward():
    """Eye: white almond (tinted green), dark outline, grey iris, dark pupil, white highlight."""
    a, b = 12.5, 8.0
    r = (a * a + b * b) / (2 * b)
    off = r - b
    lens = circle(16, 16 + off, r) & circle(16, 16 - off, r)
    cv = new_canvas()
    outlined(cv, lens, WHITE)
    paint(cv, circle(16, 16, 5.6) & lens, OUTLINE)
    paint(cv, circle(16, 16, 4.4), (165, 165, 165))
    paint(cv, circle(16, 16, 2.4), OUTLINE)
    paint(cv, circle(14.6, 14.6, 1.2), WHITE)
    return finish(cv)


def icon_dot():
    return to_rgba8(WHITE, disc_cov(ICELL, 6))


def icon_x():
    cv = new_canvas()
    m = np.zeros((N, N), bool)
    for sx in (1, -1):
        ax, ay, bx, by = 16 - sx * 8.5, 7.5, 16 + sx * 8.5, 24.5
        L = math.hypot(bx - ax, by - ay)
        nx, ny = -(by - ay) / L * 2.6, (bx - ax) / L * 2.6
        m |= poly([(ax + nx, ay + ny), (bx + nx, by + ny), (bx - nx, by - ny), (ax - nx, ay - ny)])
    paint(cv, grow(m, 1.5), OUTLINE)
    paint(cv, m, (225, 40, 35))
    return finish(cv)


def icon_ring():
    return to_rgba8(WHITE, ring_cov(ICELL, 15.5, 14))


def icon_cells():
    cells = [icon_totem(c) for c in TOTEM_COLOURS]
    cells += [icon_glow(), icon_swords(), icon_plus(), icon_drop(),
              icon_fountain((150, 200, 215), 'drop'),
              icon_fountain(mix(BLUE, WHITE, 0.15), 'plus'),
              icon_fountain(mix(RED, WHITE, 0.15), 'plus'),
              icon_arch(), icon_turret(), icon_nexus(), icon_ward(), icon_dot(), icon_x(), icon_ring()]
    cells = [np.array(c) for c in cells]
    for c in cells[:17]:                 # keep a fully transparent 1 px frame (only the heal ring reaches the edge)
        c[[0, -1], :, 3] = 0
        c[:, [0, -1], 3] = 0
    return [bleed(c) for c in cells]


ICON_NAMES = ['totem blue', 'totem green', 'totem red', 'totem yellow', 'glow', 'swords badge', 'plus', 'water drop',
              'fountain neutral', 'fountain blue', 'fountain red', 'jungle arch', 'turret', 'nexus', 'ward eye', 'dot',
              'dead X', 'heal ring']


def build_icon_atlas():
    atlas = np.zeros((8 * ICELL, 8 * ICELL, 4), np.uint8)
    for i, c in enumerate(icon_cells()):
        atlas[(i // 8) * ICELL:(i // 8 + 1) * ICELL, (i % 8) * ICELL:(i % 8 + 1) * ICELL] = c
    return atlas


# ---------------------------------------------------------------- previews (only with --preview)
def checker(w, h, a=(52, 52, 56), b=(70, 70, 76), step=8):
    yy, xx = np.mgrid[0:h, 0:w]
    m = ((xx // step + yy // step) % 2).astype(bool)
    out = np.zeros((h, w, 4), np.uint8)
    out[...] = a + (255,)
    out[m] = b + (255,)
    return Image.fromarray(out, 'RGBA')


def atlas_sheet(atlas, cell, scale):
    h, w = atlas.shape[:2]
    big = Image.fromarray(atlas, 'RGBA').resize((w * scale, h * scale), Image.NEAREST)
    sheet = checker(w * scale, h * scale)
    sheet.alpha_composite(big)
    d = ImageDraw.Draw(sheet)
    cols, rows = w // cell, h // cell
    for c in range(1, cols):
        d.line((c * cell * scale, 0, c * cell * scale, h * scale), fill=(255, 0, 255, 160))
    for r in range(1, rows):
        d.line((0, r * cell * scale, w * scale, r * cell * scale), fill=(255, 0, 255, 160))
    for r in range(rows):
        for c in range(cols):
            d.text((c * cell * scale + 3, r * cell * scale + 2), str(r * cols + c), fill=(255, 255, 0, 255))
    return sheet


def tinted(img, rgb, alpha=1.0):
    a = np.asarray(img).astype(np.float32)
    a[..., :3] *= np.asarray(rgb, np.float32) / 255
    a[..., 3] *= alpha
    return Image.fromarray(a.clip(0, 255).astype(np.uint8), 'RGBA')


def usage_mock(patlas, iatlas):
    """How the client would compose the cells (glColor modulate), at minimap size, over a map-like ground."""
    P = Image.fromarray(patlas, 'RGBA')
    I = Image.fromarray(iatlas, 'RGBA')
    pc = lambda i, s: P.crop(((i % 8) * 64, (i // 8) * 64, (i % 8) * 64 + 64, (i // 8) * 64 + 64)).resize((s, s), Image.LANCZOS)
    ic = lambda i, s: I.crop(((i % 8) * 32, (i // 8) * 32, (i % 8) * 32 + 32, (i // 8) * 32 + 32)).resize((s, s), Image.LANCZOS)
    bg = Image.new('RGBA', (560, 72), (128, 104, 72, 255))
    ImageDraw.Draw(bg).rectangle((0, 36, 560, 72), fill=(40, 30, 20, 255))
    for row, y in ((0, 7), (1, 43)):
        x = 6
        for k in range(7):                                         # champions: backing + portrait + team ring
            team = BLUE if k < 4 else RED
            dead = k == 3
            bg.alpha_composite(tinted(pc(15, 22), (0, 0, 0), 0.8), (x - 1, y - 1))
            bg.alpha_composite(tinted(pc(8 + k, 20), (150, 150, 150)) if dead else pc(k, 20), (x, y))
            bg.alpha_composite(tinted(pc(7, 22), (140, 140, 140) if dead else team), (x - 1, y - 1))
            if dead:
                bg.alpha_composite(ic(16, 12), (x + 4, y + 4))
            x += 26
        for k in range(4):                                         # totems with tinted glow
            bg.alpha_composite(tinted(ic(4, 22), TOTEM_COLOURS[k], 0.7), (x - 3, y - 2))
            bg.alpha_composite(ic(k, 16), (x, y + 1))
            x += 22
        for k, team in ((8, None), (9, BLUE), (10, RED)):           # fountains (+ heal ring when owned)
            if team:
                bg.alpha_composite(tinted(ic(17, 30), team, 0.7), (x - 6, y - 6))
            bg.alpha_composite(ic(k, 18), (x, y))
            x += 30
        bg.alpha_composite(ic(5, 12), (x - 12, y - 4))             # contested badge on the last fountain
        bg.alpha_composite(ic(11, 8), (x, y + 6))
        x += 14
        for k, col in ((12, BLUE), (12, RED), (13, BLUE), (13, RED), (14, (90, 230, 110))):
            bg.alpha_composite(tinted(ic(k, 14), col), (x, y + 3))
            x += 17
        for k in range(8):                                         # recall arc made of dots
            a = math.radians(k * 25)
            bg.alpha_composite(tinted(ic(15, 6), (120, 230, 255)), (int(x + 8 + 11 * math.sin(a)), int(y + 8 - 11 * math.cos(a))))
        bg.alpha_composite(ic(6, 10), (x + 30, y + 5))
        bg.alpha_composite(tinted(ic(7, 10), (120, 200, 255)), (x + 44, y + 5))
    return bg


def write_previews(out, patlas, iatlas):
    os.makedirs(out, exist_ok=True)
    Image.fromarray(patlas, 'RGBA').save(os.path.join(out, 'MobaPortraits.png'))
    Image.fromarray(iatlas, 'RGBA').save(os.path.join(out, 'MobaMinimapIcons.png'))
    atlas_sheet(patlas, PCELL, 3).save(os.path.join(out, 'MobaPortraits_x3.png'))
    atlas_sheet(iatlas[:3 * ICELL], ICELL, 5).save(os.path.join(out, 'MobaMinimapIcons_x5.png'))
    mock = usage_mock(patlas, iatlas)
    mock.resize((mock.width * 3, mock.height * 3), Image.NEAREST).save(os.path.join(out, 'usage_mock_x3.png'))


# ---------------------------------------------------------------- main
def verify(path, atlas):
    tex = read_ozt_like_client(path)
    pil = read_ozt_pil(path)
    if not np.array_equal(pil, atlas):
        raise SystemExit('%s: PIL decode differs from the source image' % path)
    if not np.array_equal(tex, atlas):
        raise SystemExit('%s: client-style decode differs (texture row 0 must be the top image row)' % path)
    print('  %s  %d bytes  %dx%d  verified (PIL + client-style reader, texture row 0 = top row)'
          % (os.path.basename(path), os.path.getsize(path), atlas.shape[1], atlas.shape[0]))


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('data', help='mu-main/src/bin/Data')
    ap.add_argument('out', nargs='?', help='output folder (default <Data>/Interface)')
    ap.add_argument('--preview', metavar='DIR', help='also write PNG atlases, enlarged sheets and a usage mock here')
    args = ap.parse_args()
    out = args.out or os.path.join(args.data, 'Interface')
    os.makedirs(out, exist_ok=True)

    print('portraits:')
    patlas = build_portrait_atlas(args.data)
    iatlas = build_icon_atlas()
    ppath = os.path.join(out, PORTRAIT_FILE)
    ipath = os.path.join(out, ICON_FILE)
    write_ozt(ppath, patlas)
    write_ozt(ipath, iatlas)
    verify(ppath, patlas)
    verify(ipath, iatlas)
    if args.preview:
        write_previews(args.preview, patlas, iatlas)
        print('  previews in', args.preview)


if __name__ == '__main__':
    main()
