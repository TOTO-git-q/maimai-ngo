"""List what the stock background animation clips drive: every curve's target path, attribute and key count / duration.
Read-only. usage: python ngo_bg_clips.py <out.txt> [controller names...]"""
import os, sys
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

OUT = sys.argv[1]
CTRL = set(sys.argv[2:] or ["UI_CMN_BG", "UI_CMN_SubBG"])
DATA = r"D:\maimai\Package\Sinmai_Data"
env = UnityPy.load(os.path.join(DATA, "resources.assets"))
gen = TypeTreeGenerator("2018.4.7f1"); gen.load_local_dll_folder(os.path.join(DATA, "Managed")); env.typetree_generator = gen

# path hash -> path string, from every GameObject hierarchy (clips store paths as CRC32 hashes in m_ClipBindingConstant)
import zlib
names = {}
def collect(tr, prefix):
    for ch in tr.m_Children:
        try:
            c = ch.read(); go = c.m_GameObject.read()
            p = prefix + "/" + go.m_Name if prefix else go.m_Name
            names[zlib.crc32(p.encode("utf-8"))] = p
            collect(c, p)
        except Exception:
            pass
for o in env.objects:
    if o.type.name == "GameObject" and o.peek_name() in CTRL:
        go = o.read()
        for comp in go.m_Components:
            c = comp.component.deref() if hasattr(comp, "component") else None
            if c is not None and c.type.name in ("Transform", "RectTransform"):
                collect(c.read(), "")

lines = []
for o in env.objects:
    if o.type.name != "AnimatorController" or o.peek_name() not in CTRL: continue
    ctl = o.read()
    for cl in ctl.m_AnimationClips:
        clip = cl.read()
        t = clip.read_typetree() if hasattr(clip, "read_typetree") else None
        t = cl.deref().read_typetree()
        lines.append("== controller %s clip %s length?%s loop=%s" % (o.peek_name(), t["m_Name"], t.get("m_MuscleClip", {}).get("m_StopTime"),
                     t.get("m_MuscleClip", {}).get("m_LoopTime")))
        for b in t.get("m_ClipBindingConstant", {}).get("genericBindings", []):
            lines.append("  path=%s attr=%s typeID=%s customType=%s isPPtr=%s" % (names.get(b["path"], b["path"]), b["attribute"],
                         b.get("typeID"), b.get("customType"), b.get("isPPtrCurve")))
        for k in ("m_EditorCurves", "m_FloatCurves", "m_PositionCurves", "m_RotationCurves", "m_ScaleCurves", "m_EulerCurves", "m_PPtrCurves"):
            for c in t.get(k, []):
                keys = c.get("curve", {}).get("m_Curve", []) if "curve" in c else c.get("curve", [])
                lines.append("  %s path=%s attr=%s keys=%d" % (k, c.get("path"), c.get("attribute"), len(keys) if isinstance(keys, list) else -1))
open(OUT, "w", encoding="utf-8").write("\n".join(lines))
print("\n".join(lines[:120]))
