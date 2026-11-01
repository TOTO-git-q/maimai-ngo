"""Find which materials / sprites in resources.assets reference the given textures, and whether any other serialized
file in Sinmai_Data lists resources.assets as an external (if none does, an in-file scan is complete). Read-only.
usage: python ngo_tex_refs.py <texture name> [...]"""
import os, sys
import UnityPy

DATA = r"D:\maimai\Package\Sinmai_Data"
names = set(sys.argv[1:])
env = UnityPy.load(os.path.join(DATA, "resources.assets"))
tex_ids = {o.path_id: o.peek_name() for o in env.objects if o.type.name == "Texture2D" and o.peek_name() in names}
print("textures:", tex_ids)
mat_users = {}
for o in env.objects:
    if o.type.name == "Material":
        t = o.read_typetree()
        for slot, env_ in t["m_SavedProperties"]["m_TexEnvs"]:
            pid = env_["m_Texture"]["m_PathID"]
            if env_["m_Texture"]["m_FileID"] == 0 and pid in tex_ids:
                mat_users.setdefault(o.path_id, []).append((t["m_Name"], slot, tex_ids[pid]))
    elif o.type.name == "Sprite":
        s = o.read()
        if s.m_RD.texture.m_FileID == 0 and s.m_RD.texture.m_PathID in tex_ids:
            print("sprite", o.peek_name(), "->", tex_ids[s.m_RD.texture.m_PathID])
for pid, v in mat_users.items():
    print("material", pid, v)
# renderers using those materials
for o in env.objects:
    if o.type.name in ("MeshRenderer", "SpriteRenderer", "ParticleSystemRenderer"):
        r = o.read_typetree()
        hit = [m["m_PathID"] for m in r["m_Materials"] if m["m_FileID"] == 0 and m["m_PathID"] in mat_users]
        if hit:
            go = env.files  # placeholder
            print("renderer", o.type.name, o.path_id, "go", r["m_GameObject"]["m_PathID"], "mats", hit)
# other files that reference resources.assets
for f in sorted(os.listdir(DATA)):
    p = os.path.join(DATA, f)
    if not os.path.isfile(p) or f == "resources.assets" or f.endswith(".resS") or f.endswith(".resource"): continue
    if not (f.startswith("level") or f.startswith("sharedassets") or f == "globalgamemanagers" or f == "globalgamemanagers.assets"): continue
    try:
        e2 = UnityPy.load(p)
        for sf in e2.files.values():
            ext = [x.path for x in getattr(sf, "externals", [])]
            if any("resources.assets" in x for x in ext):
                print("EXTERNAL REF from", f)
    except Exception as ex:
        print("skip", f, repr(ex)[:80])
