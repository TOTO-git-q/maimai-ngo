"""Write the NGO skin PNGs straight into Sinmai_Data\\resources.assets (no runtime mod needed).
Each Texture2D keeps its name, size and format (BC7 re-encoded with etcpak, RGBA32 stays RGBA32); its pixels move from
the .resS stream into the .assets file. Sprites whose new art reaches outside the original trimmed (tight) area get their
textureRect opened to the full rect so nothing is cut off. Backups: D:\\maimai\\backup_ngo_skin (restore = copy back).
usage: python ngo_patch_assets.py <theme_dir> <log.csv> [--only name1,name2]"""
import csv, os, sys
import UnityPy
from UnityPy.enums import TextureFormat
from PIL import Image

THEME, LOG = sys.argv[1], sys.argv[2]
only = set(sys.argv[sys.argv.index("--only") + 1].split(",")) if "--only" in sys.argv else None
ASSETS = os.environ.get("NGO_TARGET", r"D:\maimai\Package\Sinmai_Data\resources.assets")   # or a StreamingAssets .ab
BUNDLE = ASSETS.endswith(".ab")
pngs = {os.path.splitext(f)[0]: os.path.join(THEME, f) for f in os.listdir(THEME) if f.endswith(".png")}
if only: pngs = {k: v for k, v in pngs.items() if k in only}

env = UnityPy.load(ASSETS)
rows = []
sprites = {}
for obj in env.objects:
    if obj.type.name == "Sprite":
        s = obj.read()
        if s.m_Name in pngs: sprites[s.m_Name] = (obj, s)
for obj in env.objects:
    if obj.type.name != "Texture2D": continue
    t = obj.read()
    if t.m_Name not in pngs: continue
    img = Image.open(pngs[t.m_Name]).convert("RGBA")
    if img.size != (t.m_Width, t.m_Height):
        rows.append([t.m_Name, "skipped: size %s vs %dx%d" % (img.size, t.m_Width, t.m_Height)]); continue
    fmt = TextureFormat(t.m_TextureFormat)
    t.set_image(img, target_format=fmt)
    t.save()
    note = "written " + fmt.name
    if t.m_Name in sprites:
        sobj, s = sprites[t.m_Name]
        rd, r = s.m_RD, s.m_Rect
        if (round(rd.textureRect.width), round(rd.textureRect.height)) != (round(r.width), round(r.height)):
            a = img.getchannel("A")
            off = rd.textureRectOffset
            x0, y1 = round(off.x), img.height - round(off.y)
            inside = a.crop((x0, y1 - round(rd.textureRect.height), x0 + round(rd.textureRect.width), y1))
            if a.getbbox() and (inside.getbbox() is None or sum(1 for v in a.getdata() if v > 8) != sum(1 for v in inside.getdata() if v > 8)):
                rd.textureRect.x, rd.textureRect.y = 0, 0
                rd.textureRect.width, rd.textureRect.height = r.width, r.height
                rd.textureRectOffset.x, rd.textureRectOffset.y = 0, 0
                s.save()
                note += "; sprite textureRect opened to full rect"
    rows.append([t.m_Name, note])

# UnityPy reads unchanged objects lazily from the original file: build the whole output before opening it for writing
if BUNDLE:
    data = env.file.save(packer="lz4")
else:
    data = env.file.save()
    assert len(data) > 60 * 1024 * 1024, "output unexpectedly small: %d bytes" % len(data)
# written next to the original; the caller copies it over resources.assets (renaming onto it fails from here)
with open(ASSETS + ".new", "wb") as f:
    f.write(data)
with open(LOG, "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f); w.writerow(["texture", "result"]); w.writerows(rows)
print("written", sum(1 for r in rows if r[1].startswith("written")), "| opened rects", sum(1 for r in rows if "opened" in r[1]),
      "| skipped", sum(1 for r in rows if not r[1].startswith("written")))
