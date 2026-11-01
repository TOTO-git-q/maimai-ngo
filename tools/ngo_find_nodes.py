"""Find GameObjects by name in the game's resources.assets and print their RectTransform / Transform (position,
scale, pivot, size) and parent chain. usage: python ngo_find_nodes.py <name> [...]"""
import os, sys
import UnityPy

names = set(sys.argv[1:])
env = UnityPy.load(r"D:\maimai\Package\Sinmai_Data\resources.assets")
objs = env.file.objects
for o in env.objects:
    if o.type.name != "GameObject" or o.peek_name() not in names: continue
    go = o.read_typetree()
    for c in go["m_Component"]:
        co = objs.get(c["component"]["m_PathID"])
        if co is None or co.type.name not in ("RectTransform", "Transform"): continue
        t = co.read_typetree()
        chain, f = [], t["m_Father"]["m_PathID"]
        while f and f in objs and len(chain) < 8:
            ft = objs[f].read_typetree(); chain.append(objs[ft["m_GameObject"]["m_PathID"]].peek_name()); f = ft["m_Father"]["m_PathID"]
        print(o.peek_name(), "go", o.path_id, co.type.name, co.path_id,
              "pos", {k: round(t["m_LocalPosition"][k], 1) for k in "xy"}, "scale", {k: round(t["m_LocalScale"][k], 3) for k in "xy"},
              "pivot", t.get("m_Pivot"), "size", t.get("m_SizeDelta"), "parents", " < ".join(chain))
