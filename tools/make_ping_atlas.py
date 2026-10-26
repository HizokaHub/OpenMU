"""Builds the MOBA ping atlas the client loads from Data/Interface: MobaPings.OZT (512x256). Reproducible, no randomness.

Layout (the client has the same table in World/Moba/MobaPing.cpp):
  8 x 4 cells of 64x64, index = row * 8 + col (image row 0 is the top of every cell, like the other MOBA atlases):
     0..9   the ten pings (kind 1..10): generic, caution, retreat, push, on my way, all-in, assist me, need vision, enemy missing, enemy vision.
            Each is a dark violet disc with a coloured ring and a white glyph (the style of Eclipse Arena).
     10     thin white ring (ripple)              11  soft white glow          12  edge arrow (white, points up)
     13     white filled disc (tint it dark for the buttons of the wheel)     14  thick white ring (border of a button)
  one 128x128 region at x 384, y 128 (cells 22, 23, 30, 31): the damage wedge, a red-white arc that points up (the client rotates it).

Colours are straight (not premultiplied) alpha; the RGB of fully transparent texels is bled from the nearest visible texel.

usage: make_ping_atlas.py <mu-main/src/bin/Data> [out dir, default <Data>/Interface] [--preview file.png]
"""
import argparse
import math
import os
import struct

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi

OZT_PREFIX = b'\x00\x00\x02\x00'
TGA_FOOTER = b'\x00' * 8 + b'TRUEVISION-XFILE.\x00'
FILE = 'MobaPings.OZT'

CELL = 64
COLS, ROWS = 8, 4
SS = 4
BIG = CELL * SS
C = BIG / 2

BASE = (22, 12, 40)
WHITE = (255, 255, 255)
# (ring colour) per ping kind 1..10
KIND_COLOURS = [(125, 249, 255), (255, 205, 70), (255, 95, 85), (95, 165, 255), (100, 235, 125),
                (255, 125, 45), (255, 107, 214), (85, 232, 232), (255, 165, 60), (255, 70, 145)]
GLYPH_COLOUR = (255, 255, 255)


def write_ozt(path, rgba):
    """rgba: uint8 [h, w, 4], row 0 = top. OZT prefix + a bottom-up 32-bpp TGA (like every Interface OZT)."""
    h, w = rgba.shape[:2]
    header = struct.pack('<BBBHHBHHHHBB', 0, 0, 2, 0, 0, 0, 0, 0, w, h, 32, 0x08)
    pixels = np.ascontiguousarray(rgba[::-1, :, [2, 1, 0, 3]]).tobytes()
    with open(path, 'wb') as f:
        f.write(OZT_PREFIX + header + pixels + TGA_FOOTER)


def bleed(cell):
    cell = cell.copy()
    cell[cell[..., 3] < 3] = 0
    empty = cell[..., 3] == 0
    if empty.all() or not empty.any():
        return cell
    iy, ix = ndi.distance_transform_edt(empty, return_distances=False, return_indices=True)
    cell[..., :3] = cell[iy, ix, :3]
    return cell


def canvas(size=BIG):
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def shrink(img, size):
    return np.asarray(img.resize((size, size), Image.LANCZOS))


def rot(points, deg, cx=C, cy=C):
    a = math.radians(deg)
    ca, sa = math.cos(a), math.sin(a)
    return [(cx + (x - cx) * ca - (y - cy) * sa, cy + (x - cx) * sa + (y - cy) * ca) for x, y in points]


