"""List large textures in the backup resources.assets that look like screen backgrounds (sky, clouds, base scenery).
usage: python ngo_list_backgrounds.py <out.csv>"""
import csv, re, sys
import UnityPy
from UnityPy.enums import TextureFormat

env = UnityPy.load(r"D:\maimai\backup_ngo_skin\resources.assets")
rows = []
for obj in env.objects:
    if obj.type.name != "Texture2D": continue
    t = obj.read()
    area = t.m_Width * t.m_Height
    if area < 300 * 300: continue
    rows.append([t.m_Name, t.m_Width, t.m_Height, TextureFormat(t.m_TextureFormat).name, obj.path_id])
rows.sort(key=lambda r: -r[1] * r[2])
with open(sys.argv[1], "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f); w.writerow(["name", "w", "h", "format", "path_id"]); w.writerows(rows)
pat = re.compile(r"BG|Back|Sky|Cloud|Base_|Bg|_Ground|Sea|Map", re.I)
for r in rows:
    if pat.search(r[0]): print(r[0], r[1], r[2])
