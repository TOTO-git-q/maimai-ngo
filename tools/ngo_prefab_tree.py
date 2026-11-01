"""Print a prefab subtree from resources.assets with each node's active flag, Image sprite, Animator controller and
Mask, to see how a screen is built before touching it.
usage: python ngo_prefab_tree.py <rect transform path id> [depth] [--up]   (--up: print the parent chain first)"""
import sys
import UnityPy

ROOT = int(sys.argv[1]); DEPTH = int(sys.argv[2]) if len(sys.argv) > 2 and sys.argv[2].isdigit() else 6
env = UnityPy.load(r"D:\maimai\Package\Sinmai_Data\resources.assets"); objs = env.file.objects


def comps(go):
    out = []
    for c in go["m_Component"]:
        co = objs.get(c["component"]["m_PathID"])
        if co is None: continue
        t = co.type.name
        if t == "MonoBehaviour":
            try:
                tt = co.read_typetree()
                if "m_Sprite" in tt:
                    sid = tt["m_Sprite"]["m_PathID"]
                    sp = objs.get(sid)
                    out.append("img:" + (sp.peek_name() if sp else ("ext%d" % sid if sid else "none")) + ("" if tt.get("m_Enabled", 1) else "(off)"))
                elif "m_ShowMaskGraphic" in tt: out.append("mask")
            except Exception: pass
        elif t == "Animator":
            tt = co.read_typetree(); ctl = objs.get(tt["m_Controller"]["m_PathID"])
            out.append("anim:" + (ctl.peek_name() if ctl else "?"))
    return out


def walk(pid, d):
    t = objs[pid].read_typetree(); go = objs[t["m_GameObject"]["m_PathID"]].read_typetree()
    print("  " * d + "%s [%d]%s pos(%.0f,%.0f) s(%.2f) %s" % (go["m_Name"], pid, "" if go["m_IsActive"] else " INACTIVE",
          t["m_LocalPosition"]["x"], t["m_LocalPosition"]["y"], t["m_LocalScale"]["x"], " ".join(comps(go))))
    if d < DEPTH:
        for c in t["m_Children"]: walk(c["m_PathID"], d + 1)


if "--up" in sys.argv:
    p, chain = ROOT, []
    while p:
        t = objs[p].read_typetree(); chain.append("%s[%d]" % (objs[t["m_GameObject"]["m_PathID"]].peek_name(), p)); p = t["m_Father"]["m_PathID"]
    print(" < ".join(chain))
walk(ROOT, 0)
