"""Recolor the MAGiCAL (v170) background textures toward the NEEDY GIRL OVERDOSE palette: sky blues -> lavender / soft
violet, greens -> pink / magenta, yellows warmed slightly pink. Lightness and detail are untouched (hue/saturation only).
Reads the original textures from the backup resources.assets. Writes <out>/orig/<name>.png, <out>/ngo/<name>.png and a
before/after sheet. usage: python ngo_bg_recolor.py <out_dir> name1,name2,..."""
import os, sys
import numpy as np
import UnityPy
from PIL import Image

OUT, NAMES = sys.argv[1], sys.argv[2].split(",")
os.makedirs(os.path.join(OUT, "orig"), exist_ok=True); os.makedirs(os.path.join(OUT, "ngo"), exist_ok=True)

def hue_map(h):
    """h in degrees -> NGO hue. Piecewise: green 70-170 -> pink 320-345, blue/cyan 170-250 -> violet 255-290."""
    out = h.copy()
    g = (h >= 70) & (h < 170)
    out[g] = 320 + (h[g] - 70) / 100 * 25
    b = (h >= 170) & (h < 250)
    out[b] = 255 + (h[b] - 170) / 80 * 35
    y = (h >= 40) & (h < 70)
    out[y] = h[y] - (h[y] - 40) * 0.5 - 5
    return out % 360

def recolor(img):
    rgba = np.asarray(img.convert("RGBA")).astype(np.float32) / 255
    rgb, a = rgba[..., :3], rgba[..., 3:]
    hsv = np.asarray(Image.fromarray((rgb * 255).astype(np.uint8), "RGB").convert("HSV")).astype(np.float32)
    h = hsv[..., 0] / 255 * 360
    nh = hue_map(h)
    s = hsv[..., 1]
    s = np.where((h >= 170) & (h < 250), s * 0.85, s)          # pastel sky
    hsv2 = np.stack([nh / 360 * 255, np.clip(s, 0, 255), hsv[..., 2]], -1).astype(np.uint8)
    rgb2 = np.asarray(Image.fromarray(hsv2, "HSV").convert("RGB")).astype(np.float32) / 255
    return Image.fromarray((np.concatenate([rgb2, a], -1) * 255).astype(np.uint8), "RGBA")

env = UnityPy.load(r"D:\maimai\backup_ngo_skin\resources.assets")
pairs = []
for obj in env.objects:
    if obj.type.name != "Texture2D" or obj.peek_name() not in NAMES: continue
    t = obj.read()
    img = t.image
    img.save(os.path.join(OUT, "orig", t.m_Name + ".png"))
    new = recolor(img)
    new.save(os.path.join(OUT, "ngo", t.m_Name + ".png"))
    pairs.append((t.m_Name, img, new))

# before / after sheet, each pair scaled to 300 px tall, on a checker so transparency shows
H = 300
cells = []
for name, a, b in pairs:
    w = max(1, round(a.width * H / a.height))
    cells.append((name, a.resize((w, H)), b.resize((w, H))))
W = 1500
rows, row, x = [], [], 0
for c in cells:
    cw = c[1].width * 2 + 30
    if row and x + cw > W: rows.append(row); row, x = [], 0
    row.append(c); x += cw
if row: rows.append(row)
sheet = Image.new("RGBA", (W, len(rows) * (H + 30)), (255, 255, 255, 255))
from PIL import ImageDraw
d = ImageDraw.Draw(sheet)
checker = Image.new("RGBA", (16, 16), (210, 210, 210, 255))
for i, r in enumerate(rows):
    x = 0
    for name, a, b in r:
        y = i * (H + 30) + 20
        for img in (a, b):
            bg = Image.new("RGBA", img.size, (235, 235, 235, 255))
            bg.alpha_composite(img); sheet.paste(bg, (x, y)); x += img.width + 4
        d.text((x - a.width * 2 - 8, y - 16), name + "  (left: original, right: NGO)", fill=(200, 0, 80, 255))
        x += 22
sheet.convert("RGB").save(os.path.join(OUT, "samples_sheet.jpg"), quality=88)
print("recolored", len(pairs))
