"""Put the tabset_v3 side-tab layers into the stock roles. In game the Tab_MiniBase/WhiteBase overlay is a faint white
highlight (stock: a light silhouette), so an outline painted there almost vanishes. Here: Base_01 = v3 body under the
v3 outline/bevel (tinted together; the plum outline stays dark under any tint), Base_02 = white silhouette of the body.
usage: python ngo_tab_side_roles.py <tabset_v3 replacements dir> <out dir>"""
import os, sys
import numpy as np
from PIL import Image

src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
body = Image.open(os.path.join(src, "UI_CMN_Tab_Base_01.png")).convert("RGBA")
over = Image.open(os.path.join(src, "UI_CMN_Tab_Base_02.png")).convert("RGBA")
a = np.asarray(Image.alpha_composite(body, over)).copy()
a[..., 3] = np.where(a[..., 3] > 127, 255, 0)
Image.fromarray(a).save(os.path.join(out, "UI_CMN_Tab_Base_01.png"))
w = np.full_like(a, 255); w[..., 3] = a[..., 3]
Image.fromarray(w).save(os.path.join(out, "UI_CMN_Tab_Base_02.png"))
print("side tab layers:", int((a[..., 3] > 0).sum()), "px")
