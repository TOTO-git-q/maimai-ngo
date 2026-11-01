"""Take the soft "AI" gradients out of a generated background: build a small palette from NGO's own art (median cut
over the original sprites), then map the image onto it with ordered (Bayer) dithering, like NGO's pixel screens.
usage: python ngo_palette_quantize.py <out_dir> <colors> img1.png img2.png ...  (writes <name>_ngo.png + compare sheet)"""
import os, sys
import numpy as np
from PIL import Image

OUT, K, FILES = sys.argv[1], int(sys.argv[2]), sys.argv[3:]
REF = r"E:\claude workspace\NGO桌宠\素材源\桌面素材（手工重绘版）\重做素材\Sprite"
os.makedirs(OUT, exist_ok=True)

# palette source: NGO originals with the look we want (stream room, JINE sky, desktop, sky strips)
refs = ["bg_stream.png", "bg_stream_angel_lv1.png", "JINEBG.png", "FHDbg.png",
        "bg_side_bar_noon2.png", "bg_side_bar_evening2.png", "bg_side_bar_night2.png"]
px = []
for r in refs:
    p = os.path.join(REF, r)
    if not os.path.exists(p): continue
    im = Image.open(p).convert("RGB"); im.thumbnail((400, 400))
    px.append(np.asarray(im).reshape(-1, 3))
pool = Image.fromarray(np.concatenate(px)[None, :, :].astype(np.uint8), "RGB")
pal_img = pool.quantize(colors=K, method=Image.Quantize.MEDIANCUT)
pal = np.array(pal_img.getpalette()[:K * 3], dtype=np.float32).reshape(K, 3)

BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], dtype=np.float32) / 16 - 0.5

def quantize(img, strength=28.0, block=2):
    rgba = np.asarray(img.convert("RGBA")).astype(np.float32)
    h, w = rgba.shape[:2]
    # work on 2x2 pixel blocks so the dither reads as pixel art at cabinet size
    small = Image.fromarray(rgba.astype(np.uint8)).resize((w // block, h // block), Image.BOX)
    s = np.asarray(small).astype(np.float32)
    sh, sw = s.shape[:2]
    t = np.tile(BAYER, (sh // 4 + 1, sw // 4 + 1))[:sh, :sw, None]
    rgb = s[..., :3] + t * strength
    d = ((rgb[:, :, None, :] - pal[None, None, :, :]) ** 2).sum(-1)
    q = pal[d.argmin(-1)]
    out = np.concatenate([q, s[..., 3:]], -1).astype(np.uint8)
    return Image.fromarray(out, "RGBA").resize((w, h), Image.NEAREST)

for f in FILES:
    img = Image.open(f)
    q = quantize(img)
    name = os.path.splitext(os.path.basename(f))[0]
    q.save(os.path.join(OUT, name + "_ngo.png"))
    w = 540; a = img.convert("RGB").resize((w, round(img.height * w / img.width))); b = q.convert("RGB").resize(a.size, Image.NEAREST)
    sheet = Image.new("RGB", (w * 2 + 10, a.height), (60, 60, 60)); sheet.paste(a, (0, 0)); sheet.paste(b, (w + 10, 0))
    sheet.save(os.path.join(OUT, name + "_compare.png"))
swatch = Image.new("RGB", (K * 24, 24))
for i, c in enumerate(pal.astype(np.uint8)): swatch.paste(tuple(int(v) for v in c), (i * 24, 0, i * 24 + 24, 24))
swatch.save(os.path.join(OUT, "palette.png"))
print("palette", K, "colors ->", OUT)
