"""Per screen: the textures actually drawn (from the walk's *.ui.json), whether the skin replaced them, their on-screen
area, and the mean hue/saturation of the stock texture (to spot leftover maimai colors: mint, orange, saturated blue).
usage: python ngo_ui_audit.py <walk_dir> <manifest.csv> <out.csv>"""
import csv, glob, json, os, sys, colorsys
import UnityPy
from PIL import Image

WALK, MANIFEST, OUT = sys.argv[1:4]
replaced = {r["texture"] for r in csv.DictReader(open(MANIFEST, encoding="utf-8"))}
seen = {}
for f in sorted(glob.glob(os.path.join(WALK, "*.ui.json"))):
    screen = os.path.basename(f)[:-8]
    d = json.load(open(f, encoding="utf-8"))
    for it in d["items"]:
        if "w" not in it: continue
        area = max(0, it["w"]) * max(0, it["h"])
        key = (screen, it["texture"])
        if key not in seen or seen[key]["area"] < area:
            seen[key] = {"screen": screen, "texture": it["texture"], "sprite": it["sprite"], "area": area, "path": it["path"]}
names = {v["texture"] for v in seen.values()}
env = UnityPy.load(r"D:\maimai\backup_ngo_skin\resources.assets")
color = {}
for o in env.objects:
    if o.type.name != "Texture2D" or o.peek_name() not in names or o.peek_name() in color: continue
    try:
        img = o.read().image.convert("RGBA").resize((32, 32))
    except Exception:
        continue
    px = [p for p in img.getdata() if p[3] > 128]
    if not px: color[o.peek_name()] = ("", ""); continue
    r = sum(p[0] for p in px) / len(px) / 255; g = sum(p[1] for p in px) / len(px) / 255; b = sum(p[2] for p in px) / len(px) / 255
    h, s, v = colorsys.rgb_to_hsv(r, g, b)
    color[o.peek_name()] = (round(h * 360), round(s, 2))
rows = []
for v in seen.values():
    h, s = color.get(v["texture"], ("", ""))
    rows.append([v["screen"], v["texture"], v["sprite"], v["area"], "skin" if v["texture"] in replaced else "STOCK", h, s, v["path"]])
rows.sort(key=lambda r: (r[0], r[4] != "STOCK", -r[3]))
with open(OUT, "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f); w.writerow(["screen", "texture", "sprite", "area_px", "status", "hue", "sat", "path"]); w.writerows(rows)
stock = [r for r in rows if r[4] == "STOCK" and r[3] >= 2000]
print("drawn textures", len(rows), "| stock with area>=2000px", len(stock))
for r in stock: print(r[0][:22].ljust(22), r[1][:44].ljust(44), str(r[3]).rjust(7), str(r[5]).rjust(4), r[6], r[7][:50])
