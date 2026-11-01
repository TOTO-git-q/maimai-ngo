"""Pixelate stock textures (the owner: "干脆直接把官方那个像素化"): box-downscale by F, quantise to a small palette,
nearest-upscale back to the exact stock size, binary alpha. Atlases keep their sprite cells as long as cell sizes
are multiples of F (true for the level digit atlases at F=3/4).
usage: python ngo_pixelate.py <out dir> <F> <colours> name [name ...]   (names may use * wildcards)"""
import fnmatch, os, sys
import numpy as np
import UnityPy
from PIL import Image

OUT, F, K = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]); pats = sys.argv[4:]
os.makedirs(OUT, exist_ok=True)
env = UnityPy.load(r"D:\maimai\backup_ngo_skin\resources.assets")
for o in env.objects:
    if o.type.name != "Texture2D": continue
    n = o.peek_name()
    if not any(fnmatch.fnmatchcase(n, p) for p in pats): continue
    img = o.read().image.convert("RGBA"); W, H = img.size
    small = img.resize((max(1, W // F), max(1, H // F)), Image.BOX)
    a = np.array(small); alpha = a[..., 3] >= 110
    rgb = Image.fromarray(a[..., :3]).quantize(colors=K, method=Image.Quantize.MEDIANCUT).convert("RGB")
    out = np.dstack([np.array(rgb), np.where(alpha, 255, 0).astype(np.uint8)])
    Image.fromarray(out).resize((W, H), Image.NEAREST).save(os.path.join(OUT, n + ".png")); print(n, img.size)
