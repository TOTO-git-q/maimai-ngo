"""Round arrow button (UI_CMN_Btn_01A 158x158 disc + UI_CMN_Arrow 128x128 triangle) in the song-frame pixel look:
stepped disc on a 4 px grid, deep plum outline, flat NGO violet face, a white pixel highlight arc; chunky white
triangle with a plum outline and a one-unit drop shadow. Same sizes, centres and pivots as the stock.
usage: python ngo_circle_button.py <out dir> [preview.png]"""
import os, sys
import numpy as np
from PIL import Image
from scipy import ndimage

P = 4
PLUM, VIOLET, VIOLET_DK, LIGHT = (58, 26, 140), (129, 92, 221), (98, 62, 190), (190, 168, 245)


def snap(m):
    h, w = m.shape
    a = np.pad(m, ((0, -h % P), (0, -w % P)))
    s = a.reshape(a.shape[0] // P, P, a.shape[1] // P, P).mean(axis=(1, 3)) > .5
    return np.kron(s, np.ones((P, P), bool))[:h, :w]


def disc(size=158, r=74):
    yy, xx = np.mgrid[0:size, 0:size]; c = (size - 1) / 2
    d = np.hypot(yy - c, xx - c)
    body = snap(d <= r)
    inner = ndimage.binary_erosion(body, np.ones((2 * P + 1, 2 * P + 1)))
    img = np.zeros((size, size, 4), np.uint8)
    img[body] = PLUM + (255,)
    img[inner] = VIOLET + (255,)
    lower = inner & snap((d <= r - 2 * P) & (yy > c + r * .35))           # darker lower band: flat two-tone
    img[lower] = VIOLET_DK + (255,)
    ring = inner & ~ndimage.binary_erosion(inner, np.ones((P + 1, P + 1)))
    img[ring & (yy < c)] = LIGHT + (255,)                                  # light rim on the upper half
    ang = np.degrees(np.arctan2(yy - c, xx - c))
    arc = snap((np.abs(d - (r - 4 * P)) < P * .75) & (ang > -150) & (ang < -110))
    img[arc & inner] = (255, 255, 255, 255)                                # pixel highlight
    img[snap((np.hypot(yy - (c - r * .62), xx - (c + r * .38)) < P * 1.2)) & inner] = (255, 255, 255, 255)
    return Image.fromarray(img)


def arrow(size=128):
    # right-pointing triangle, centred like the stock glyph (stock bbox ~ x 38..100, y 30..98)
    yy, xx = np.mgrid[0:size, 0:size]
    x0, x1, cy, hh = 40, 98, 64, 34
    tri = (xx >= x0) & (xx <= x1) & (np.abs(yy - cy) <= hh * (x1 - xx) / (x1 - x0))
    tri = snap(tri)
    out = ndimage.binary_dilation(tri, np.ones((2 * P + 1, 2 * P + 1)))
    shadow = np.roll(np.roll(out, P, 0), P, 1)
    img = np.zeros((size, size, 4), np.uint8)
    img[shadow & ~out] = PLUM + (140,)
    img[out] = PLUM + (255,)
    img[tri] = (255, 255, 255, 255)
    return Image.fromarray(img)


if __name__ == "__main__":
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    d, a = disc(), arrow()
    d.save(os.path.join(out, "UI_CMN_Btn_01A.png")); a.save(os.path.join(out, "UI_CMN_Arrow.png"))
    if len(sys.argv) > 2:
        pv = Image.new("RGBA", (420, 200), (248, 222, 238, 255))
        pv.alpha_composite(d, (20, 20)); b = d.copy(); b.alpha_composite(a.resize((101, 101), Image.NEAREST), (29, 29))
        pv.alpha_composite(b, (220, 20))
        pv.resize((840, 400), Image.NEAREST).save(sys.argv[2])
