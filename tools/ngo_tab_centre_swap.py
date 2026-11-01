"""Put the tabset_v2 selected-tab layers back into the stock roles. Stock: the base (MainTabPanel._bgImage) is
full-colour art and the WhiteBase overlay is a plain white silhouette whose alpha the game pulses as a flash.
tabset_v2 delivered the reverse (white base, violet overlay), so the face would fade to white on every pulse.
Here: base = v2 base under v2 overlay (the painted face, outline, arrow), overlay = white silhouette of that shape.
usage: python ngo_tab_centre_swap.py <tabset_v2 replacements dir> <out dir>"""
import os, sys
import numpy as np
from PIL import Image

src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
base = Image.open(os.path.join(src, "tabbase_flat.png")).convert("RGBA")
over = Image.open(os.path.join(src, "tabwhite_flat.png")).convert("RGBA")
face = Image.alpha_composite(base, over)
a = np.asarray(face).copy()
a[..., 3] = np.where(a[..., 3] > 127, 255, 0)
face = Image.fromarray(a)
face.save(os.path.join(out, "tabbase_flat.png"))
w = np.full_like(a, 255)
w[..., 3] = a[..., 3]
Image.fromarray(w).save(os.path.join(out, "tabwhite_flat.png"))
print("base", face.size, "opaque px", int((a[..., 3] > 0).sum()))
