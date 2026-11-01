"""Where do the skin textures live? Lists, for every PNG in the theme folder, the game asset file holding a Texture2D or
Sprite of that name, its size and texture format. usage: python ngo_locate_textures.py <theme_dir> <out.csv>"""
import csv, os, sys, struct, glob
import UnityPy

THEME, OUT = sys.argv[1], sys.argv[2]
DATA = r"D:\maimai\Package\Sinmai_Data"
names = {os.path.splitext(f)[0] for f in os.listdir(THEME) if f.endswith(".png")}
files = [os.path.join(DATA, f) for f in ("resources.assets", "sharedassets0.assets")] + \
        [f for f in glob.glob(os.path.join(DATA, "**", "*"), recursive=True) if os.path.isfile(f) and "StreamingAssets" in f
         and os.path.basename(os.path.dirname(f)).lower() in ("ui", "uibundle")]
rows, found = [], set()
for path in files:
    env = UnityPy.load(path)
    for obj in env.objects:
        if obj.type.name not in ("Texture2D", "Sprite"): continue
        d = obj.read()
        nm = d.m_Name
        if nm not in names: continue
        if obj.type.name == "Texture2D":
            rows.append([nm, "Texture2D", os.path.relpath(path, DATA), d.m_Width, d.m_Height, UnityPy.enums.TextureFormat(d.m_TextureFormat).name, obj.path_id])
        else:
            tex = d.m_RD.texture.read() if d.m_RD.texture else None
            rows.append([nm, "Sprite", os.path.relpath(path, DATA), int(d.m_Rect.width), int(d.m_Rect.height),
                         tex.m_Name if tex else "", obj.path_id])
        found.add(nm)
with open(OUT, "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f); w.writerow(["name", "kind", "file", "w", "h", "format_or_texture", "path_id"]); w.writerows(rows)
print("names", len(names), "found", len(found), "rows", len(rows))
from collections import Counter
print(Counter((r[1], r[2], r[5] if r[1] == "Texture2D" else "") for r in rows).most_common(12))
print("missing e.g.", sorted(names - found)[:10])
