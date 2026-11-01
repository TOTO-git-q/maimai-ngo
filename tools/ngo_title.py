"""Title screen (AdvertiseProcess logo page) for maimai NEEDY GIRL OVERDOSE, written straight into resources.assets.
Layout follows Astra's layout C (E:\\cxref\\maimai-ngo\\astra_title\\ADVICE.md), picked by the owner, logo a bit
bigger (LOGO_W) and with the pixel "maimai" mark above; layout A's plate is still available (PLATE):
- the MAGiCAL logo layers, its ruby, the disc and the white radial rays behind it, and both characters (Kana, Kuma)
  become transparent textures, so the stock intro animation keeps running but shows nothing of them (the stage
  above stays free for the later Live2D);
- the visible logo layer Magical_00 (texture UI_ADV_Logo_main_00) shows NGO's own logo (13_library_logo_small) 760 px
  wide on a pale stepped backing plate baked into the same texture (792 x 242 on screen, texture at 2x), centred
  (+10, -12) from the stock logo centre; Magical_00_eff, the flash copy on the same texture, gets the same box;
- the "maimai" mark (LogoDX, texture UI_ADV_Logo_maimaidx) sits centred just above the plate, MARK_W wide; with
  --mark <png> its texture is replaced by the NGO pixel mark (Codex out/maimai_mark_v2, owner picked B).
Mask_Logo_all (UI_ADV_Logo_main_02) masks a 67 % white shine layer: with a solid logo as the mask that white washes
the whole logo out, so the mask is blank.
Writes resources.assets.new; the caller copies it over.
usage: python ngo_title.py <ngo_logo.png> [--mark <maimai_mark.png>]"""
import os, struct, sys
import UnityPy
from UnityPy.enums import TextureFormat
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from PIL import Image, ImageDraw

LOGO_PNG = sys.argv[1]
MARK_PNG = sys.argv[sys.argv.index("--mark") + 1] if "--mark" in sys.argv else None
DATA = r"D:\maimai\Package\Sinmai_Data"
ASSETS = os.path.join(DATA, "resources.assets")
BLANK = ["UI_ADV_Chara_a_00", "UI_ADV_Chara_a_01", "UI_ADV_Chara_a_02", "UI_ADV_Chara_a_03", "UI_ADV_Chara_b_00",
         "UI_ADV_Chara_b_01", "UI_ADV_Logo_back_00", "UI_ADV_Logo_back_01", "UI_ADV_Logo_rubi", "UI_ADV_Logo_outline_00",
         "UI_ADV_Logo_outline_01", "UI_ADV_Logo_main_01", "UI_ADV_Logo_main_02", "UI_ADV_Logo_Eff_00"]
STOCK_CENTRE = (-10.3, -7.1)                       # Magical_00's anchored position in Null_Logo
# owner picked Astra's layout C (no backing plate), a little bigger than its 800 px
LOGO_W, PLATE = 840, None                          # logo width; plate size (screen px) or None for no plate
CENTRE = (STOCK_CENTRE[0] + 10, STOCK_CENTRE[1] - 4)
MARK_W = 280                                       # owner: the pixel mark (v2 B) a bit bigger than Astra's 200
MARK_GAP = 8                                       # px between the mark's bottom and the plate's top
PLATE_FILL, PLATE_EDGE = (0xFC, 0xEF, 0xFF, round(255 * 0.92)), (0x69, 0x31, 0x8F, round(255 * 0.86))

env = UnityPy.load(ASSETS)
gen = TypeTreeGenerator("2018.4.7f1")
gen.load_local_dll_folder(os.path.join(DATA, "Managed"))
env.typetree_generator = gen
log = []


