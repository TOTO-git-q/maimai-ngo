"""Popup title bar (UI_CMN_Title_Default, 300x88, sliced; "INFORMATION" and other popup titles): the installed NGO
bar keeps its pink pixel frame, but its charcoal face read as the stock grey strip. The face becomes the
skin's dark violet #4D23CF (the game's pale title text stays readable on it). Writes <out>/UI_CMN_Title_Default.png.
usage: python ngo_title_default_fill.py <out dir>"""
import os, sys
import numpy as np
from PIL import Image

SRC = r"E:\cxref\maimai-ngo\tasks\refs_audit2\now_UI_CMN_Title_Default.png"   # the installed bar (ngo_dump_tex.py)
FACE, NEW = np.array((89, 73, 87)), np.array((77, 35, 207))
a = np.array(Image.open(SRC).convert("RGBA"))
face = (np.abs(a[..., :3].astype(int) - FACE).max(-1) <= 12) & (a[..., 3] > 0)
a[..., :3][face] = NEW
os.makedirs(sys.argv[1], exist_ok=True)
Image.fromarray(a).save(os.path.join(sys.argv[1], "UI_CMN_Title_Default.png"))
print("face pixels recoloured:", int(face.sum()), "of", int((a[..., 3] > 0).sum()))

