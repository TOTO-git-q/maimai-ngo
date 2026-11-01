"""Fit the music-select board (select_layout.ini images.board) so its bottom arc sits on the screen circle's bottom and
its top rises by RAISE px behind the tab row. Geometry from the ui.json dump (screen px, y up, 0 = top of the lower
square's centre line used by the dump): board shown at 0.84 (selector group scale), texture 2160x1300 with the arc
bottom at texture row 932; the screen circle bottom is at y = -960 and the current board top at -484.
usage: python ngo_board_fit.py <raise px>"""
import json, sys

INI = r"D:\maimai\Package\LocalAssets\PartnerLive2D\select_layout.ini"
GROUP, TEX_W, TEX_H, ARC_BOTTOM = 0.84, 2160, 1300, 932
CIRCLE_BOTTOM, TOP_NOW, ORIGIN_Y = -960.0, -484.0, -659.5   # ORIGIN_Y: shown centre (-809) - config y (-178) * 0.84
top = TOP_NOW + float(sys.argv[1])
k = (top - CIRCLE_BOTTOM) / ARC_BOTTOM          # shown px per texture px
centre = top - TEX_H * k / 2
lines = open(INI, encoding="utf-8").read().split("\n")
i = next(n for n, l in enumerate(lines) if l.startswith("layout = "))
d = json.loads(lines[i][len("layout = "):])
b = d["images"]["board"]
b.update(width=round(TEX_W * k / GROUP, 1), height=round(TEX_H * k / GROUP, 1), y=round((centre - ORIGIN_Y) / GROUP, 1))
lines[i] = "layout = " + json.dumps(d, separators=(",", ":"))
open(INI, "w", encoding="utf-8", newline="").write("\n".join(lines))
print("board", {k2: b[k2] for k2 in ("x", "y", "width", "height")}, "scale %.3f" % (k / 0.5))
