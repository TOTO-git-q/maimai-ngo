"""Who uses the v170 background textures? texture -> materials (+shader) -> renderers / UI images -> GameObject path.
Reads the backup resources.assets (stock). usage: python ngo_bg_graph.py <out.txt>"""
import sys, collections
import UnityPy

env = UnityPy.load(r"D:\maimai\backup_ngo_skin\resources.assets")
objs = {o.path_id: o for o in env.objects}
tex = {pid: o.peek_name() for pid, o in objs.items() if o.type.name == "Texture2D" and "v170" in (o.peek_name() or "").lower()}
tex.update({pid: o.peek_name() for pid, o in objs.items() if o.type.name == "Texture2D" and o.peek_name() in ("T_UI_CloudV170", "T_UI_hillFloorV170")})

lines = []
mat_of_tex = collections.defaultdict(list)
shader_of_mat = {}
for pid, o in objs.items():
    if o.type.name != "Material": continue
    m = o.read()
    try: shader = m.m_Shader.read().m_ParsedForm.m_Name if m.m_Shader else ""
    except Exception: shader = "?"
    shader_of_mat[pid] = (m.m_Name, shader)
    for name, te in m.m_SavedProperties.m_TexEnvs:
        if te.m_Texture.m_PathID in tex:
            mat_of_tex[te.m_Texture.m_PathID].append((pid, name))

go_name = {}
parent = {}
def path_of(go_pid):
    parts, seen = [], set()
    pid = go_pid
    while pid and pid not in seen:
        seen.add(pid); parts.append(go_name.get(pid, "?")); pid = parent.get(pid)
    return "/".join(reversed(parts))
trans_go = {}
for pid, o in objs.items():
    if o.type.name == "GameObject": go_name[pid] = o.peek_name()
for pid, o in objs.items():
    if o.type.name in ("Transform", "RectTransform"):
        t = o.read()
        trans_go[pid] = t.m_GameObject.m_PathID
for pid, o in objs.items():
    if o.type.name in ("Transform", "RectTransform"):
        t = o.read()
        if t.m_Father and t.m_Father.m_PathID in trans_go:
            parent[t.m_GameObject.m_PathID] = trans_go[t.m_Father.m_PathID]

users = collections.defaultdict(list)
for pid, o in objs.items():
    if o.type.name in ("MeshRenderer", "SkinnedMeshRenderer", "ParticleSystemRenderer", "SpriteRenderer"):
        r = o.read()
        for mp in getattr(r, "m_Materials", []):
            users[mp.m_PathID].append((o.type.name, r.m_GameObject.m_PathID))

for tp, tname in sorted(tex.items(), key=lambda x: x[1]):
    lines.append("== " + tname)
    for mp, slot in mat_of_tex.get(tp, []):
        mname, sh = shader_of_mat[mp]
        lines.append("   material %s [%s] shader=%s" % (mname, slot, sh))
        for kind, gp in users.get(mp, [])[:6]:
            lines.append("      %s  %s" % (kind, path_of(gp)))
open(sys.argv[1], "w", encoding="utf-8").write("\n".join(lines))
print("\n".join(lines[:200]))
