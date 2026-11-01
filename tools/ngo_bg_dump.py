"""Dump maimai's menu background prefabs (UI_CMN_BG / UI_CMN_SubBG and everything under them): hierarchy, transforms,
renderers with material / shader / texture, animators and their clips, particle systems and the MonoBehaviours with
their serialized fields. Read-only; for deciding how the NGO background replaces or reuses the stock one.
usage: python ngo_bg_dump.py <out.txt> [root names...]"""
import os, sys
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

OUT = sys.argv[1]
ROOTS = sys.argv[2:] or ["UI_CMN_BG", "UI_CMN_SubBG"]
DATA = r"D:\maimai\Package\Sinmai_Data"

env = UnityPy.load(os.path.join(DATA, "resources.assets"))
gen = TypeTreeGenerator("2018.4.7f1")
gen.load_local_dll_folder(os.path.join(DATA, "Managed"))
env.typetree_generator = gen

lines = []
p = lines.append


def short(v, n=160):
    s = repr(v)
    return s if len(s) <= n else s[:n] + "..."


def tex_names(mat):
    out = []
    try:
        for k, te in mat.m_SavedProperties.m_TexEnvs:
            if te.m_Texture and te.m_Texture.path_id:
                try:
                    t = te.m_Texture.read()
                    out.append("%s=%s(%dx%d) scale%s off%s" % (k, t.m_Name, t.m_Width, t.m_Height,
                               (round(te.m_Scale.x, 3), round(te.m_Scale.y, 3)), (round(te.m_Offset.x, 3), round(te.m_Offset.y, 3))))
                except Exception as e:
                    out.append("%s=<ext %s>" % (k, te.m_Texture.path_id))
    except Exception as e:
        out.append("<tex err %s>" % e)
    return out


def component_info(c, depth):
    ind = "  " * depth + "    - "
    tn = c.type.name
    try:
        if tn == "Transform" or tn == "RectTransform":
            t = c.read()
            p(ind + "%s pos%s scale%s" % (tn, tuple(round(v, 2) for v in (t.m_LocalPosition.x, t.m_LocalPosition.y, t.m_LocalPosition.z)),
                                         tuple(round(v, 2) for v in (t.m_LocalScale.x, t.m_LocalScale.y, t.m_LocalScale.z))))
        elif tn in ("MeshRenderer", "SpriteRenderer", "ParticleSystemRenderer", "SkinnedMeshRenderer"):
            r = c.read()
            mats = []
            for m in r.m_Materials:
                try:
                    mm = m.read()
                    sh = mm.m_Shader.read().m_ParsedForm.m_Name if mm.m_Shader else "?"
                    mats.append("%s [%s] %s" % (mm.m_Name, sh, "; ".join(tex_names(mm))))
                except Exception as e:
                    mats.append("<mat %s>" % e)
            extra = ""
            if tn == "SpriteRenderer":
                try:
                    s = r.m_Sprite.read(); extra = " sprite=%s" % s.m_Name
                except Exception: pass
            p(ind + "%s enabled=%s sortOrder=%s%s mats=%s" % (tn, getattr(r, "m_Enabled", "?"), getattr(r, "m_SortingOrder", "?"), extra, mats))
        elif tn == "MeshFilter":
            try: p(ind + "MeshFilter mesh=%s" % c.read().m_Mesh.read().m_Name)
            except Exception: p(ind + "MeshFilter")
        elif tn == "Animator":
            a = c.read()
            try:
                ctl = a.m_Controller.read()
                clips = []
                for cl in getattr(ctl, "m_AnimationClips", []):
                    try: clips.append(cl.read().m_Name)
                    except Exception: pass
                p(ind + "Animator controller=%s clips=%s" % (ctl.m_Name, clips))
            except Exception as e:
                p(ind + "Animator <%s>" % e)
        elif tn == "Animation":
            a = c.read()
            clips = []
            for cl in a.m_Animations:
                try: clips.append(cl.read().m_Name)
                except Exception: pass
            p(ind + "Animation clips=%s" % clips)
        elif tn == "ParticleSystem":
            tr = c.read_typetree()
            p(ind + "ParticleSystem duration=%s looping=%s maxParticles=%s" % (tr.get("lengthInSec"), tr.get("looping"),
                  tr.get("InitialModule", {}).get("maxNumParticles")))
        elif tn == "MonoBehaviour":
            tr = c.read_typetree()
            try: name = c.read().m_Script.read().m_ClassName
            except Exception: name = "?"
            fields = {k: v for k, v in tr.items() if not k.startswith("m_")}
            p(ind + "MB %s %s" % (name, short(fields, 400)))
        elif tn == "Camera":
            tr = c.read_typetree()
            p(ind + "Camera ortho=%s size=%s fov=%s near=%s far=%s depth=%s cull=%s clear=%s" % (
                tr.get("orthographic"), tr.get("orthographic size"), tr.get("field of view"), tr.get("near clip plane"),
                tr.get("far clip plane"), tr.get("m_Depth"), tr.get("m_CullingMask"), tr.get("m_ClearFlags")))
        else:
            p(ind + tn)
    except Exception as e:
        p(ind + "%s <err %s>" % (tn, e))


def walk(go_obj, depth):
    go = go_obj.read()
    p("  " * depth + "* %s  active=%s layer=%s" % (go.m_Name, go.m_IsActive, go.m_Layer))
    tr = None
    for comp in go.m_Components:
        try:
            c = comp.component if hasattr(comp, "component") else comp
            cobj = c.deref() if hasattr(c, "deref") else None
        except Exception:
            cobj = None
        if cobj is None:
            continue
        component_info(cobj, depth)
        if cobj.type.name in ("Transform", "RectTransform"): tr = cobj.read()
    if tr:
        for ch in tr.m_Children:
            try:
                walk(ch.read().m_GameObject.deref(), depth + 1)
            except Exception as e:
                p("  " * (depth + 1) + "<child err %s>" % e)


for o in env.objects:
    if o.type.name == "GameObject":
        try:
            if o.peek_name() in ROOTS:
                go = o.read()
                # only roots (no parent)
                p("=" * 80); walk(o, 0)
        except Exception as e:
            p("<root err %s>" % e)
open(OUT, "w", encoding="utf-8").write("\n".join(lines))
print(len(lines), "lines ->", OUT)
