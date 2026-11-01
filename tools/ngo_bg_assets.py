"""Install the NGO menu background straight into Sinmai_Data\\resources.assets (no runtime mod), reusing maimai's own
background objects so their built-in motion keeps working:
- sub screen: the full-strip sprite UI_CMN_SubBG/.../Adjust/BG (shipped inactive) is switched on and shows our baked
  strip (T_v170SubBG); the procedural sky, five lines and pattern are switched off; the 9 ToneKigou note sprites and
  the ToneSymbol keep their Loop animation but show our float icons (T_v170SubBG_Onpu01..09, T_v170SubBG_ToneSymbol);
- main screen: castle / lake / islands / hill / rainbow circles are switched off; BG_Sky's material is moved from the
  procedural v170BG/BGSky shader to Sprites/Default and shows our baked main image through T_v170BG_Rainbow
  (re-sized to the image; its only other user, the lake, is switched off).
Writes resources.assets.new; the caller copies it over (backup: D:\\maimai\\backup_ngo_skin).
usage: python ngo_bg_assets.py <main.png 1080x1080> <sub.png 1078x450> <floats_dir with onpu01..09.png, symbol.png>"""
import os, sys
import UnityPy
from UnityPy.enums import TextureFormat
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from PIL import Image

MAIN_PNG, SUB_PNG, FLOATS = sys.argv[1:4]
DATA = r"D:\maimai\Package\Sinmai_Data"
ASSETS = os.path.join(DATA, "resources.assets")
OFF_MAIN = {"RainbowCircleS", "RainbowCircleM", "Catsle_Area", "SM_v170_Hill_Setted"}
OFF_SUB = {"Sky", "GameObject", "Pattern"}   # children of Adjust: procedural sky, five-line holder, pattern
ON_SUB = {"BG"}

env = UnityPy.load(ASSETS)
gen = TypeTreeGenerator("2018.4.7f1")
gen.load_local_dll_folder(os.path.join(DATA, "Managed"))
env.typetree_generator = gen
objs = {o.path_id: o for o in env.objects}


def children(go_obj):
    go = go_obj.read()
    for comp in go.m_Components:
        c = (comp.component if hasattr(comp, "component") else comp).deref()   # this UnityPy gives plain PPtrs
        if c.type.name in ("Transform", "RectTransform"):
            for ch in c.read().m_Children:
                yield ch.read().m_GameObject.deref()


def find(root, path):
    cur = root
    for name in path:
        cur = next((c for c in children(cur) if c.peek_name() == name), None)
        if cur is None: return None
    return cur


def set_active(go_obj, active, log):
    t = go_obj.read_typetree()
    if bool(t["m_IsActive"]) != active:
        t["m_IsActive"] = active
        go_obj.save_typetree(t)
        log.append("%s active -> %s" % (t["m_Name"], active))


log = []
roots_main = [o for o in env.objects if o.type.name == "GameObject" and o.peek_name() == "UI_CMN_BG"]
roots_sub = [o for o in env.objects if o.type.name == "GameObject" and o.peek_name() == "UI_CMN_SubBG"]
sky_renderers = []
for r in roots_main:
    adj = find(r, ["Adjust_WorldPos"])
    if adj is None: log.append("skip a UI_CMN_BG without Adjust_WorldPos"); continue
    for c in children(adj):
        if c.peek_name() in OFF_MAIN: set_active(c, False, log)
        if c.peek_name() == "BG_Sky": sky_renderers.append(c)
for r in roots_sub:
    adj = find(r, ["BG", "Null_all", "Adjust"])
    if adj is None: log.append("skip a UI_CMN_SubBG without BG/Null_all/Adjust"); continue
    for c in children(adj):
        n = c.peek_name()
        if n in OFF_SUB: set_active(c, False, log)
        if n in ON_SUB: set_active(c, True, log)

# BG_Sky material -> Sprites/Default with our texture
# Sprites/Default lives in the built-in extra resources (path id 10753 there); point at it through this file's externals
ext = [e.path.lower() for e in env.file.externals]
sprites_default_shader = {"m_FileID": ext.index("resources/unity_builtin_extra") + 1, "m_PathID": 10753}
tex_by_name = {}
for o in env.objects:
    if o.type.name == "Texture2D":
        n = o.peek_name()
        if n in ("T_v170BG_Rainbow", "T_v170SubBG", "T_v170SubBG_ToneSymbol") or n.startswith("T_v170SubBG_Onpu"):
            tex_by_name.setdefault(n, o)
