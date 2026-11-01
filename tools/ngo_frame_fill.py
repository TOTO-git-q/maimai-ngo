"""Give a painted frame back the stock's semi-opaque face: transparent pixels enclosed by the frame (not reachable from
the canvas edge) are filled with a colour at the stock face alpha. UI_CMN_Frame_01 stock centre is white a=204 and the
9-slice centre (border 68 on a 136 texture) stretches that seam pixel over every window that uses it.
usage: python ngo_frame_fill.py <in.png> <out.png> <#RRGGBB> <alpha 0-255>"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

src, dst, hexc, alpha = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4])
a = np.asarray(Image.open(src).convert("RGBA")).copy()
clear = a[..., 3] == 0
lab, _ = ndimage.label(clear)
edge = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
inside = clear & ~np.isin(lab, list(edge))
a[inside, :3] = [int(hexc[i:i + 2], 16) for i in (1, 3, 5)]
a[inside, 3] = alpha
Image.fromarray(a).save(dst)
print("filled", int(inside.sum()), "px")
