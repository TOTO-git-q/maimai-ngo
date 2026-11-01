"""Recolor stock textures whose own pixels are maimai blue / mint / green into the NGO palette (same hue mapping as the
runtime tint: blue/cyan -> lavender-violet, mint/green -> pink), from the backup resources.assets. Writes PNGs named
after the texture into <out_dir> (for ngo_patch_assets.py) plus a before/after sheet.
usage: python ngo_recolor_stock.py <out_dir> name1,name2,..."""
import os, sys
import numpy as np
import UnityPy
from PIL import Image, ImageDraw

OUT, NAMES = sys.argv[1], sys.argv[2].split(",")
os.makedirs(OUT, exist_ok=True)

def remap(img):
    a = np.asarray(img.convert("RGBA")).astype(np.float32)
    rgb = a[..., :3] / 255
    hsv = np.asarray(Image.fromarray(a[..., :3].astype(np.uint8), "RGB").convert("HSV")).astype(np.float32)
    h, s, v = hsv[..., 0] / 255 * 360, hsv[..., 1] / 255, hsv[..., 2]
    nh = h.copy()
    blue = (h >= 175) & (h < 245) & (s >= 0.15)
    green = (h >= 80) & (h < 175) & (s >= 0.15)
    nh[blue] = 262 + (h[blue] - 175) * 0.3
    nh[green] = 322 + (h[green] - 80) * 0.15
    ns = np.where(blue | green, s * 0.9, s)
    out = np.stack([nh / 360 * 255, ns * 255, v], -1).astype(np.uint8)
    rgb2 = np.asarray(Image.fromarray(out, "HSV").convert("RGB"))
    return Image.fromarray(np.concatenate([rgb2, a[..., 3:].astype(np.uint8)], -1), "RGBA")

env = UnityPy.load(r"D:\maimai\backup_ngo_skin\resources.assets")
pairs, done = [], set()
for o in env.objects:
    if o.type.name != "Texture2D" or o.peek_name() not in NAMES or o.peek_name() in done: continue
    t = o.read(); done.add(t.m_Name)
    img = t.image
    new = remap(img)
    new.save(os.path.join(OUT, t.m_Name + ".png"))
    pairs.append((t.m_Name, img, new))
H = 160
sheet = Image.new("RGB", (1200, len(pairs) * (H + 24)), (80, 80, 80))
d = ImageDraw.Draw(sheet)
for i, (n, a, b) in enumerate(pairs):
    y = i * (H + 24)
    d.text((4, y + 4), n, fill=(255, 230, 120))
    x = 4
    for im in (a, b):
        im = im.copy(); im.thumbnail((580, H))
        bg = Image.new("RGBA", im.size, (200, 200, 200, 255)); bg.alpha_composite(im.convert("RGBA"))
        sheet.paste(bg.convert("RGB"), (x, y + 20)); x += 600
sheet.save(os.path.join(OUT, "_sheet.png"))
print("recolored", len(pairs), "missing", sorted(set(NAMES) - done))