def plate_with_logo(logo):
    """2x texture: stepped (pixel-cornered) pale plate with a 2 px plum edge, the logo centred on it (no plate when
    PLATE is None: just the logo at 2x LOGO_W)"""
    S = 2
    if PLATE is None:
        lw = LOGO_W * S
        return logo.resize((lw, round(lw * logo.height / logo.width)), Image.LANCZOS)
    W, H = PLATE[0] * S, PLATE[1] * S
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    step = 8 * S                                   # corner cut as two 8 px stair steps
    pts = [(2 * step, 0), (W - 2 * step, 0), (W - 2 * step, step), (W - step, step), (W - step, 2 * step), (W, 2 * step),
           (W, H - 2 * step), (W - step, H - 2 * step), (W - step, H - step), (W - 2 * step, H - step), (W - 2 * step, H),
           (2 * step, H), (2 * step, H - step), (step, H - step), (step, H - 2 * step), (0, H - 2 * step),
           (0, 2 * step), (step, 2 * step), (step, step), (2 * step, step)]
    d.polygon(pts, fill=PLATE_EDGE)
    inner = Image.new("L", (W, H), 0)
    ImageDraw.Draw(inner).polygon(pts, fill=255)
    inner = inner.resize((W - 4 * S, H - 4 * S), Image.NEAREST)
    fill = Image.new("RGBA", inner.size, PLATE_FILL)
    img.paste(fill, (2 * S, 2 * S), inner)
    lw = LOGO_W * S
    lg = logo.resize((lw, round(lw * logo.height / logo.width)), Image.LANCZOS)
    img.alpha_composite(lg, ((W - lg.width) // 2, (H - lg.height) // 2))
    return img


logo_img = plate_with_logo(Image.open(LOGO_PNG).convert("RGBA"))
BOX = PLATE or (logo_img.width // 2, logo_img.height // 2)      # on-screen size of Magical_00
mark_img = Image.open(MARK_PNG).convert("RGBA") if MARK_PNG else None
REPLACE = {"UI_ADV_Logo_main_00": logo_img}
if mark_img is not None: REPLACE["UI_ADV_Logo_maimaidx"] = mark_img
tex_ids = {}
for o in env.objects:
    if o.type.name != "Texture2D": continue
    n = o.peek_name()
    if n in BLANK or n in REPLACE:
        t = o.read()
        tex_ids[n] = o.path_id
        img = REPLACE.get(n) or Image.new("RGBA", (t.m_Width, t.m_Height), (0, 0, 0, 0))
        t.m_Width, t.m_Height = img.size
        t.set_image(img, target_format=TextureFormat.RGBA32)
        t.m_MipCount = 1
        t.save()
        log.append("texture %s <- %s %dx%d" % (n, "art" if n in REPLACE else "blank", img.width, img.height))
missing = (set(BLANK) | set(REPLACE)) - set(tex_ids)
assert not missing, missing

# replaced sprites: full rect of the new texture, plain quad (stock meshes are traced around the stock letters)
for o in env.objects:
    if o.type.name != "Sprite" or o.peek_name() not in REPLACE: continue
    t = o.read_typetree()
    name = o.peek_name()
    assert t["m_RD"]["texture"]["m_PathID"] == tex_ids[name], name + " sprite is not on its own texture"
    ppu, piv = t["m_PixelsToUnits"], t["m_Pivot"]
    W, H = REPLACE[name].size
    t["m_Rect"] = {"x": 0.0, "y": 0.0, "width": float(W), "height": float(H)}
    t["m_Offset"] = {"x": 0.0, "y": 0.0}
    rd = t["m_RD"]
    x0, x1 = -piv["x"] * W / ppu, (1 - piv["x"]) * W / ppu
    y0, y1 = -piv["y"] * H / ppu, (1 - piv["y"]) * H / ppu
    pos = struct.pack("<12f", x0, y1, 0, x1, y0, 0, x1, y1, 0, x0, y0, 0)
    uv = struct.pack("<8f", 0, 1, 1, 0, 1, 1, 0, 0)
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
    log.append("sprite %s -> %dx%d full quad" % (name, W, H))

# on-screen boxes (anchored position, size) in Null_Logo space
mark_h = round(MARK_W * (mark_img.height / mark_img.width if mark_img is not None else 60 / 248))
mark_dy = BOX[1] / 2 + MARK_GAP + mark_h / 2
BOXES = {"Magical_00": (CENTRE, BOX), "Magical_00_eff": (CENTRE, BOX),
         "LogoDX": ((CENTRE[0], CENTRE[1] + mark_dy), (MARK_W, mark_h))}
for o in env.objects:
    if o.type.name != "GameObject" or o.peek_name() not in BOXES: continue
    (px, py), (sw, sh) = BOXES[o.peek_name()]
    for c in o.read().m_Components:
        c = (c.component if hasattr(c, "component") else c).deref()
        if c.type.name != "RectTransform": continue
        t = c.read_typetree()
        t["m_AnchoredPosition"] = {"x": float(px), "y": float(py)}
        t["m_SizeDelta"] = {"x": float(sw), "y": float(sh)}
        c.save_typetree(t)
        log.append("%s -> pos (%.1f, %.1f) size %dx%d" % (o.peek_name(), px, py, sw, sh))

data = env.file.save()
assert len(data) > 60 * 1024 * 1024, "output unexpectedly small: %d bytes" % len(data)
with open(ASSETS + ".new", "wb") as f:
    f.write(data)
print("\n".join(log))
