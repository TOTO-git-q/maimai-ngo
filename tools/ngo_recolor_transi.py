"""Login transitions in NGO colors: v170transi (1080 x 1920, main) and Sub_01 (1080 x 452, sub: the green wipes with the
bear emblem / hat bears). Greens -> JINE pink ramp, blues/cyans -> Windose violet ramp (ngo_ramp.py), pushed dark
because the stock art is strong green on near-white; the near-white mint ground, too pale for the hue bands, is
pulled to NGO's light pink #FCE1F1. (Sub_02, the stock pink variant, already fits and is left alone.)
Writes <out_dir>/<name>.png for ngo_patch_assets.py.
usage: python ngo_recolor_transi.py <dir with stock <name>.png> <out_dir> [name ...]"""
import os, sys
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ngo_ramp

SRC, OUT = sys.argv[1], sys.argv[2]
NAMES = sys.argv[3:] or ["v170transi", "Sub_01"]
os.makedirs(OUT, exist_ok=True)
light = np.array([0xFC, 0xE1, 0xF1], np.float32)
for name in NAMES:
    a = np.asarray(Image.open(os.path.join(SRC, name + ".png")).convert("RGBA")).astype(np.float32)
    rgb = a[..., :3]
    mx, mn = rgb.max(-1), rgb.min(-1)
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1), 0)
    g_dom = (rgb[..., 1] >= rgb[..., 0]) & (rgb[..., 1] >= rgb[..., 2] - 8)
    pale_mint = (s < 0.15) & (mx > 200) & g_dom & (rgb[..., 1] - rgb[..., 0] > 4)
    out = ngo_ramp.remap(rgb, -0.45, 0.9)
    out[pale_mint] = (light * (rgb[pale_mint].mean(-1, keepdims=True) / 245.0)).clip(0, 255)
    a[..., :3] = out
    Image.fromarray(a.clip(0, 255).astype(np.uint8), "RGBA").save(os.path.join(OUT, name + ".png"))
    print("wrote", name)