# ---------------------------------------------------------------- glyphs (drawn on a BIG x BIG canvas, centre C)
def glyph_generic(d, k):
    d.ellipse([C - 14 * SS / 2, C - 14 * SS / 2, C + 14 * SS / 2, C + 14 * SS / 2], fill=GLYPH_COLOUR)
    d.ellipse([C - 26 * SS / 2, C - 26 * SS / 2, C + 26 * SS / 2, C + 26 * SS / 2], outline=GLYPH_COLOUR, width=3 * SS // 2)


def glyph_caution(d, k):
    s = SS
    d.polygon([(C, C - 17 * s), (C + 19 * s, C + 14 * s), (C - 19 * s, C + 14 * s)], outline=GLYPH_COLOUR, width=4 * s // 2 + 2)
    d.rectangle([C - 2.2 * s, C - 7 * s, C + 2.2 * s, C + 3 * s], fill=GLYPH_COLOUR)
    d.ellipse([C - 2.6 * s, C + 5.5 * s, C + 2.6 * s, C + 10.5 * s], fill=GLYPH_COLOUR)


def arrow(d, deg, length=34, width=14, head=22, fill=GLYPH_COLOUR):
    s = SS
    shaft = [(C - length / 2 * s, C - width / 4 * s), (C + (length / 2 - head * 0.55) * s, C - width / 4 * s),
             (C + (length / 2 - head * 0.55) * s, C + width / 4 * s), (C - length / 2 * s, C + width / 4 * s)]
    tip = [(C + (length / 2 - head) * s, C - head / 2 * s), (C + length / 2 * s, C), (C + (length / 2 - head) * s, C + head / 2 * s)]
    d.polygon(rot(shaft, deg), fill=fill)
    d.polygon(rot(tip, deg), fill=fill)


def glyph_retreat(d, k):
    arrow(d, 90)            # points down: back


def chevron(d, dy, deg=0, width=22, thick=7):
    s = SS
    pts = [(C - width * s, C + (dy + 8) * s), (C, C + (dy - 10) * s), (C + width * s, C + (dy + 8) * s),
           (C + (width - thick * 1.1) * s, C + (dy + 8 + thick * 0.9) * s), (C, C + (dy - 10 + thick * 1.3) * s), (C - (width - thick * 1.1) * s, C + (dy + 8 + thick * 0.9) * s)]
    d.polygon(rot(pts, deg), fill=GLYPH_COLOUR)


def glyph_push(d, k):
    chevron(d, -9)
    chevron(d, 7)


def glyph_onmyway(d, k):
    arrow(d, 0, 36, 15, 24)
    s = SS
    d.arc([C - 26 * s, C - 26 * s, C + 26 * s, C + 26 * s], 200, 330, fill=GLYPH_COLOUR, width=2 * s)


def sword(d, deg):
    s = SS
    blade = [(C - 3 * s, C - 26 * s), (C + 3 * s, C - 26 * s), (C + 4 * s, C + 10 * s), (C - 4 * s, C + 10 * s)]
    tip = [(C - 3 * s, C - 26 * s), (C + 3 * s, C - 26 * s), (C, C - 32 * s)]
    guard = [(C - 11 * s, C + 10 * s), (C + 11 * s, C + 10 * s), (C + 11 * s, C + 14 * s), (C - 11 * s, C + 14 * s)]
    grip = [(C - 2.5 * s, C + 14 * s), (C + 2.5 * s, C + 14 * s), (C + 2.5 * s, C + 24 * s), (C - 2.5 * s, C + 24 * s)]
    for poly in (blade, tip, guard, grip):
        d.polygon(rot(poly, deg), fill=GLYPH_COLOUR)


def glyph_allin(d, k):
    sword(d, 40)
    sword(d, -40)


def glyph_assist(d, k):
    s = SS
    d.rectangle([C - 4.5 * s, C - 18 * s, C + 4.5 * s, C + 18 * s], fill=GLYPH_COLOUR)
    d.rectangle([C - 18 * s, C - 4.5 * s, C + 18 * s, C + 4.5 * s], fill=GLYPH_COLOUR)


def glyph_eye(d, k, slash=False):
    s = SS
    xs = [x / 2 for x in range(-48, 49)]
    upper = [(C + x * s, C - 17 * (1 - (x / 24.0) ** 2) * s) for x in xs]
    lower = [(C + x * s, C + 17 * (1 - (x / 24.0) ** 2) * s) for x in reversed(xs)]
    d.polygon(upper + lower, fill=GLYPH_COLOUR)
    d.ellipse([C - 10 * s, C - 10 * s, C + 10 * s, C + 10 * s], fill=BASE)
    d.ellipse([C - 5.5 * s, C - 5.5 * s, C + 5.5 * s, C + 5.5 * s], fill=GLYPH_COLOUR)
    if slash:
        d.line([C - 24 * s, C + 22 * s, C + 24 * s, C - 22 * s], fill=BASE, width=8 * s)
        d.line([C - 24 * s, C + 22 * s, C + 24 * s, C - 22 * s], fill=(255, 70, 145), width=int(4.5 * s))


def glyph_needvision(d, k):
    glyph_eye(d, k)


def glyph_missing(d, k):
    s = SS
    d.arc([C - 12 * s, C - 24 * s, C + 12 * s, C], 180, 90, fill=GLYPH_COLOUR, width=int(5.5 * s))
    d.line([C, C - 0.5 * s, C, C + 8 * s], fill=GLYPH_COLOUR, width=int(5.5 * s))
    d.ellipse([C - 3.6 * s, C + 13 * s, C + 3.6 * s, C + 20.2 * s], fill=GLYPH_COLOUR)


def glyph_enemyvision(d, k):
    glyph_eye(d, k, slash=True)


GLYPHS = [glyph_generic, glyph_caution, glyph_retreat, glyph_push, glyph_onmyway, glyph_allin, glyph_assist,
          glyph_needvision, glyph_missing, glyph_enemyvision]


def ping_cell(kind_index):
    colour = KIND_COLOURS[kind_index]
    img, d = canvas()
    d.ellipse([C - 30.5 * SS, C - 30.5 * SS, C + 30.5 * SS, C + 30.5 * SS], fill=BASE + (255,))
    d.ellipse([C - 30.5 * SS, C - 30.5 * SS, C + 30.5 * SS, C + 30.5 * SS], outline=colour + (255,), width=int(3.2 * SS))
    glyph = Image.new('RGBA', (BIG, BIG), (0, 0, 0, 0))
    GLYPHS[kind_index](ImageDraw.Draw(glyph), kind_index)
    img.alpha_composite(glyph)
    return shrink(img, CELL)


# ---------------------------------------------------------------- shapes
def ring_cell(r_out, r_in):
    img, d = canvas()
    d.ellipse([C - r_out * SS, C - r_out * SS, C + r_out * SS, C + r_out * SS], fill=WHITE + (255,))
    d.ellipse([C - r_in * SS, C - r_in * SS, C + r_in * SS, C + r_in * SS], fill=(0, 0, 0, 0))
    return shrink(img, CELL)


def glow_cell():
    yy, xx = np.mgrid[0:CELL, 0:CELL]
    r = np.hypot(xx + 0.5 - CELL / 2, yy + 0.5 - CELL / 2) / (CELL / 2)
    a = np.clip(1 - r, 0, 1) ** 1.6
    out = np.zeros((CELL, CELL, 4), np.uint8)
    out[..., :3] = 255
    out[..., 3] = (a * 255).astype(np.uint8)
    return out


def arrow_cell():
    img, d = canvas()
    s = SS
    pts = [(C, C - 26 * s), (C + 20 * s, C + 20 * s), (C, C + 10 * s), (C - 20 * s, C + 20 * s)]
    d.polygon(pts, fill=(14, 8, 28, 255))
    inner = [(C, C - 20 * s), (C + 14 * s, C + 14 * s), (C, C + 6 * s), (C - 14 * s, C + 14 * s)]
    d.polygon(inner, fill=WHITE + (255,))
    return shrink(img, CELL)


def disc_cell():
    return ring_cell(31.5, 0)


def wedge_region():
    """128 x 128: a ring segment (about 80 degrees) pointing up, white at the core fading to the edges; the client tints it red."""
    size = 128
    yy, xx = np.mgrid[0:size, 0:size]
    dx, dy = xx + 0.5 - size / 2, yy + 0.5 - size / 2
    r = np.hypot(dx, dy) / (size / 2)
    ang = np.degrees(np.arctan2(dx, -dy))          # 0 = up, clockwise
    radial = np.clip(1 - np.abs(r - 0.78) / 0.26, 0, 1) ** 1.1
    angular = np.clip(1 - np.abs(ang) / 46.0, 0, 1) ** 0.55
    a = radial * angular
    out = np.zeros((size, size, 4), np.uint8)
    out[..., :3] = 255
    out[..., 3] = (a * 255).clip(0, 255).astype(np.uint8)
    return out


def build():
    atlas = np.zeros((CELL * ROWS, CELL * COLS, 4), np.uint8)

    def put(index, cell):
        r, c = divmod(index, COLS)
        atlas[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL] = bleed(cell)

    for k in range(10):
        put(k, ping_cell(k))
    put(10, ring_cell(30.5, 28.5))
    put(11, glow_cell())
    put(12, arrow_cell())
    put(13, disc_cell())
    put(14, ring_cell(31.0, 27.0))
    atlas[128:256, 384:512] = bleed(wedge_region())
    return atlas


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('data_dir')
    parser.add_argument('out_dir', nargs='?')
    parser.add_argument('--preview')
    args = parser.parse_args()
    out_dir = args.out_dir or os.path.join(args.data_dir, 'Interface')
    atlas = build()
    os.makedirs(out_dir, exist_ok=True)
    write_ozt(os.path.join(out_dir, FILE), atlas)
    print('wrote', os.path.join(out_dir, FILE), atlas.shape)
    if args.preview:
        back = Image.new('RGBA', (atlas.shape[1], atlas.shape[0]), (60, 60, 70, 255))
        back.alpha_composite(Image.fromarray(atlas))
        back.resize((atlas.shape[1] * 2, atlas.shape[0] * 2), Image.LANCZOS).save(args.preview)


if __name__ == '__main__':
    main()
