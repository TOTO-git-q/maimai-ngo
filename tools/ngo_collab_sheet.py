"""Contact sheets of the game's NEEDY GIRL OVERDOSE collab items (icons, travel-partner charas, frames, nameplates),
extracted from StreamingAssets/A000/AssetBundleImages, numbered for picking.
usage: python ngo_collab_sheet.py <out dir>"""
import glob, os, sys
import UnityPy
from PIL import Image, ImageDraw, ImageFont

AB = r"D:\maimai\Package\Sinmai_Data\StreamingAssets\A000\AssetBundleImages"
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
IDS = {"icon": ["455101", "455102"] + ["5551%02d" % i for i in range(1, 9)],
       "chara": ["455101", "455102"] + ["5551%02d" % i for i in range(1, 9)],
       "frame": ["455101", "455102", "455103", "455104", "555101", "555102", "555103"],
       "nameplate": ["455101", "455102", "555101", "555102"]}
CELL = {"icon": (220, 220), "chara": (260, 260), "frame": (360, 200), "nameplate": (600, 110)}
font = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 22)

def biggest(path):
    env = UnityPy.load(path); best = None
    for o in env.objects:
        if o.type.name == "Texture2D":
            t = o.read()
            if best is None or t.m_Width * t.m_Height > best.width * best.height: best = t.image
    return best

for kind, ids in IDS.items():
    cw, ch = CELL[kind]; cols = 5 if kind != "nameplate" else 2
    rows = (len(ids) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cw, rows * (ch + 30)), (40, 40, 48)); d = ImageDraw.Draw(sheet)
    for i, idv in enumerate(ids):
        hits = [p for p in glob.glob(os.path.join(AB, kind, "*%s*.ab" % idv))]
        x, y = (i % cols) * cw, (i // cols) * (ch + 30)
        d.text((x + 6, y + 2), "%d  %s" % (i + 1, idv), fill=(255, 220, 255), font=font)
        if not hits: continue
        im = biggest(hits[0])
        if im is None: continue
        im = im.convert("RGBA"); im.thumbnail((cw - 8, ch - 8))
        sheet.paste(im, (x + (cw - im.width) // 2, y + 30 + (ch - im.height) // 2), im)
    sheet.save(os.path.join(OUT, kind + ".png")); print(kind, len(ids))
