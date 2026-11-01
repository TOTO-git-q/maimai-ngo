"""Remove the window buttons (_ [] x) from the music-select board's title bar. With the board fitted to the circle
(ngo_board_fit.py) the bar sits behind the tabs and the buttons only peek out as a white corner at the right edge.
Every bar column right of the bar's plain part is replaced by a plain bar column.
usage: python ngo_board_nobuttons.py <board.png in> <board.png out>"""
import sys
import numpy as np
from PIL import Image

a = np.asarray(Image.open(sys.argv[1]).convert("RGBA")).copy()
H, W = a.shape[:2]
# the bar: top rows whose middle pixel is the bar violet; find its height
mid = W // 2
bar_rgb = a[6, mid, :3].astype(int)
near = np.abs(a[:200, mid, :3].astype(int) - bar_rgb).max(1) < 40
rows = int(np.where(near)[0].max()) + 5   # include the bar's bottom outline
band = a[:rows]
light = (band[..., :3].min(2) > 200) & (band[..., 3] > 0)   # white button faces
cols = np.where(light.any(0))[0]
cols = cols[cols > W // 2]
x0, x1 = cols.min() - 8, cols.max() + 8
plain = a[:rows, x0 - 40].copy()
for x in range(x0, min(x1 + 1, W)):
    keep = a[:rows, x, 3] > 0
    a[:rows, x][keep] = plain[keep]
Image.fromarray(a).save(sys.argv[2])
print("bar rows", rows, "buttons cleared x", x0, "-", x1)
