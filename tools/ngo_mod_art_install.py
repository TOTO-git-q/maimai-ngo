"""Copy the mod-loaded NGO art into LocalAssets/PartnerLive2D (the game reads these at runtime, no asset patching):
- ngo_transi_kangel/: KAngel's screen-change medallion (out/ngo_transi_kangel/01_01) and her KAngel-style banners
  (out/ngo_transi_kangel3: Sub_01, Sub_02, sub_03), used by NgoTransitionVariant when the roll picks her;
- ngo_upload/upload_scene.png: the server-upload parade (out/ngo_upload_scene), green keyed, used by NgoUploadScene;
- ngo_ame_walk/frame_1..4.png: Ame's walk cycle (out/ngo_ame_walk), green keyed, used by NgoTrackStartWalker.
usage: python ngo_mod_art_install.py"""
import os, shutil
import numpy as np
from PIL import Image
from scipy import ndimage

O = r"E:\cxref\maimai-ngo\out"
DST = r"D:\maimai\Package\LocalAssets\PartnerLive2D"

k = os.path.join(DST, "ngo_transi_kangel"); os.makedirs(k, exist_ok=True)
shutil.copy(os.path.join(O, "ngo_transi_kangel", "01_01.png"), k)
for n in ("Sub_01", "Sub_02", "sub_03"):
    im = Image.open(os.path.join(O, "ngo_transi_kangel3", n + ".png")).convert("RGBA")
    assert im.size == (1080, 452), (n, im.size)
    im.save(os.path.join(k, n + ".png"))

def keyed(path):
    a = np.array(Image.open(path).convert("RGB")).astype(int)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    green = (g > 170) & (r < 130) & (b < 130)
    clear = green | (((g > r + 50) & (g > b + 50)) & ndimage.binary_dilation(green, iterations=2))
    return Image.fromarray(np.dstack([a.astype(np.uint8), np.where(clear, 0, 255).astype(np.uint8)]))


im = keyed(os.path.join(O, "ngo_upload_scene", "upload_scene.png"))
im = im.crop(im.getchannel("A").getbbox())
u = os.path.join(DST, "ngo_upload"); os.makedirs(u, exist_ok=True)
im.save(os.path.join(u, "upload_scene.png"))

# the walk frames keep their common canvas (same size, same ground line), so they do not jitter when swapped
w = os.path.join(DST, "ngo_ame_walk"); os.makedirs(w, exist_ok=True)
frames = [keyed(os.path.join(O, "ngo_ame_walk", "frame_%d.png" % i)) for i in range(1, 5)]
boxes = [f.getchannel("A").getbbox() for f in frames]
box = (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))
for i, f in enumerate(frames, 1): f.crop(box).save(os.path.join(w, "frame_%d.png" % i))
print("kangel:", sorted(os.listdir(k)), "upload:", im.size, "walk:", box)
