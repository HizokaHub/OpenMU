"""Prototypes of minimap zoom modes, made from a screenshot of the current minimap (for deciding with the user).
usage: zoom_prototypes.py <minimap screenshot.png> <out.png> [hero_x hero_y]
"""
import sys
from PIL import Image, ImageDraw, ImageFont, ImageOps

src, out = sys.argv[1:3]
hx, hy = (int(sys.argv[3]), int(sys.argv[4])) if len(sys.argv) > 4 else (273, 465)
im = Image.open(src).convert('RGB')
W, H = im.size
FR = 300           # frame size of each panel


def crop_follow(zoom):
    """Window 1/zoom of the map centred on the hero, kept inside the image."""
    cw, ch = W / zoom, H / zoom
    x0 = min(max(hx - cw / 2, 0), W - cw)
    y0 = min(max(hy - ch / 2, 0), H - ch)
    return im.crop((int(x0), int(y0), int(x0 + cw), int(y0 + ch))).resize((FR, FR), Image.LANCZOS)


def label(img, text, sub):
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, FR, 34), fill=(0, 0, 0))
    d.text((6, 4), text, fill=(255, 220, 120))
    d.text((6, 18), sub, fill=(200, 200, 200))
    return img


panels = []
# A: today
p = im.resize((FR, int(FR * H / W)), Image.LANCZOS)
base = Image.new('RGB', (FR, FR), (12, 12, 12)); base.paste(p, (0, (FR - p.height) // 2))
panels.append(label(base, 'A. Hoy (mapa completo)', 'sin zoom'))
# B/C: follow hero
panels.append(label(crop_follow(1.6), 'B. Zoom 1.6x siguiendo al heroe', 'se ve mas detalle, mapa parcial'))
panels.append(label(crop_follow(2.2), 'C. Zoom 2.2x siguiendo al heroe', 'muy detallado, poca vision global'))
# D: full map + circular lens on hero
lens = crop_follow(3.0).resize((120, 120))
mask = Image.new('L', (120, 120), 0); ImageDraw.Draw(mask).ellipse((0, 0, 119, 119), fill=255)
full = base.copy()
ring = Image.new('RGB', (128, 128), (255, 205, 70)); rm = Image.new('L', (128, 128), 0); ImageDraw.Draw(rm).ellipse((0, 0, 127, 127), fill=255)
full.paste(ring, (FR - 134, 40), rm); full.paste(lens, (FR - 130, 44), mask)
panels.append(label(full, 'D. Mapa completo + lupa 3x', 'lupa circular sobre el heroe'))
# E: bigger minimap (same view, larger on screen) - show as relative size
big = Image.new('RGB', (FR, FR), (12, 12, 12)); b2 = im.resize((FR, int(FR * H / W)), Image.LANCZOS); big.paste(b2, (0, (FR - b2.height) // 2))
small = im.resize((int(FR * 0.62), int(FR * 0.62 * H / W)), Image.LANCZOS)
panels.append(label(big, 'E. Solo agrandar el minimapa', 'ocupa mas pantalla (opcion Tamano)'))
# F: zoom with wheel / key toggle: show 1.6x with hint
f = crop_follow(1.6)
d = ImageDraw.Draw(f); d.rectangle((0, FR - 22, FR, FR), fill=(0, 0, 0)); d.text((6, FR - 17), 'rueda del mouse o tecla para alternar', fill=(255, 255, 255))
panels.append(label(f, 'F. Zoom a voluntad', '1x / 1.6x / 2.2x con la rueda'))

sheet = Image.new('RGB', (FR * 3 + 40, FR * 2 + 30), (30, 30, 30))
for i, pnl in enumerate(panels):
    sheet.paste(pnl, (10 + (i % 3) * (FR + 10), 10 + (i // 3) * (FR + 10)))
sheet.save(out)
