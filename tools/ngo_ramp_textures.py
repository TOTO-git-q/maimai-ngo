"""Build the NGO-ramp versions of the select-screen textures from the installed redraws (theme_ngo_test), for
ngo_patch_assets.py. Two kinds:
- blue art (round arrow buttons, level plates, count bar): every blue pixel replaced by the NGO violet ramp;
- tinted panels the redraw colored lilac (Selector_Base_05/07): maimai multiplies them by a per-screen color, so a
  colored texture turns muddy. Base_07 goes back to white (the color comes from the serialized tint, now NGO);
  Base_05 (tinted near-white) gets the Windose button face #F7E1FB.
usage: python ngo_ramp_textures.py <out_dir>"""
import os, sys
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ngo_ramp

THEME = r"D:\maimai\Package\LocalAssets\PartnerLive2D\theme_ngo_test"
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)

RAMP = {"UI_CMN_Btn_01A": (-0.15, 0.75), "UI_MSS_Plate_05_01": (0.15, 0.95), "UI_MSS_BlueBar": (-0.1, 0.8)}
REFILL = {"UI_CMN_Selector_Base_07": "#FFFFFF", "UI_CMN_Selector_Base_05": "#F7E1FB"}


def load(name):
    return np.asarray(Image.open(os.path.join(THEME, name + ".png")).convert("RGBA")).astype(np.float32)


def save(name, a):
    Image.fromarray(a.clip(0, 255).astype(np.uint8), "RGBA").save(os.path.join(OUT, name + ".png"))


for name, (lo, hi) in RAMP.items():
    a = load(name)
    a[..., :3] = ngo_ramp.remap(a[..., :3], lo, hi)
    save(name, a)
for name, hexc in REFILL.items():
    a = load(name)
    body = a[..., 3] > 200
    base = a[body][:, :3].mean(0)
    target = np.array([int(hexc[i:i + 2], 16) for i in (1, 3, 5)], np.float32)
    a[..., :3] = a[..., :3] / base * target   # keep the redraw's own shading, swap its base color
    save(name, a)
print("wrote", len(RAMP) + len(REFILL), "->", OUT)
