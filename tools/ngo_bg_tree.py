"""Dump the hierarchy of the standalone background prefabs (root GameObjects named UI_CMN_BG / SubBG-like) with each
node's components, materials and sprite/texture names. usage: python ngo_bg_tree.py <out.txt> root1,root2"""
import sys
import UnityPy

env = UnityPy.load(r"D:\maimai\backup_ngo_skin\resources.assets")
objs = {o.path_id: o for o in env.objects}
roots = sys.argv[2].split(",")
go_tr, children, comps = {}, {}, {}
for pid, o in objs.items():
    if o.type.name == "GameObject":
        g = o.read()
        comps[pid] = [c.component.m_PathID if hasattr(c, "component") else c[1].m_PathID for c in g.m_Component]
for pid, o in objs.items():
    if o.type.name in ("Transform", "RectTransform"):
        t = o.read()
        go_tr[t.m_GameObject.m_PathID] = pid
        children[pid] = [c.m_PathID for c in t.m_Children]
        if t.m_Father.m_PathID == 0: pass

def describe(cpid):
    o = objs.get(cpid)
    if o is None: return "?"
    k = o.type.name
    if k in ("Transform", "RectTransform"): return None
    try:
        c = o.read()
    except Exception:
        return k
    if k in ("MeshRenderer", "ParticleSystemRenderer", "SpriteRenderer"):
        names = []
        for m in c.m_Materials:
            mo = objs.get(m.m_PathID)
            names.append(mo.peek_name() if mo else "?")
        return "%s[%s]" % (k, ",".join(names))
    if k == "MonoBehaviour":
        try:
            script = c.m_Script.read().m_ClassName
        except Exception:
            script = "MB"
        extra = ""
        for f in ("m_Sprite", "m_Texture"):
            ref = getattr(c, f, None)
            if ref is not None and ref.m_PathID in objs:
                extra = "<" + objs[ref.m_PathID].peek_name() + ">"
        return script + extra
    if k == "Animator": return "Animator"
    return k

out = []
def walk(tr_pid, depth):
    t = objs[tr_pid].read()
    gpid = t.m_GameObject.m_PathID
    g = objs[gpid].read()
    descs = [d for d in (describe(c) for c in comps.get(gpid, [])) if d]
    out.append("  " * depth + "%s%s  %s" % (g.m_Name, "" if g.m_IsActive else " (inactive)", " ".join(descs)))
    for ch in children.get(tr_pid, []):
        walk(ch, depth + 1)

for pid, o in objs.items():
    if o.type.name != "GameObject" or o.peek_name() not in roots: continue
    tr = go_tr.get(pid)
    if tr is None: continue
    if objs[tr].read().m_Father.m_PathID != 0: continue
    out.append("##### root %s (go %d)" % (o.peek_name(), pid))
    walk(tr, 0)
open(sys.argv[1], "w", encoding="utf-8").write("\n".join(out))
print(len(out), "lines")
