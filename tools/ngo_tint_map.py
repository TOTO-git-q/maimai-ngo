"""Apply a per-texture tint map to the UI Images that tint white-mask textures (their serialized m_Color).
tints.json: {"<texture>": {"#RRGGBB current": "#RRGGBB new", ...}, ...}; alpha is kept. Images whose colour is not in
the map are listed and left alone. Writes resources.assets.new; the caller copies it over (ngo_install_new.ps1).
usage: python ngo_tint_map.py <tints.json>"""
import json, os, sys
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

MAP = {t: {k.upper(): v.upper() for k, v in m.items()} for t, m in json.load(open(sys.argv[1], encoding="utf-8")).items()}
ASSETS = r"D:\maimai\Package\Sinmai_Data\resources.assets"
env = UnityPy.load(ASSETS)
gen = TypeTreeGenerator("2018.4.7f1"); gen.load_local_dll_folder(os.path.join(os.path.dirname(ASSETS), "Managed"))
env.typetree_generator = gen
objs = env.file.objects

sprite_tex = {}
for o in env.objects:
    if o.type.name == "Sprite":
        try:
            sprite_tex[o.path_id] = o.read().m_RD.texture.read().m_Name
        except Exception:
            pass

hexof = lambda c: "#%02X%02X%02X" % tuple(round(c[k] * 255) for k in "rgb")
done = skipped = 0
for o in env.objects:
    if o.type.name != "MonoBehaviour": continue
    try:
        t = o.read_typetree()
    except Exception:
        continue
    sp = t.get("m_Sprite")
    if not sp or sp.get("m_FileID") != 0: continue
    tex = sprite_tex.get(sp.get("m_PathID"))
    if tex not in MAP or "m_Color" not in t: continue
    cur = hexof(t["m_Color"]); new = MAP[tex].get(cur)
    who = objs[t["m_GameObject"]["m_PathID"]].peek_name()
    if not new:
        print("unmapped", tex, who, cur); skipped += 1; continue
    t["m_Color"].update(r=int(new[1:3], 16) / 255, g=int(new[3:5], 16) / 255, b=int(new[5:7], 16) / 255)
    o.save_typetree(t); done += 1
open(ASSETS + ".new", "wb").write(env.file.save())
print("tinted", done, "| unmapped", skipped)
