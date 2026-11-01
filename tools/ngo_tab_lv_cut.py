"""Cut the owner-picked Codex art into the track-start card's deluxe tab and LV plate (all difficulties).
- tab: out/ngo_tab_lv_draw/tab_1.png (pink folder tab) -> UI_TST_MBase_<D>_Tab (420x96), fitted to the stock
  silhouette area (15,6)-(405,96): scaled to the height, then the plain middle of the body is stretched to the width
  (raised label and right end kept unstretched).
- LV: out/ngo_lv_draw2/lv_1.png (tiny popup window) -> UI_TST_MBase_LV_<D> (180x112), area (40,44)-(180,112), same
  3-slice; its title bar takes the difficulty colour of the approved play-screen plate (UI_UPE_MBase_<D>).
usage: python ngo_tab_lv_cut.py <out dir>"""
import os, sys
import cv2
import numpy as np
from PIL import Image

X = r"E:\cxref\maimai-ngo\out"
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)

def cut(path):
    a = np.array(Image.open(path).convert("RGB"))
    near = np.all(a > 238, axis=2).astype("uint8")
    _, lab, _, _ = cv2.connectedComponentsWithStats(near, 8)
    edge = np.unique(np.r_[lab[0], lab[-1], lab[:, 0], lab[:, -1]])
    alpha = np.where(np.isin(lab, edge[edge != 0]), 0, 255).astype("uint8")
    im = Image.fromarray(np.dstack([a, alpha])); return im.crop(im.getchannel("A").getbbox())

def fit3(src, box, keep_left, keep_right):
    """scale src to the box height, then stretch only the columns between keep_left and keep_right (fractions)"""
    x0, y0, x1, y1 = box; H = y1 - y0; W = x1 - x0
    k = H / src.height; s = src.resize((max(1, round(src.width * k)), H), Image.LANCZOS)
    l, r = round(s.width * keep_left), round(s.width * (1 - keep_right))
    mid = max(1, W - l - (s.width - r))
    out = Image.new("RGBA", (W, H))
    out.alpha_composite(s.crop((0, 0, l, H)), (0, 0))
    out.alpha_composite(s.crop((l, 0, r, H)).resize((mid, H), Image.LANCZOS), (l, 0))
    out.alpha_composite(s.crop((r, 0, s.width, H)), (l + mid, 0))
    a = np.array(out); a[..., 3] = np.where(a[..., 3] >= 128, 255, 0); return Image.fromarray(a)

def bar_colour(diff):
    u = np.array(Image.open(os.path.join(X, r"ngo_end_result\replacements\UI_UPE_MBase_%s.png" % diff)).convert("RGB"))
    return u[166, 400]   # inside the lower window's title bar

tab = cut(os.path.join(X, r"ngo_tab_lv_draw\tab_1.png"))
lv = cut(os.path.join(X, r"ngo_lv_draw2\lv_1.png"))
la = np.array(lv).astype(int)
bar_ref = la[round(lv.height * 0.12), round(lv.width * 0.3), :3]   # the lavender title bar
is_bar = (np.abs(la[..., :3] - bar_ref).sum(2) < 60) & (np.arange(lv.height)[:, None] < lv.height * 0.3)

tab_img = Image.new("RGBA", (420, 96)); tab_img.alpha_composite(fit3(tab, (15, 6, 405, 96), 0.45, 0.12), (15, 6))
for diff in ("BSC", "ADV", "EXP", "MST", "MST_Re", "UTG", "DMY"):
    tab_img.save(os.path.join(OUT, "UI_TST_MBase_%s_Tab.png" % diff))
    c = la.copy(); bc = bar_colour(diff)
    shade = la[..., :3].mean(2) / max(1, bar_ref.mean())      # keep the bar's own light/dark steps
    for i in range(3): c[..., i] = np.where(is_bar, np.clip(bc[i] * shade, 0, 255), c[..., i])
    lvd = Image.fromarray(c.astype(np.uint8))
    img = Image.new("RGBA", (180, 112)); img.alpha_composite(fit3(lvd, (40, 44, 180, 112), 0.25, 0.42), (40, 44))
    img.save(os.path.join(OUT, "UI_TST_MBase_LV_%s.png" % diff))
print("written", len(os.listdir(OUT)))
