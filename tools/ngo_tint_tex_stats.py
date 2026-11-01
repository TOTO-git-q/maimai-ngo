"""Mean opaque color of the tinted panel textures: stock (backup) vs the installed NGO redraw. A maimai tint multiplies
the texture, so a redraw that is itself colored comes out muddy under any tint.
usage: python ngo_tint_tex_stats.py"""
import os
import numpy as np
import UnityPy
from PIL import Image

THEME = r"D:\maimai\Package\LocalAssets\PartnerLive2D\theme_ngo_test"
NAMES = [l.strip() for l in open(r"D:\maimai\Package\LocalAssets\PartnerLive2D\ngo_tint.txt", encoding="utf-8")
         if l.strip() and not l.startswith(";")]


def mean(img):
    a = np.asarray(img.convert("RGBA")).astype(np.float32)
    m = a[..., 3] > 200
    return "#%02X%02X%02X" % tuple(int(v) for v in a[m][:, :3].mean(0)) if m.any() else "-"


stock = {}
env = UnityPy.load(r"D:\maimai\backup_ngo_skin\resources.assets")
for o in env.objects:
    if o.type.name == "Texture2D" and o.peek_name() in NAMES and o.peek_name() not in stock:
        stock[o.peek_name()] = mean(o.read().image)
for n in NAMES:
    p = os.path.join(THEME, n + ".png")
    print("%-36s stock %-8s ngo %s" % (n, stock.get(n, "?"), mean(Image.open(p)) if os.path.exists(p) else "(none)"))