rainbow = tex_by_name["T_v170BG_Rainbow"]
done_mats = set()
for go_obj in sky_renderers:
    for comp in go_obj.read().m_Components:
        c = (comp.component if hasattr(comp, "component") else comp).deref()
        if c.type.name != "MeshRenderer": continue
        for mref in c.read().m_Materials:
            m = mref.deref()
            if m.path_id in done_mats: continue
            done_mats.add(m.path_id)
            t = m.read_typetree()
            t["m_Shader"] = sprites_default_shader
            sp = t["m_SavedProperties"]
            sp["m_TexEnvs"] = [e for e in sp["m_TexEnvs"] if e[0] != "_MainTex"] + [
                ("_MainTex", {"m_Texture": {"m_FileID": 0, "m_PathID": rainbow.path_id}, "m_Scale": {"x": 1.0, "y": 1.0}, "m_Offset": {"x": 0.0, "y": 0.0}})]
            cols = dict(sp["m_Colors"])
            cols["_Color"] = {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0}
            cols["_RendererColor"] = {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0}
            sp["m_Colors"] = list(cols.items())
            t["m_ShaderKeywords"] = ""
            t["m_CustomRenderQueue"] = -1
            m.save_typetree(t)
            log.append("material %s -> Sprites/Default + T_v170BG_Rainbow" % t["m_Name"])


# BG_Sky shows the texture rotated 180 degrees and 1 world unit = 1 screen px; its centre sits SKY_SHIFT px above the
# main circle's centre (measured with a calibration grid), so the plane is moved down by that much
SKY_SHIFT = 410.0
for go_obj in sky_renderers:
    for comp in go_obj.read().m_Components:
        c = (comp.component if hasattr(comp, "component") else comp).deref()
        if c.type.name != "Transform": continue
        t = c.read_typetree()
        if abs(t["m_LocalPosition"]["y"] - (-3.15)) < 0.01:
            t["m_LocalPosition"]["y"] -= SKY_SHIFT
            c.save_typetree(t)
            log.append("BG_Sky local y -> %.2f" % t["m_LocalPosition"]["y"])


def put_texture(obj, img, allow_resize=False):
    t = obj.read()
    if not allow_resize: assert img.size == (t.m_Width, t.m_Height), (t.m_Name, img.size, (t.m_Width, t.m_Height))
    fmt = TextureFormat(t.m_TextureFormat)
    if allow_resize and fmt not in (TextureFormat.RGBA32,): fmt = TextureFormat.RGBA32
    t.m_Width, t.m_Height = img.size
    t.set_image(img.convert("RGBA"), target_format=fmt)
    t.m_MipCount = 1
    t.save()
    log.append("texture %s <- %dx%d %s" % (t.m_Name, img.width, img.height, fmt.name))


put_texture(rainbow, Image.open(MAIN_PNG).rotate(180), allow_resize=True)
put_texture(tex_by_name["T_v170SubBG"], Image.open(SUB_PNG))
for i in range(1, 10):
    put_texture(tex_by_name["T_v170SubBG_Onpu%02d" % i], Image.open(os.path.join(FLOATS, "onpu%02d.png" % i)))
put_texture(tex_by_name["T_v170SubBG_ToneSymbol"], Image.open(os.path.join(FLOATS, "symbol.png")))

# sprites cut from the replaced float textures: open their rects to the full texture so nothing is clipped
for o in env.objects:
    if o.type.name != "Sprite": continue
    n = o.peek_name()
    if not (n.startswith("T_v170SubBG_Onpu") or n in ("T_v170SubBG_ToneSymbol", "T_v170SubBG")): continue
    s = o.read(); r, rd = s.m_Rect, s.m_RD
    if (rd.textureRect.width, rd.textureRect.height) != (r.width, r.height):
        rd.textureRect.x = rd.textureRect.y = 0
        rd.textureRect.width, rd.textureRect.height = r.width, r.height
        rd.textureRectOffset.x = rd.textureRectOffset.y = 0
        s.save(); log.append("sprite %s rect opened" % n)

data = env.file.save()
assert len(data) > 60 * 1024 * 1024, "output unexpectedly small: %d bytes" % len(data)
with open(ASSETS + ".new", "wb") as f:
    f.write(data)
print("\n".join(log))
