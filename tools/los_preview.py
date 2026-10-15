"""Preview of the MOBA line-of-sight rule (same integer algorithm as MobaFog.cpp LineOfSight).
usage: los_preview.py <Data dir> <out.png> x,y[,r] ..."""
import sys, numpy as np
from PIL import Image
N = 256
data, out = sys.argv[1], sys.argv[2]
raw = open(data + '/World200/MobaWalkMask.att', 'rb').read()
wall = (np.frombuffer(raw[3:3 + N * N], np.uint8).reshape(N, N) & 4) != 0   # [y, x]
def opaque(x, y): return bool(wall[y, x])
def los(fx, fy, tx, ty):
    dx, dy = tx - fx, ty - fy
    nx, ny = abs(dx), abs(dy); sx = -1 if dx < 0 else 1; sy = -1 if dy < 0 else 1
    inside = opaque(fx, fy); x, y, ix, iy = fx, fy, 0, 0
    while ix < nx or iy < ny:
        d = (1 + 2 * ix) * ny - (1 + 2 * iy) * nx
        if d == 0:
            if not inside and opaque(x + sx, y) and opaque(x, y + sy): return False
            x += sx; y += sy; ix += 1; iy += 1
        elif d < 0: x += sx; ix += 1
        else: y += sy; iy += 1
        if x == tx and y == ty: return True
        if opaque(x, y):
            if not inside: return False
        else: inside = False
    return True
img = np.zeros((N, N, 3), np.uint8)
img[~wall] = (60, 56, 50); img[wall] = (20, 14, 10)
for a in sys.argv[3:]:
    p = [int(v) for v in a.split(',')]; sx, sy = p[0], p[1]; r = p[2] if len(p) > 2 else 9
    for y in range(max(0, sy - r - 1), min(N, sy + r + 2)):
        for x in range(max(0, sx - r - 1), min(N, sx + r + 2)):
            if (x + .5 - sx - .5) ** 2 + (y + .5 - sy - .5) ** 2 < (r + 1) ** 2 and los(sx, sy, x, y):
                img[y, x] = (230, 220, 120) if not wall[y, x] else (200, 90, 40)
    img[sy, sx] = (255, 0, 0)
Image.fromarray(img).resize((1024, 1024), Image.NEAREST).save(out)
