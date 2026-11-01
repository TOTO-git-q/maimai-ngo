"""Make the side tabs shorter without repainting: drop CUT rows from the plain middle of the body (rows below the
stepped top corners are identical), keep the bottom edge where it is, so the tab still sits on the same baseline.
Base_02 (white flash) is rebuilt as the new silhouette. Works on the stock-role pair from ngo_tab_side_roles.py.
usage: python ngo_tab_shorten.py <in dir> <out dir> <cut rows>"""
import os, sys
import numpy as np
from PIL import Image

src, out, cut = sys.argv[1], sys.argv[2], int(sys.argv[3])
os.makedirs(out, exist_ok=True)
a = np.asarray(Image.open(os.path.join(src, "UI_CMN_Tab_Base_01.png")).convert("RGBA"))
rows = np.where(a[..., 3].any(1))[0]
top, bottom = rows.min(), rows.max() + 1
plain0, best, start = top, 0, top                     # start of the longest run of identical rows
for r in range(top + 1, bottom + 1):
    if r == bottom or not np.array_equal(a[r], a[r - 1]):
        if r - start > best: plain0, best = start, r - start
        start = r
assert bottom - plain0 > cut + 4, "not enough plain rows to cut"
b = np.zeros_like(a)
b[top + cut:plain0 + cut] = a[top:plain0]             # stepped top, moved down
b[plain0 + cut:bottom] = a[plain0 + cut:bottom]       # remaining plain rows and the bottom stay put
Image.fromarray(b).save(os.path.join(out, "UI_CMN_Tab_Base_01.png"))
w = np.full_like(b, 255); w[..., 3] = b[..., 3]
Image.fromarray(w).save(os.path.join(out, "UI_CMN_Tab_Base_02.png"))
print("body rows", top, "-", bottom, "->", top + cut, "-", bottom, "(plain run from", plain0, ")")
