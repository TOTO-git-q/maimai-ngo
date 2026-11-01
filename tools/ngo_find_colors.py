"""Find where maimai stores the per-screen tint of the NGO panel textures: every UI Image (MonoBehaviour) in
resources.assets whose sprite uses one of the given textures, with its serialized m_Color. Read-only.
usage: python ngo_find_colors.py <resources.assets> <out.csv> [tex1,tex2,...]   (default: ngo_tint.txt list)"""
import csv, os, sys
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

SRC, OUT = sys.argv[1], sys.argv[2]
if len(sys.argv) > 3:
    WANT = set(sys.argv[3].split(","))
else:
    WANT = {l.strip() for l in open(r"D:\maimai\Package\LocalAssets\PartnerLive2D\ngo_tint.txt", encoding="utf-8")
            if l.strip() and not l.startswith(";")}
MANAGED = os.path.join(os.path.dirname(SRC), "Managed")

env = UnityPy.load(SRC)
gen = TypeTreeGenerator("2018.4.7f1")
gen.load_local_dll_folder(MANAGED)
env.typetree_generator = gen

# sprite path id -> texture name
sprite_tex = {}
for o in env.objects:
    if o.type.name == "Sprite":
        s = o.read()
        try:
            t = s.m_RD.texture.read() if s.m_RD.texture else None
            sprite_tex[o.path_id] = t.m_Name if t else ""
        except Exception:
            pass
print("sprites", len(sprite_tex))

rows, n_mb, n_err = [], 0, 0
for o in env.objects:
    if o.type.name != "MonoBehaviour": continue
    n_mb += 1
    try:
        tree = o.read_typetree()
    except Exception:
        n_err += 1; continue
    spr = tree.get("m_Sprite")
    if not isinstance(spr, dict) or "m_Color" not in tree: continue
    tex = sprite_tex.get(spr.get("m_PathID"), "")
    if spr.get("m_FileID") == 0 and tex in WANT:
        c = tree["m_Color"]
        go = tree.get("m_GameObject", {}).get("m_PathID")
        gname = ""
        try:
            gname = env.assets[0].objects[go].read().m_Name if False else ""
        except Exception:
            pass
        rows.append([o.path_id, tex, "%02X%02X%02X%02X" % tuple(round(c[k] * 255) for k in ("r", "g", "b", "a")), go])
print("monobehaviours", n_mb, "typetree errors", n_err, "matches", len(rows))
with open(OUT, "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f); w.writerow(["path_id", "texture", "color", "gameobject"]); w.writerows(rows)
from collections import Counter
for (t, c), n in Counter((r[1], r[2]) for r in rows).most_common(60):
    print(n, t, c)
