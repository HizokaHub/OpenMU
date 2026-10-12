"""Mirror the blue half (y <= 127) of the MOBA walk mask onto the red half (y' = 255 - y),
after carving a 12-row horizontal road centred on the axis (rows 122..133) and closing
every island the road cuts with wall cells on the road's edges.

usage: mirror_mask.py <in.att> <out.att> <preview.png>
"""
import sys
import numpy as np
from scipy import ndimage as ndi
from PIL import Image, ImageDraw

src, dst, preview = sys.argv[1:4]
raw = open(src, 'rb').read()
hdr = raw[:3]
m = np.frombuffer(raw[3:], dtype=np.uint8).reshape(256, 256).copy()   # [y, x]

BAND_TOP, BAND_BOT = 122, 127          # blue-half rows of the road (mirror gives 128..133)
EDGE_ROW = BAND_TOP - 1                # 121 (mirror gives 134)

blocked = (m == 4)
# solid rock outside the arena (blocked, connected to the border through blocked cells)
lab, _ = ndi.label(blocked)
border = set(lab[0, :]) | set(lab[-1, :]) | set(lab[:, 0]) | set(lab[:, -1])
border.discard(0)
exterior = np.isin(lab, list(border)) & blocked
# enclosed walkable pockets (island interiors): not in the largest walkable component
wl, n = ndi.label(~blocked)
sizes = ndi.sum(~blocked, wl, range(1, n + 1))
main = wl == (1 + int(np.argmax(sizes)))
pocket = (~blocked) & (~main)

out = m.copy()
carved = 0
for y in range(BAND_TOP, BAND_BOT + 1):
    for x in range(256):
        island_wall = blocked[y, x] and not exterior[y, x]
        if island_wall or pocket[y, x]:
            if out[y, x] == 4:
                carved += 1
            out[y, x] = 0
# close the islands along the road edge: pocket cells on the edge row become wall
closed = 0
for x in range(256):
    if pocket[EDGE_ROW, x] and out[EDGE_ROW, x] != 4:
        out[EDGE_ROW, x] = 4
        closed += 1
# the island interiors that touch the band from inside also need a wall in the band's first
# row beyond: handled by the edge row above; pocket cells of rows below the band do not exist
# in the blue half (row 127 is the last one), and the mirror supplies the other side.

final = out.copy()
for y in range(128, 256):
    final[y, :] = out[255 - y, :]

print('carved wall cells:', carved, ' closed edge cells:', closed)
open(dst, 'wb').write(hdr + final.tobytes())

def render(a):
    im = Image.new('RGB', (256, 256)); px = im.load()
    for y in range(256):
        for x in range(256):
            v = a[y, x]
            px[x, 255 - y] = (40, 40, 40) if v == 4 else ((120, 200, 120) if v == 1 else (230, 220, 190))
    d = ImageDraw.Draw(im)
    d.line((0, 255 - 127, 255, 255 - 127), fill=(220, 40, 40))
    return im

W = Image.new('RGB', (256 * 2 + 10, 256), (255, 255, 255))
W.paste(render(m), (0, 0)); W.paste(render(final), (266, 0))
W.resize((W.width * 3, W.height * 3), Image.NEAREST).save(preview)
