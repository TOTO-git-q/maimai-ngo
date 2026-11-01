"""Contact sheet of the stock (not reskinned) textures that are visible, biggest on-screen area first, with the screens
they appear on. usage: python ngo_stock_sheet.py <audit.csv> <out.png> [min_area] [limit]"""
import csv, sys, collections
import UnityPy
from PIL import Image, ImageDraw

AUDIT, OUT = sys.argv[1], sys.argv[2]
MIN = int(sys.argv[3]) if len(sys.argv) > 3 else 2000
LIMIT = int(sys.argv[4]) if len(sys.argv) > 4 else 80
area, screens = collections.defaultdict(int), collections.defaultdict(set)
for r in csv.DictReader(open(AUDIT, encoding="utf-8")):
    if r["status"] != "STOCK": continue
    t = r["texture"]; area[t] = max(area[t], int(r["area_px"])); screens[t].add(r["screen"][:2])
names = [t for t in sorted(area, key=lambda t: -area[t]) if area[t] >= MIN][:LIMIT]
env = UnityPy.load(r"D:\maimai\backup_ngo_skin\resources.assets")
imgs = {}
for o in env.objects:
    if o.type.name == "Texture2D" and o.peek_name() in names and o.peek_name() not in imgs:
        try: imgs[o.peek_name()] = o.read().image.convert("RGBA")
        except Exception: pass
C, W, H = 8, 200, 170
sheet = Image.new("RGB", (C * W, ((len(names) + C - 1) // C) * H), (60, 60, 60))
d = ImageDraw.Draw(sheet)
for i, n in enumerate(names):
    x, y = (i % C) * W, (i // C) * H
    im = imgs.get(n)
    if im:
        im.thumbnail((W - 8, H - 34))
        bg = Image.new("RGBA", im.size, (200, 200, 200, 255)); bg.alpha_composite(im)
        sheet.paste(bg.convert("RGB"), (x + 4, y + 30))
    d.text((x + 3, y + 2), "%d %s" % (i + 1, n[:30]), fill=(255, 220, 120))
    d.text((x + 3, y + 15), "scr " + ",".join(sorted(screens[n]))[:28], fill=(180, 220, 255))
sheet.save(OUT)
print(len(names), "textures")
