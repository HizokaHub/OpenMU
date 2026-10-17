"""Closes the diagonal gaps of the MOBA walk mask: two wall cells that only touch at a corner (the two cells beside that corner
open) let a champion slip diagonally through the 'wall'. One of the two open cells becomes wall, so every wall is 4-connected.
The cells around the jungle entrances (MobaJungleDoors.txt) are left alone.

usage: close_pinches.py <mask.att> <doors.txt> [--write]   (without --write only reports)
The same file is the server's embedded resource (Persistence/Initialization/Resources/Terrain201.att) and the client's
Data/World200/MobaWalkMask.att: write both."""
import sys
import numpy as np
from scipy import ndimage as ndi

path, doors_path = sys.argv[1], sys.argv[2]
write = '--write' in sys.argv
raw = bytearray(open(path, 'rb').read())
m = np.frombuffer(bytes(raw[3:3 + 65536]), np.uint8).reshape(256, 256).copy()   # [y, x]
doors = [tuple(map(int, l.split())) for l in open(doors_path) if l.strip() and l[0] != '#']
DOOR_CLEAR = 4.5

def near_door(x, y):
    return any((x - dx) ** 2 + (y - dy) ** 2 <= DOOR_CLEAR ** 2 for dx, dy in doors)

blocked = (m & 4) != 0
before_lab, before_n = ndi.label(~blocked)
before_sizes = ndi.sum(~blocked, before_lab, range(1, before_n + 1))
fills = []
for y in range(1, 255):
    for x in range(1, 255):
        for dx, dy in ((1, 1), (1, -1)):
            # wall cells (x, y) and (x + dx, y + dy), open cells (x + dx, y) and (x, y + dy)
            if blocked[y, x] and blocked[y + dy, x + dx] and not blocked[y, x + dx] and not blocked[y + dy, x]:
                if near_door(x, y) or near_door(x + dx, y + dy):
                    continue
                fills.append((x, y, dx, dy))
print('pinches to close:', len(fills))
closed = blocked.copy()
for x, y, dx, dy in fills:
    # re-check: an earlier fill may have closed this pinch already
    if closed[y, x + dx] or closed[y + dy, x]:
        continue
    closed[y, x + dx] = True          # the open cell beside the corner on the x side
lab, n = ndi.label(~closed)
sizes = ndi.sum(~closed, lab, range(1, n + 1))
print('walkable regions before/after:', before_n, n, ' largest before/after:', int(before_sizes.max()), int(sizes.max()))
added = int(closed.sum() - blocked.sum())
print('wall cells added:', added)
# every cell that was in the main region and is no longer (cut off by the fills)
main_before = before_lab == (1 + int(np.argmax(before_sizes)))
cut = main_before & closed
cut_cells = int(((main_before) & (~closed) & (lab != (1 + int(np.argmax(sizes))))).sum())
print('cells of the main field cut off from it by the fills:', cut_cells)
if write:
    out = m.copy()
    out[closed & ~blocked] = 4
    raw[3:3 + 65536] = out.tobytes()
    open(path, 'wb').write(bytes(raw))
    print('written', path)
