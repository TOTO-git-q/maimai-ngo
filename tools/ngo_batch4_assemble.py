"""Owner round 2026-10-31 (watched on the cabinet): textures from the Codex outputs, in the stock sizes.
- Card-login picture (the maimai cabinet -> Ame and KAngel): out/ngo_aime_touch/aime_touch.png, green keyed, fitted to
  784x456 for UI_ENT_AimeTouch_Image_01/_02 and UI_ENT_Credit_Image_01 (the three cabinet views use the one picture).
- Screen-change medallion and banners (the old head read as the bear): out/ngo_transi_head2 01_01, Sub_02, sub_03.
- Song-list scrollbar walker (the bear -> Ame): out/ngo_scroll_ame UI_MSS_Scrollbar_Parts_03/_04.
- Mode-select arrows come from ngo_mode_arrow.py (same out dir).
usage: python ngo_batch4_assemble.py <out dir>"""
import os, shutil, sys
import numpy as np
from PIL import Image
from scipy import ndimage

O = r"E:\cxref\maimai-ngo\out"
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)

# card-login picture: the green outside the capsule (connected to the border) becomes transparent
a = np.array(Image.open(os.path.join(O, "ngo_aime_touch", "aime_touch.png")).convert("RGB")).astype(int)
green = (a[..., 1] > 170) & (a[..., 0] < 120) & (a[..., 2] < 120)
lab, _ = ndimage.label(green)
edge = np.unique(np.r_[lab[0], lab[-1], lab[:, 0], lab[:, -1]]); edge = edge[edge != 0]
outside = ndimage.binary_dilation(np.isin(lab, edge), iterations=1)
rgba = np.dstack([a.astype(np.uint8), np.where(outside, 0, 255).astype(np.uint8)])
pic = Image.fromarray(rgba).resize((784, 456), Image.LANCZOS)
for name in ("UI_ENT_AimeTouch_Image_01", "UI_ENT_AimeTouch_Image_02", "UI_ENT_Credit_Image_01"):
    pic.save(os.path.join(OUT, name + ".png"))

for src, names in (("ngo_transi_head2", ("01_01", "Sub_02", "sub_03")),
                   ("ngo_scroll_ame", ("UI_MSS_Scrollbar_Parts_03", "UI_MSS_Scrollbar_Parts_04"))):
    for n in names:
        im = Image.open(os.path.join(O, src, n + ".png")).convert("RGBA")
        im.save(os.path.join(OUT, n + ".png"))
        print(n, im.size)
print("wrote", OUT)
