"""Compose the music-select board from Codex's layers (out/select_board6): field, rim, title bar, then the decor
sprites. My art-direction choices live here, not in the layers:
- FIELD_COLORS: the field stripes / dots recoloured a step stronger than delivered so the pattern reads under content;
- DECOR_SCALE: the board texture is shown at 1286/2160 of its size, so sprites are enlarged (nearest) to come out at
  the same on-screen size as the identical floats drifting in the menu background (1:1 game px there);
- DECOR: which sprite goes where (board-texture px, sprite centre).
usage: python ngo_board_compose.py <layers_dir> <out.png>"""
import json, os, sys
import numpy as np
from PIL import Image

SRC, OUT = sys.argv[1], sys.argv[2]
SHOWN_W, TEX_W = 1286, 2160
DECOR_SCALE = TEX_W / SHOWN_W                     # ~1.68
FIELD_COLORS = {(0xF7, 0xE1, 0xFB): (0xF4, 0xD4, 0xF4),   # stripe A
                (0xFC, 0xE1, 0xF1): (0xFB, 0xE6, 0xF5),   # stripe B
                (0xF2, 0xC6, 0xEC): (0xEA, 0xB6, 0xE6)}   # dots

NUDGE = {"float_jine_mid.png": (70, 30), "float_folder_mid.png": (-80, 20)}   # board-texture px

layers = [os.path.join(SRC, n) for n in ("layer_0_field.png", "layer_1_rim.png", "layer_2_titlebar.png")]
board = Image.new("RGBA", Image.open(layers[0]).size, (0, 0, 0, 0))
for i, p in enumerate(layers):
    im = Image.open(p).convert("RGBA")
    if i == 0:
        a = np.array(im)
        for src, dst in FIELD_COLORS.items():
            m = (a[..., 0] == src[0]) & (a[..., 1] == src[1]) & (a[..., 2] == src[2]) & (a[..., 3] > 0)
            a[m, :3] = dst
        im = Image.fromarray(a, "RGBA")
    board.alpha_composite(im)

pos = json.load(open(os.path.join(SRC, "decor", "positions.json")))["sprites"]
for name, (x, y) in pos.items():
    sp = Image.open(os.path.join(SRC, "decor", name)).convert("RGBA")
    cx, cy = x + sp.width / 2, y + sp.height / 2           # Codex's placement centre ...
    dx, dy = NUDGE.get(name, (0, 0)); cx += dx; cy += dy    # ... nudged clear of the field's edge line
    big = sp.resize((round(sp.width * DECOR_SCALE), round(sp.height * DECOR_SCALE)), Image.NEAREST)
    board.alpha_composite(big, (round(cx - big.width / 2), round(cy - big.height / 2)))
board.save(OUT)
print("composed ->", OUT, board.size)
