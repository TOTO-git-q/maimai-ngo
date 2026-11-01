"""Inspect the stock background sky materials and the textures the NGO background will reuse: material floats /
colors / texture slots, and each texture's size, format and the sprites cut from it. Read-only.
usage: python ngo_bg_mats.py"""
import os
import UnityPy

DATA = r"D:\maimai\Package\Sinmai_Data"
MATS = {"M_v170BG_BGSky", "M_v170BG_BGSkySub", "M_v170Sprite_Emitt_Rotate", "M_v170SubBG_Ripple", "M_v170BG_BokehHill01"}
TEX = {"T_v170SubBG_Pattern", "T_v170BG_Rainbow", "T_v170BG_Cloud", "T_v170SubBG_ToneSymbol", "T_v170SubBG", "T_v170BG_CatcleEffect01"} | {
    "T_v170SubBG_Onpu%02d" % i for i in range(1, 10)}

env = UnityPy.load(os.path.join(DATA, "resources.assets"))
sprites = {}
for o in env.objects:
    if o.type.name == "Sprite":
        try:
            s = o.read(); t = s.m_RD.texture.read().m_Name
            if t in TEX: sprites.setdefault(t, []).append((s.m_Name, s.m_Rect.width, s.m_Rect.height, s.m_PixelsToUnits,
                                                           (s.m_RD.textureRect.width, s.m_RD.textureRect.height)))
        except Exception:
            pass
for o in env.objects:
    if o.type.name == "Material" and o.peek_name() in MATS:
        t = o.read_typetree()
        sp = t["m_SavedProperties"]
        print("==", t["m_Name"], "keywords", t.get("m_ShaderKeywords"), "queue", t.get("m_CustomRenderQueue"))
        print("   floats", [(k, round(v, 3)) for k, v in sp["m_Floats"]])
        print("   colors", [(k, tuple(round(c[x], 3) for x in "rgba")) for k, c in sp["m_Colors"]])
        print("   texs", [(k, v["m_Texture"]["m_PathID"]) for k, v in sp["m_TexEnvs"]])
for o in env.objects:
    if o.type.name == "Texture2D" and o.peek_name() in TEX:
        t = o.read()
        print("TEX", t.m_Name, t.m_Width, t.m_Height, t.m_TextureFormat, "wrap", t.m_TextureSettings.m_WrapMode, "sprites", sprites.get(t.m_Name))
