"""Gameplay upper-screen song panel (UI_UPE_MusicJacket_Base_<d>, UI_UPE_MBase_<d>, UI_UPE_Box_01) redrawn as one
NGO pixel piece. The official silhouettes are kept (jacket frame, header bar with the level knob, achievement bar); the
three textures are drawn together on one canvas at their in-game offsets, so the outline, bevel, stripes and hearts run
across them as one surface, then cut back into the separate textures. Colours stay per difficulty like the official.
usage: python ngo_upe_hud.py <out dir> [preview.png]"""
import os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

REF = r"E:\cxref\maimai-ngo\tasks\refs_endflow\gametop"
BOX = r"E:\cxref\maimai-ngo\out\m1_title_login\for_repo\s11_login\UI_UPE_Box_01.png"
FONT = r"C:\Windows\Fonts\ARLRDBD.TTF"   # Arial Rounded Bold: close to the official rounded heavy word, snapped to the grid
P = 4                       # pixel unit
JX, JY = 0, 22              # jacket texture origin on the canvas (MBase at (286, 0)); from the gameplay RectTransforms
MX, MY = 286, 0
W, H = 286 + 792, 22 + 288
SCREEN = (59, 31, 150)      # Box_01 face: NGO deep purple, shared by every difficulty (the official is one navy)
DIFFS = {  # main, dark, light, word
    "BSC": ((69, 193, 36), (14, 117, 54), (150, 226, 110), "BASIC"),
    "ADV": ((255, 186, 1), (199, 69, 12), (255, 226, 90), "ADVANCED"),
    "EXP": ((255, 90, 102), (192, 32, 56), (255, 150, 160), "EXPERT"),
    "MST": ((159, 81, 220), (90, 20, 141), (196, 140, 240), "MASTER"),
    "MST_Re": ((210, 160, 255), (140, 44, 213), (240, 224, 255), "Re:MASTER"),
    "UTG": ((250, 120, 250), (150, 40, 190), (255, 196, 255), "U·TA·GE"),
    "DMY": ((170, 170, 170), (100, 100, 100), (210, 210, 210), "DUMMY"),
}


