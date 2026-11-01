"""For each skin sprite in resources.assets: texture size vs sprite rect, trimmed textureRect, mip count, and whether the
new PNG has visible pixels outside the original's trimmed area (those would be cut off when written in place).
usage: python ngo_sprite_check.py <theme_dir> <out.csv>"""
import csv, os, sys
import UnityPy
from PIL import Image

THEME, OUT = sys.argv[1], sys.argv[2]
names = {os.path.splitext(f)[0] for f in os.listdir(THEME) if f.endswith(".png")}
env = UnityPy.load(r"D:\maimai\Package\Sinmai_Data\resources.assets")
rows = []
for obj in env.objects:
    if obj.type.name != "Sprite": continue
    s = obj.read()
    if s.m_Name not in names: continue
    tex = s.m_RD.texture.read()
    r, tr = s.m_Rect, s.m_RD.textureRect
    off = s.m_RD.textureRectOffset
    trimmed = (round(tr.width), round(tr.height)) != (round(r.width), round(r.height))
    outside = 0
    if trimmed:
        img = Image.open(os.path.join(THEME, s.m_Name + ".png")).convert("RGBA")
        a = img.getchannel("A")
        # visible area of the trimmed rect inside the sprite rect (PNG is top-down, offsets are bottom-up)
        x0, y1 = round(off.x), img.height - round(off.y)
        box = (x0, y1 - round(tr.height), x0 + round(tr.width), y1)
        inside = a.crop(box)
        total = sum(1 for v in a.getdata() if v > 8)
        outside = total - sum(1 for v in inside.getdata() if v > 8)
    rows.append([s.m_Name, tex.m_Name, tex.m_Width, tex.m_Height, round(r.width), round(r.height),
                 round(tr.width), round(tr.height), trimmed, getattr(tex, "m_MipCount", ""), outside,
                 s.m_RD.settingsRaw if hasattr(s.m_RD, "settingsRaw") else ""])
with open(OUT, "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f)
    w.writerow(["sprite", "texture", "tex_w", "tex_h", "rect_w", "rect_h", "trim_w", "trim_h", "trimmed", "mips", "px_outside_trim", "settings"])
    w.writerows(rows)
print("sprites", len(rows), "| texture size != rect", sum(1 for x in rows if (x[2], x[3]) != (x[4], x[5])),
      "| trimmed", sum(1 for x in rows if x[8]), "| trimmed with new pixels outside", sum(1 for x in rows if x[10] > 0),
      "| mips>1", sum(1 for x in rows if isinstance(x[9], int) and x[9] > 1))
