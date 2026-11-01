"""Character slots on the music-select top screen (UI_CharactorSlot frame + base, the leader's LV bar, the "LV" tag),
faithful to the stock design: every stock pixel is sorted into the stock's own colour zones (dark frame, light inner
ring / LV block, white edge, pale base stripes), the zones are snapped to a 2 px pixel grid and repainted with the
NGO violet ramp. Shapes, rounded corners, the inner ring and the two-tone LV block all stay where the stock has them.
"LV" is pixel letters on the same grid, white so the game's tint still applies (shared with the gameplay level knob).
Stock textures from tasks/refs_chara/stock_*.png. usage: python ngo_chara_frames.py <out dir> [preview.png]"""
import os, sys
import numpy as np
from PIL import Image
from ngo_diff_frames import P, load

REF = r"E:\cxref\maimai-ngo\tasks\refs_chara"
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
# stock zone colour -> NGO colour (zone 0 = transparent)
ZONES = [((40, 168, 232), (150, 84, 222)),     # dark frame / header band
         ((150, 240, 245), (222, 196, 255)),   # light inner ring
         ((70, 240, 245), (196, 150, 245)),    # the leader bar's light LV block
         ((246, 246, 246), (255, 255, 255)),   # white edge / bowl / base
         ((212, 240, 242), (240, 226, 255))]   # pale base stripes


def stock(n): return load(os.path.join(REF, "stock_%s.png" % n))
def save(n, a): Image.fromarray(a).save(os.path.join(OUT, n + ".png"))


def snap(lab, g):
    """majority vote on a g-pixel grid, back to full size"""
    h, w = lab.shape
    a = np.pad(lab, ((0, -h % g), (0, -w % g)))
    v = a.reshape(a.shape[0] // g, g, a.shape[1] // g, g).transpose(0, 2, 1, 3).reshape(a.shape[0] // g, a.shape[1] // g, -1)
    m = np.apply_along_axis(lambda x: np.bincount(x, minlength=8).argmax(), 2, v)
    return np.kron(m, np.ones((g, g), int))[:h, :w]


def recolor(rgba, zones, g=2):
    """the stock's thin rings are 2-4 px wide, so these slots use a 2 px grid (the song frames use 4)"""
    src = np.array([z[0] for z in zones], float); dst = [z[1] for z in zones]
    d = ((rgba[..., None, :3].astype(float) - src) ** 2).sum(-1)
    lab = d.argmin(-1) + 1
    lab[rgba[..., 3] < 128] = 0
    lab = snap(lab, g)
    img = np.zeros(rgba.shape, np.uint8)
    for i, c in enumerate(dst): img[lab == i + 1] = c + (255,)
    return img


save("UI_CMN_Chara_RFrame_01", recolor(stock("UI_CMN_Chara_RFrame_01"), [ZONES[0], ZONES[1], ZONES[3]]))
save("UI_CMN_Chara_LFrame_01", recolor(stock("UI_CMN_Chara_LFrame_01"), [ZONES[0], ZONES[2], ZONES[3]]))
save("UI_CMN_Chara_RBase_01", recolor(stock("UI_CMN_Chara_RBase_01"), [ZONES[3], ZONES[4], ZONES[1]]))

# "LV" on the 4 px grid (the tag is 56x24 = 14x6 cells), one-cell strokes
LV = [".X....X....X..",
      ".X....X....X..",
      ".X.....X..X...",
      ".X.....X..X...",
      ".X......XX....",
      ".XXXX...XX...."]
cellmask = np.array([[c == "X" for c in row] for row in LV])
tag = np.zeros((24, 56, 4), np.uint8)
tag[np.kron(cellmask, np.ones((P, P), bool))] = (255, 255, 255, 255)
save("UI_UPE_MBase_Lv", tag)

if len(sys.argv) > 2:   # one slot as the game stacks it, and the leader bar, stock above / NGO below
    pv = Image.new("RGBA", (360, 580), (246, 214, 236, 255))
    for row, get in ((0, lambda n: Image.open(os.path.join(REF, "stock_%s.png" % n))),
                     (290, lambda n: Image.open(os.path.join(OUT, n + ".png")))):
        pv.alpha_composite(get("UI_CMN_Chara_RBase_01").convert("RGBA"), (10, 10 + row))
        pv.alpha_composite(get("UI_CMN_Chara_RFrame_01").convert("RGBA"), (10, 10 + row))
        pv.alpha_composite(get("UI_CMN_Chara_LFrame_01").convert("RGBA"), (190, 100 + row))
    pv.resize((720, 1160), Image.NEAREST).save(sys.argv[2])
