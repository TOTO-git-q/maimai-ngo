"""Second half of installing the NGO screen transition (run after ngo_patch_assets.py wrote the 9 redrawn textures from
E:\\cxref\\maimai-ngo\\out\\transi_v2\\replacements):
- tints: ChangeScreen_01 builds the main-screen wipe from white sprites tinted by their Image colour; each node gets
  the NGO colour from Codex's tints.json (alpha kept: the clips animate only m_Color.a);
- meshes: the 9 transition sprites get plain full-rect quads (stock meshes are traced around the bear chip, chevrons
  and fringes and would clip the new art).
Writes resources.assets.new; the caller copies it over.
usage: python ngo_transi_install.py"""
import os, struct
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

DATA = r"D:\maimai\Package\Sinmai_Data"
ASSETS = os.path.join(DATA, "resources.assets")
SPRITES = {"01_01", "UI_CMN_Parts_06_01", "UI_CMN_Parts_06_02", "UI_CMN_Parts_07_01", "UI_CMN_Parts_08_01",
           "UI_CMN_Parts_08_02", "Sub_01", "Sub_02", "sub_03"}
# node name (or prefix ending in *) under ChangeScreen_01 -> colour (tints.json, transi_v1/v2)
TINTS = {"01_01": "#4D23CF", "01_02": "#FFFFFF", "08_01": "#9E3C8C", "08_02": "#F7E1FB", "08_03_*": "#F7E1FB",
         "06_01_01": "#FFE36E", "06_01_02": "#7FD8F0", "06_01_03": "#D46CC4", "06_01_04": "#FFE36E",
         "06_02_01": "#7FD8F0", "06_02_02": "#FFE36E", "07_01_*": "#815CDD"}

env = UnityPy.load(ASSETS)
gen = TypeTreeGenerator("2018.4.7f1")
gen.load_local_dll_folder(os.path.join(DATA, "Managed"))
env.typetree_generator = gen
objs = env.file.objects
log = []


def comps(go):
    return [(c.component if hasattr(c, "component") else c).deref() for c in go.read().m_Components]


def tint_for(name):
    for k, v in TINTS.items():
        if name == k or (k.endswith("*") and name.startswith(k[:-1])):
            return v
    return None


def walk(go):
    yield go
    for c in comps(go):
        if c.type.name in ("Transform", "RectTransform"):
            for ch in c.read().m_Children:
                yield from walk(ch.read().m_GameObject.deref())


root = next(o for o in env.objects if o.type.name == "GameObject" and o.peek_name() == "ChangeScreen_01")
for go in walk(root):
    hexc = tint_for(go.peek_name())
    if hexc is None: continue
    for c in comps(go):
        if c.type.name != "MonoBehaviour": continue
        try:
            t = c.read_typetree()
        except Exception:
            continue
        if "m_Color" not in t: continue
        rgb = [int(hexc[i:i + 2], 16) / 255 for i in (1, 3, 5)]
        t["m_Color"].update({"r": rgb[0], "g": rgb[1], "b": rgb[2]})
        c.save_typetree(t)
        log.append("tint %s -> %s (a %.2f)" % (go.peek_name(), hexc, t["m_Color"]["a"]))

tex_size = {o.path_id: (o.read().m_Width, o.read().m_Height) for o in env.objects
            if o.type.name == "Texture2D" and o.peek_name() in SPRITES}
for o in env.objects:
    if o.type.name != "Sprite" or o.peek_name() not in SPRITES: continue
    t = o.read_typetree()
    rd, rect, ppu, piv = t["m_RD"], t["m_Rect"], t["m_PixelsToUnits"], t["m_Pivot"]
    TW, TH = tex_size[rd["texture"]["m_PathID"]]
    W, H = rect["width"], rect["height"]          # some stock rects are fractional (1023.99 x 109.61 on 1024 x 110)
    assert abs(W - TW) < 1 and abs(H - TH) < 1, (o.peek_name(), rect, TW, TH)
    x0, x1 = -piv["x"] * W / ppu, (1 - piv["x"]) * W / ppu
    y0, y1 = -piv["y"] * H / ppu, (1 - piv["y"]) * H / ppu
    pos = struct.pack("<12f", x0, y1, 0, x1, y0, 0, x1, y1, 0, x0, y0, 0)
    u1, v1 = W / TW, H / TH
    uv = struct.pack("<8f", 0, v1, u1, 0, u1, v1, 0, 0)
    as_field = lambda old, b: b if isinstance(old, (bytes, bytearray)) else list(b)
    vd = rd["m_VertexData"]
    vd["m_VertexCount"] = 4
    vd["m_DataSize"] = as_field(vd["m_DataSize"], pos + uv)
    rd["m_IndexBuffer"] = as_field(rd["m_IndexBuffer"], struct.pack("<6H", 3, 0, 1, 2, 1, 0))
    sm = rd["m_SubMeshes"][0]
    sm["indexCount"], sm["vertexCount"], sm["firstByte"], sm["baseVertex"], sm["firstVertex"] = 6, 4, 0, 0, 0
    rd["textureRect"] = {"x": 0.0, "y": 0.0, "width": float(W), "height": float(H)}
    rd["textureRectOffset"] = {"x": 0.0, "y": 0.0}
    o.save_typetree(t)
    log.append("sprite %s -> full quad %.0fx%.0f" % (o.peek_name(), W, H))

data = env.file.save()
assert len(data) > 60 * 1024 * 1024, "output unexpectedly small: %d bytes" % len(data)
with open(ASSETS + ".new", "wb") as f:
    f.write(data)
print("\n".join(log))
