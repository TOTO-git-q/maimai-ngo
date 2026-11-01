"""Write the NGO panel colors straight into Sinmai_Data\\resources.assets (replaces the NgoTint runtime hook).
maimai tints its white panel textures through each UI Image's serialized m_Color (blue mode select, mint ticket, green
music select...). For every Image whose sprite uses a texture in the list, a blue color becomes the NGO violet ramp
and a green one the pink ramp (ngo_ramp.py) at its luminance; greys, reds, golds stay. Alpha is kept.
Writes resources.assets.new next to the original; the caller copies it over.
usage: python ngo_patch_colors.py <textures.txt> <log.csv>"""
import csv, os, sys
import numpy as np
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ngo_ramp

LIST, LOG = sys.argv[1], sys.argv[2]
LO, HI = 0.2, 0.9   # tints multiply the texture: keep them in the light half of the ramp
ASSETS = r"D:\maimai\Package\Sinmai_Data\resources.assets"
WANT = {l.strip() for l in open(LIST, encoding="utf-8") if l.strip() and not l.lstrip().startswith((";", "#"))}

env = UnityPy.load(ASSETS)
gen = TypeTreeGenerator("2018.4.7f1")
gen.load_local_dll_folder(os.path.join(os.path.dirname(ASSETS), "Managed"))
env.typetree_generator = gen

sprite_tex = {}
for o in env.objects:
    if o.type.name == "Sprite":
        try:
            s = o.read()
            sprite_tex[o.path_id] = s.m_RD.texture.read().m_Name
        except Exception:
            pass

rows = []
for o in env.objects:
    if o.type.name != "MonoBehaviour": continue
    try:
        tree = o.read_typetree()
    except Exception:
        continue
    spr = tree.get("m_Sprite")
    if not isinstance(spr, dict) or "m_Color" not in tree or spr.get("m_FileID") != 0: continue
    tex = sprite_tex.get(spr.get("m_PathID"))
    if tex not in WANT: continue
    c = tree["m_Color"]
    rgb = np.array([[c["r"], c["g"], c["b"]]], np.float32) * 255
    new = ngo_ramp.remap(rgb, LO, HI)[0]
    if np.abs(new - rgb[0]).max() < 1: continue
    before = "%02X%02X%02X" % tuple(int(round(v)) for v in rgb[0])
    c["r"], c["g"], c["b"] = (float(v) / 255 for v in new)
    o.save_typetree(tree)
    rows.append([o.path_id, tex, before, "%02X%02X%02X" % tuple(int(round(v)) for v in new)])

data = env.file.save()
assert len(data) > 60 * 1024 * 1024, "output unexpectedly small: %d bytes" % len(data)
with open(ASSETS + ".new", "wb") as f:
    f.write(data)
with open(LOG, "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f); w.writerow(["path_id", "texture", "before", "after"]); w.writerows(rows)
print("recolored", len(rows), "images")
