"""Second blue sweep on the music-select screen, with the NGO ramps (ngo_ramp.py):
- game textures UI_NUM_Timer (countdown digits) and UI_UPE_Track_01 (TRACK n/4 label): blue -> violet ramp, written
  to <out_dir> for ngo_patch_assets.py; the red last-seconds digits (UI_NUM_Timer_Limit) stay red on purpose;
- the mod's own select buttons in LocalAssets\\PartnerLive2D\\select_sprites: back_flat*.png blue -> violet,
  next_flat*.png orange -> pink (forward stays the warm one). The originals are kept as <name>.orig.png.
usage: python ngo_recolor_blue2.py <out_dir>"""
import os, shutil, sys
import numpy as np
import UnityPy
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ngo_ramp

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
SELECT = r"D:\maimai\Package\LocalAssets\PartnerLive2D\select_sprites"
GAME = {"UI_NUM_Timer": (-0.45, 0.75), "UI_UPE_Track_01": (-0.4, 0.6)}   # kept dark: they sit on pink


def warm_mask(rgb):
    """orange / red pixels (hue < 45 or >= 330, saturation >= 0.15)"""
    mx, mn = rgb.max(-1), rgb.min(-1)
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1), 0)
    d = np.maximum(mx - mn, 1e-6)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    return (s >= 0.15) & ((h < 45) | (h >= 330))


def recolor(a, lo, hi, warm_to_pink=False):
    a = a.astype(np.float32)
    rgb = a[..., :3]
    if warm_to_pink:
        lum = (0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2]) / 255
        m = warm_mask(rgb)
        rgb[m] = ngo_ramp.ramp_at(ngo_ramp.PINK, lo + (hi - lo) * lum)[m]
    else:
        rgb[:] = ngo_ramp.remap(rgb, lo, hi)
    return Image.fromarray(a.clip(0, 255).astype(np.uint8), "RGBA")


env = UnityPy.load(r"D:\maimai\Package\Sinmai_Data\resources.assets")
for o in env.objects:
    if o.type.name == "Texture2D" and o.peek_name() in GAME:
        t = o.read()
        recolor(np.asarray(t.image.convert("RGBA")), *GAME[t.m_Name]).save(os.path.join(OUT, t.m_Name + ".png"))
        print("game", t.m_Name)

for f in sorted(os.listdir(SELECT)):
    if not f.endswith(".png") or f.endswith(".orig.png"): continue
    if not (f.startswith("back_flat") or f.startswith("next_flat")): continue
    src = os.path.join(SELECT, f)
    orig = src[:-4] + ".orig.png"
    if not os.path.exists(orig): shutil.copy2(src, orig)
    a = np.asarray(Image.open(orig).convert("RGBA"))
    img = recolor(a, -0.3, 0.8) if f.startswith("back") else recolor(a, -0.45, 0.65, warm_to_pink=True)
    img.save(os.path.join(OUT, "select_" + f))
    print("select", f)