def snap(mask):
    """Pixel-step a silhouette on the P grid (canvas aligned)."""
    h, w = mask.shape
    m = np.pad(mask, ((0, -h % P), (0, -w % P)))
    small = m.reshape(m.shape[0] // P, P, m.shape[1] // P, P).mean(axis=(1, 3)) > .5
    return np.kron(small, np.ones((P, P), bool))[:h, :w]


def disk(r):
    y, x = np.ogrid[-r:r + 1, -r:r + 1]
    return x * x + y * y <= r * r + 1


def stock_alpha(name, ox, oy):
    a = np.asarray(Image.open(os.path.join(REF, name + ".png")).convert("RGBA"))[..., 3] > 128
    m = np.zeros((H, W), bool); m[oy:oy + a.shape[0], ox:ox + a.shape[1]] = a
    return snap(m)


def word_mask(text, height):
    f = ImageFont.truetype(FONT, int(height * 1.3))
    gap = 3 * P                                       # letters spaced so the outline never fuses them
    widths = [f.getbbox(ch)[2] - f.getbbox(ch)[0] if ch.strip() else P * 3 for ch in text]
    im = Image.new("L", (sum(widths) + gap * len(text) + 8, int(height * 2))); dr = ImageDraw.Draw(im)
    x = 4
    for ch, w in zip(text, widths):
        if ch.strip(): dr.text((x - f.getbbox(ch)[0], 4), ch, font=f, fill=255)
        x += w + gap
    a = np.asarray(im) > 128
    a = a[np.any(a, 1)][:, np.any(a, 0)]
    return snap(np.pad(a, P))


def draw(d):
    main, dark, light, word = DIFFS[d]
    jac = stock_alpha("UI_UPE_MusicJacket_Base_" + d, JX, JY)
    mb = stock_alpha("UI_UPE_MBase_" + d, MX, MY)
    shape = jac | mb
    img = np.zeros((H, W, 4), np.uint8)
    img[shape] = main + (255,)
    yy, xx = np.mgrid[0:H, 0:W]
    gy, gx = yy // P, xx // P
    # one set of diagonal light stripes across the whole panel (official 45 degree sweep), pixel stepped
    band = ((gx + gy) % 64)
    stripe = shape & ((band < 6) | ((band >= 9) & (band < 11)))
    img[stripe] = light + (255,)
    # sparse pixel hearts in the light colour, on the same grid everywhere
    heart = np.array([[0, 1, 0, 1, 0], [1, 1, 1, 1, 1], [1, 1, 1, 1, 1], [0, 1, 1, 1, 0], [0, 0, 1, 0, 0]], bool)
    for (hx, hy) in [(150, 6), (205, 26), (238, 8), (12, 48), (96, 60), (180, 64), (250, 44), (60, 8)]:
        y0, x0 = hy, hx
        sub = np.zeros((H // P + 1, W // P + 1), bool); sub[y0:y0 + 5, x0:x0 + 5] = heart
        m = np.kron(sub, np.ones((P, P), bool))[:H, :W] & shape
        img[m] = light + (255,)
    # bevel: white top-left inner line, dark bottom-right inner line, then the outline
    inner = ndimage.binary_erosion(shape, np.ones((2 * P + 1, 2 * P + 1)))
    edge = shape & ~inner
    up = inner & ~np.roll(inner, P, axis=0)
    lf = inner & ~np.roll(inner, P, axis=1)
    dn = inner & ~np.roll(inner, -P, axis=0)
    img[(up | lf) & ~dn] = (255, 255, 255, 255)
    img[dn] = dark + (255,)
    img[edge] = dark + (255,)
    # jacket hole: dark well with a white inner line (the jacket and the game's own outline sit on it)
    hole = np.zeros((H, W), bool); hole[JY + 34:JY + 255, JX + 48:JX + 269] = True
    img[hole] = dark + (255,)
    hole2 = np.zeros((H, W), bool); hole2[JY + 38:JY + 251, JX + 52:JX + 265] = True
    img[hole2] = (255, 255, 255, 255)
    # level knob: lighter disc where the level number sits
    knob = np.asarray(Image.open(os.path.join(REF, "UI_UPE_MBase_" + d + ".png")).convert("RGBA"))[..., 3] > 128
    cols = np.where(knob[:20].any(0))[0]
    if len(cols):
        cx = MX + int(cols.mean()); cy = MY + 58
        disc = np.zeros((H, W), bool); disc[(yy - cy) ** 2 + (xx - cx) ** 2 <= 44 ** 2] = True
        disc = snap(disc) & shape
        ring = disc & ~snap(ndimage.binary_erosion(disc, disk(P)))
        img[disc] = dark + (255,) if d == "MST_Re" else (255, 255, 255, 255)   # Re:MASTER digits are near white
        img[ring] = dark + (255,)
    # difficulty word: white pixel letters, dark outline and drop shadow, where the official word is
    wm = word_mask(word, 44)
    ox, oy = MX + 40, MY + 22
    full = np.zeros((H, W), bool); full[oy:oy + wm.shape[0], ox:ox + wm.shape[1]] = wm[:H - oy, :W - ox]
    out = ndimage.binary_dilation(full, np.ones((2 * P + 1, 2 * P + 1)))
    sh = np.roll(np.roll(out, P, 0), P, 1)
    img[sh & ~out] = dark + (255,)
    img[out] = dark + (255,)
    img[full] = (255, 255, 255, 255)
    return Image.fromarray(img)


def box01():
    s = 96; a = np.zeros((s, s, 4), np.uint8)
    m = np.zeros((s, s), bool); m[4:s - 4, 4:s - 4] = True
    for i, c in enumerate([12, 8, 4]):             # stepped corners
        for (y, x) in [(4 + i * 4, 4), (s - 8 - i * 4, 4), (4 + i * 4, s - 4 - c), (s - 8 - i * 4, s - 4 - c)]:
            m[y:y + 4, x:x + c] = False
    inner = ndimage.binary_erosion(m, np.ones((7, 7)))
    a[m] = (30, 12, 90, 255)
    a[inner] = SCREEN + (255,)
    top = inner & ~np.roll(inner, 4, axis=0)
    a[top] = (86, 58, 190, 255)
    return Image.fromarray(a)


if __name__ == "__main__":
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    b = box01(); b.save(os.path.join(out, "UI_UPE_Box_01.png"))
    tiles = []
    for d in DIFFS:
        c = draw(d)
        c.crop((JX, JY, JX + 316, JY + 288)).save(os.path.join(out, "UI_UPE_MusicJacket_Base_%s.png" % d))
        c.crop((MX, MY, MX + 792, MY + 288)).save(os.path.join(out, "UI_UPE_MBase_%s.png" % d))
        tiles.append(c)
    if len(sys.argv) > 2:
        prev = Image.new("RGBA", (W + 20, (H + 10) * len(tiles) + 10), (250, 228, 242, 255))
        for i, t in enumerate(tiles):
            prev.alpha_composite(t, (10, 10 + i * (H + 10)))
        prev.save(sys.argv[2])
