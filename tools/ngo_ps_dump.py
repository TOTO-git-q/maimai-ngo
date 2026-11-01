"""Dump the typetrees of the main-menu bokeh particle systems (UI_CMN_BG/.../SM_v170_Hill_Setted/Particle System/*) and the
sub-screen ToneParticle systems, so their modules can be re-tuned for NGO floats. Read-only.
usage: python ngo_ps_dump.py <out.json>"""
import json, os, sys
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

DATA = r"D:\maimai\Package\Sinmai_Data"
env = UnityPy.load(os.path.join(DATA, "resources.assets"))
gen = TypeTreeGenerator("2018.4.7f1")
gen.load_local_dll_folder(os.path.join(DATA, "Managed"))
env.typetree_generator = gen


def comps(go_obj):
    for comp in go_obj.read().m_Components:
        yield (comp.component if hasattr(comp, "component") else comp).deref()


def children(go_obj):
    for c in comps(go_obj):
        if c.type.name in ("Transform", "RectTransform"):
            for ch in c.read().m_Children:
                yield ch.read().m_GameObject.deref()


def walk(go_obj, path):
    yield go_obj, path
    for ch in children(go_obj):
        yield from walk(ch, path + "/" + ch.peek_name())


out = {}
for root_name in ("UI_CMN_BG", "UI_CMN_SubBG"):
    root = next(o for o in env.objects if o.type.name == "GameObject" and o.peek_name() == root_name)
    for go_obj, path in walk(root, root_name):
        if not any(k in path for k in ("Particle System", "ToneParticle")): continue
        if any(k in path for k in ("Hold", "Release", "Tap")): continue
        entry = {}
        for c in comps(go_obj):
            if c.type.name in ("ParticleSystem", "ParticleSystemRenderer", "Transform"):
                entry[c.type.name] = c.read_typetree()
                entry[c.type.name + "_pathid"] = c.path_id
        entry["go_pathid"] = go_obj.path_id
        out[path] = entry
    break_after_first = True
with open(sys.argv[1], "w", encoding="utf-8") as f:
    json.dump(out, f, indent=1, ensure_ascii=False, default=str)
print("\n".join(out))
