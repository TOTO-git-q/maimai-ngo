"""Preview the NGO ramp recolor before writing anything: tinted panels (texture x serialized color) with the old hue
shift and with the NGO ramp, and textures whose own pixels are blue. Writes one sheet.
usage: python ngo_ramp_preview.py <out.png>"""
import colorsys, os, sys
import numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ngo_ramp

THEME = r"D:\maimai\Package\LocalAssets\PartnerLive2D\theme_ngo_test"
TINT_LO, TINT_HI = 0.2, 0.9


def old_tint(hexc):
    r, g, b = (int(hexc[i:i + 2], 16) / 255 for i in (0, 2, 4))
    h, s, v = colorsys.rgb_to_hsv(r, g, b)
    d = h * 360
    if s < 0.15: return np.array([r, g, b]) * 255
    if 175 <= d < 245: d = 262 + (d - 175) * 0.3
    elif 80 <= d < 175: d = 322 + (d - 80) * 0.15
    else: return np.array([r, g, b]) * 255
    return np.array(colorsys.hsv_to_rgb(d / 360, min(1, s * 0.9), v)) * 255


def new_tint(hexc):
    c = np.array([[int(hexc[i:i + 2], 16) for i in (0, 2, 4)]], np.float32)
    return ngo_ramp.remap(c, TINT_LO, TINT_HI)[0]


def tinted(tex, rgb):
    a = np.asarray(tex.convert("RGBA")).astype(np.float32)
    a[..., :3] *= rgb / 255
    return Image.fromarray(a.clip(0, 255).astype(np.uint8), "RGBA")


def recolor(tex, lo, hi):
    a = np.asarray(tex.convert("RGBA")).astype(np.float32)
    a[..., :3] = ngo_ramp.remap(a[..., :3], lo, hi)
    return Image.fromarray(a.clip(0, 255).astype(np.uint8), "RGBA")


panels = [("UI_CMN_Selector_Base_07", "2E94F4"), ("UI_CMN_Selector_Base_07", "0A87F0"), ("UI_CMN_Selector_Base_07", "43C143"),
          ("UI_CMN_Selector_Base_04", "0A87F0"), ("UI_MSS_TextBase", "447FFF"), ("UI_CMN_SelectorBase_02", "6891FF"),
          ("UI_CMN_SelectorBase_02", "7BE0B6"), ("UI_CMN_Selector_Ring_01", "43C143")]
# texture, ramp range (lo, hi)
textures = [("UI_CMN_Btn_01A", -0.15, 0.75), ("UI_MSS_Plate_05_01", 0.15, 0.95), ("UI_MSS_BlueBar", -0.1, 0.8)]

rows = []
for name, c in panels:
    t = Image.open(os.path.join(THEME, name + ".png"))
    rows.append((name + " x #" + c, [tinted(t, old_tint(c)), tinted(t, new_tint(c))]))
for name, lo, hi in textures:
    t = Image.open(os.path.join(THEME, name + ".png"))
    rows.append((name + " own pixels", [t, recolor(t, lo, hi)]))

H = 140
sheet = Image.new("RGB", (900, len(rows) * (H + 22)), (250, 245, 250))
d = ImageDraw.Draw(sheet)
d.text((300, 2), "now", fill=(0, 0, 0)); d.text((600, 2), "NGO ramp", fill=(0, 0, 0))
for i, (label, ims) in enumerate(rows):
    y = i * (H + 22) + 14
    d.text((4, y + H // 2), label, fill=(60, 30, 90))
    for j, im in enumerate(ims):
        im = im.copy(); im.thumbnail((260, H))
        sheet.paste(im, (300 + j * 300, y), im)
sheet.save(sys.argv[1])
for name, c in panels:
    print(name, c, "now #%02X%02X%02X" % tuple(int(v) for v in old_tint(c)), "new #%02X%02X%02X" % tuple(int(v) for v in new_tint(c)))
