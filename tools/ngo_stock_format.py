"""Format study of stock UI textures before redrawing them: for each texture, export the untouched stock image (from
the pre-skin backup) and report its size, sprite border (9-slice L,B,R,T), pivot, every Image that uses it
(GameObject, Image type Simple/Sliced/Tiled/Filled, serialized tint colour, preserveAspect) and a guess whether the
stock art is a white mask meant to be tinted. Writes <out_dir>/<name>.png and <out_dir>/FORMAT.md.
usage: python ngo_stock_format.py <out_dir> <texture name> [...]"""
import os, sys
import numpy as np
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

OUT = sys.argv[1]; NAMES = set(sys.argv[2:])
os.makedirs(OUT, exist_ok=True)
DATA = r"D:\maimai\Package\Sinmai_Data"
TYPES = {0: "Simple", 1: "Sliced", 2: "Tiled", 3: "Filled"}

stock = UnityPy.load(r"D:\maimai\backup_ngo_skin\resources.assets")
info = {n: {"users": []} for n in NAMES}
for o in stock.objects:
    n = o.peek_name() if o.type.name in ("Texture2D", "Sprite") else None
    if n not in NAMES: continue
    if o.type.name == "Texture2D":
        t = o.read(); img = t.image.convert("RGBA"); img.save(os.path.join(OUT, n + ".png"))
        a = np.asarray(img).astype(int); vis = a[..., 3] > 20
        rgb = a[vis][:, :3] if vis.any() else np.zeros((1, 3))
        white = float(((rgb.min(1) > 225)).mean())
        info[n].update(size=(t.m_Width, t.m_Height), white_share=round(white, 2), mean_rgb=tuple(int(v) for v in rgb.mean(0)))
    else:
        s = o.read_typetree()
        b = s["m_Border"]
        info[n].update(border=(b["x"], b["y"], b["z"], b["w"]), pivot=(s["m_Pivot"]["x"], s["m_Pivot"]["y"]), sprite_id=o.path_id)

env = UnityPy.load(os.path.join(DATA, "resources.assets"))
gen = TypeTreeGenerator("2018.4.7f1"); gen.load_local_dll_folder(os.path.join(DATA, "Managed")); env.typetree_generator = gen
objs = env.file.objects
by_sprite = {v["sprite_id"]: k for k, v in info.items() if "sprite_id" in v}
for o in env.objects:
    if o.type.name != "MonoBehaviour": continue
    try:
        t = o.read_typetree()
    except Exception:
        continue
    sp = t.get("m_Sprite")
    if not sp or sp.get("m_FileID") != 0 or sp.get("m_PathID") not in by_sprite: continue
    c = t.get("m_Color", {})
    info[by_sprite[sp["m_PathID"]]]["users"].append((objs[t["m_GameObject"]["m_PathID"]].peek_name(), TYPES.get(t.get("m_Type"), t.get("m_Type")),
        "#%02X%02X%02X a%.2f" % (round(c.get("r", 1) * 255), round(c.get("g", 1) * 255), round(c.get("b", 1) * 255), c.get("a", 1)),
        bool(t.get("m_PreserveAspect"))))

lines = ["# Stock format", "", "| texture | size | border L,B,R,T | stock art | used by (GameObject, Image type, tint, preserveAspect) |", "|---|---|---|---|---|"]
for n in sorted(info):
    v = info[n]
    art = "white mask (tinted by game)" if v.get("white_share", 0) > 0.8 else "coloured art (mean %s)" % (v.get("mean_rgb"),)
    users = sorted(set(v["users"]))
    lines.append("| %s | %s | %s | %s | %s |" % (n, v.get("size"), v.get("border"), art, "; ".join("%s %s %s%s" % (u[0], u[1], u[2], " keepAspect" if u[3] else "") for u in users[:8]) + (" …+%d" % (len(users) - 8) if len(users) > 8 else "")))
open(os.path.join(OUT, "FORMAT.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("\n".join(lines))
