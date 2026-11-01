"""Resolve the Loop clip bindings of UI_CMN_BG / UI_CMN_SubBG to transform paths (binding path = crc32 of the path
relative to the Animator) and list which attribute kinds each animated node gets. Read-only.
usage: python ngo_bg_clip_paths.py"""
import os, zlib, collections
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

DATA = r"D:\maimai\Package\Sinmai_Data"
env = UnityPy.load(os.path.join(DATA, "resources.assets"))
gen = TypeTreeGenerator("2018.4.7f1")
gen.load_local_dll_folder(os.path.join(DATA, "Managed"))
env.typetree_generator = gen
objs = env.file.objects
ATTR = {1: "pos", 2: "rot", 3: "scale", 4: "euler"}     # transform bindings: typeID 4, attribute = kind


def comps(go):
    return [(c.component if hasattr(c, "component") else c).deref() for c in go.read().m_Components]


def walk(go, path, out):
    out[zlib.crc32(path.encode()) & 0xFFFFFFFF] = path
    for c in comps(go):
        if c.type.name in ("Transform", "RectTransform"):
            for ch in c.read().m_Children:
                g = ch.read().m_GameObject.deref()
                walk(g, (path + "/" if path else "") + g.peek_name(), out)


for root_name in ("UI_CMN_BG", "UI_CMN_SubBG"):
    root = next(o for o in env.objects if o.type.name == "GameObject" and o.peek_name() == root_name)
    paths = {}
    walk(root, "", paths)
    anim = next(c for c in comps(root) if c.type.name == "Animator").read()
    ctrl = anim.m_Controller.deref().read()
    for h, s in getattr(ctrl, "m_TOS", []):       # the controller's own hash -> path table
        paths.setdefault(h, s)
    print(root_name, "clips", [c.deref().read().m_Name for c in ctrl.m_AnimationClips])
    for clip_ptr in ctrl.m_AnimationClips:
        clip = clip_ptr.deref().read()
        if clip.m_Name != "Loop": continue
        kinds = collections.defaultdict(set)
        for b in clip.m_ClipBindingConstant.genericBindings:
            what = ATTR.get(b.attribute, "attr%d" % b.attribute) if b.typeID == 4 else "type%d" % b.typeID
            kinds[paths.get(b.path, "?%d" % b.path)].add(what)
        print("==", root_name, clip.m_Name)
        for p in sorted(kinds):
            print("  ", p, sorted(kinds[p]))
