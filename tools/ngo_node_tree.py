"""Print the transform subtree under a RectTransform path id (name, path id, local position, scale, size).
usage: python ngo_node_tree.py <rect transform path id> [depth]"""
import sys
import UnityPy

ROOT = int(sys.argv[1]); DEPTH = int(sys.argv[2]) if len(sys.argv) > 2 else 4
env = UnityPy.load(r"D:\maimai\Package\Sinmai_Data\resources.assets"); objs = env.file.objects
def walk(pid, d):
    t = objs[pid].read_typetree(); name = objs[t["m_GameObject"]["m_PathID"]].peek_name()
    print("  " * d + "%s [%d] pos(%.0f,%.0f) scale(%.2f,%.2f) size%s" % (name, pid, t["m_LocalPosition"]["x"], t["m_LocalPosition"]["y"],
          t["m_LocalScale"]["x"], t["m_LocalScale"]["y"], (round(t["m_SizeDelta"]["x"]), round(t["m_SizeDelta"]["y"])) if "m_SizeDelta" in t else ""))
    if d < DEPTH:
        for c in t["m_Children"]: walk(c["m_PathID"], d + 1)
walk(ROOT, 0)
