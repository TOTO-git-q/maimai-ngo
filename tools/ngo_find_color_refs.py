"""Find every serialized color equal to one of the given hex colors anywhere in resources.assets: MonoBehaviour fields
(any depth, incl. Color arrays in monitor components) and AnimationClip color curves. Read-only.
usage: python ngo_find_color_refs.py <assets> <hex1,hex2,...>"""
import sys
from collections import Counter
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
import os

SRC = sys.argv[1]
TARGETS = {h.upper(): tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) for h in sys.argv[2].split(",")}

env = UnityPy.load(SRC)
gen = TypeTreeGenerator("2018.4.7f1")
gen.load_local_dll_folder(r"D:\maimai\Package\Sinmai_Data\Managed")   # the backup folder has no Managed
env.typetree_generator = gen


def match(rgb):
    for h, t in TARGETS.items():
        if all(abs(a - b) <= 1 for a, b in zip(rgb, t)): return h
    return None


hits = Counter()
examples = {}


def walk(node, path, owner):
    if isinstance(node, dict):
        if set(node) >= {"r", "g", "b"} and all(isinstance(node[k], float) for k in "rgb"):
            h = match(tuple(round(node[k] * 255) for k in "rgb"))
            if h:
                key = (owner, path, h); hits[key] += 1; return
        for k, v in node.items(): walk(v, path + "." + k, owner)
    elif isinstance(node, list):
        for v in node[:2000]: walk(v, path + "[]", owner)


for o in env.objects:
    if o.type.name not in ("MonoBehaviour", "AnimationClip"): continue
    try:
        tree = o.read_typetree()
    except Exception:
        continue
    owner = o.type.name
    if owner == "MonoBehaviour":
        sc = tree.get("m_Script", {})
        try:
            owner = "MB:" + o.read().m_Script.read().m_ClassName
        except Exception:
            pass
    else:
        owner = "Clip:" + tree.get("m_Name", "")
        # color curves keep values as separate float curves; check constant keyframe values
        for cur in tree.get("m_FloatCurves", []) + tree.get("m_EditorCurves", []):
            pass
    walk(tree, "", owner)

for (owner, path, h), n in hits.most_common(80):
    print(n, h, owner, path[-80:])
