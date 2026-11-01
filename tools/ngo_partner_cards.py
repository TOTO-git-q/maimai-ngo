"""Partner cards (music-select top screen, track start, collection: UI_CharactorSlot) from the owner's KAngel
illustrations, backgrounds kept. With src/Unity/NgoPartnerCards.cs the slot's capsule mask is off and the picture is
drawn above the frame (and above the next cards) at DISPLAY px, so each 512x512 chara texture carries its own window:
the frame's capsule hole (read from the installed UI_CMN_Chara_RFrame_01) filled with a cover crop of the picture.
Card 2 (the pixel KAngel reaching out) keeps the original pixels: the owner's crop sits in the window, the figure
outside it (the pointing hand, the other hand's fingers, ribbons, hair) hangs out over the frame, and the hair the
original crops off at the top comes from the Codex cut-out with the completed head (out/ngo_kangel_breakout),
registered onto the original.
usage: python ngo_partner_cards.py <out dir> [preview.png]"""
import os, sys
import cv2
import numpy as np
from PIL import Image
from scipy import ndimage

REF = r"E:\cxref\maimai-ngo\tasks\refs_chara"
FRAMES = os.environ.get("NGO_FRAMES", r"E:\cxref\maimai-ngo\out\ngo_partner_frame2")   # the installed card frame set
FRAME = os.path.join(FRAMES, "UI_CMN_Chara_RFrame_01.png")
CUT = r"E:\cxref\maimai-ngo\out\ngo_kangel_cut134"
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
DISPLAY = 300.0            # picture size on screen: stock 200 (512 x 0.34 x mask 1.15) x NgoPartnerCards.Enlarge
CY = 4.5 - 11.7 * 1.15     # picture centre in slot units (UI_Mask at +4.5, IMG_Charactor at -11.7 inside it, x1.15)
T = 512
FW, FH = 148, 264          # frame rect = slot rect, centred on the slot

# slot (x right, y up) <-> texture (x right, y down)
def tex_to_slot(tx, ty): return (tx - T / 2) * DISPLAY / T, CY - (ty - T / 2) * DISPLAY / T

# the window: the frame's capsule hole, mapped into texture pixels (1 px under the inner ring so no seam shows)
fa = np.asarray(Image.open(FRAME))[..., 3] < 128
lab, n = ndimage.label(fa)
edge = set(np.r_[lab[0], lab[-1], lab[:, 0], lab[:, -1]])
hole = np.isin(lab, [i for i in range(1, n + 1) if i not in edge])
hole = ndimage.binary_dilation(hole, iterations=1)
ty, tx = np.mgrid[0:T, 0:T].astype(float)
sx, sy = tex_to_slot(tx + .5, ty + .5)
fx, fy = np.floor(sx + FW / 2).astype(int), np.floor(FH / 2 - sy).astype(int)
ok = (fx >= 0) & (fx < FW) & (fy >= 0) & (fy < FH)
WIN = np.zeros((T, T), bool); WIN[ok] = hole[fy[ok], fx[ok]]
ys, xs = np.where(WIN); WB = (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1)   # window bbox in texture px
WW, WH = WB[2] - WB[0], WB[3] - WB[1]

# slot order left to right as the game shows them (455101, 555103, 555104, 555106); crop: (left x, top y, width) in source px, the height follows the window's aspect.
# A top above 0 is filled (card 2: the completed head).
CARDS = [("UI_Chara_455101", "kangel_1.jpg", (180, 100, 600)),   # her arm on the window's left edge
         ("UI_Chara_555103", "kangel_2.jpg", (100, -100, 560)),   # the owner's crop widened to the window's aspect; both hands come out
         ("UI_Chara_555104", "kangel_3.jpg", (120, 316, 520)),    # the ghost's feet on the window's bottom, her head up over the LV strip
         ("UI_Chara_555106", "kangel_4.jpg", (0, 150, 700))]      # her head and bows peek up over the tool strip
# cards whose figure leans out of the window: the Codex cut-out of her (RGBA, or on flat #00FF00), and the boxes where
# she may hang out past the window, slot units (x from, x to, lowest y, highest y). The window is x -64..64, y -88..84
REACH = {"UI_Chara_555103": (r"E:\cxref\maimai-ngo\out\ngo_kangel_sleeve\kangel_full_alpha.png",   # head, sleeve, waist complete
                             [(-100, -64, -100, 84),       # the left hand, its sleeve and the ribbons by it
                              (36, 100, -135, 84)]),       # the pointing hand, ribbons and the sleeve to its cuff
         "UI_Chara_455101": (CUT + r"\kangel_1_cut.png",
                             [(-112, -60, -75, 84)]),      # the hand resting on the left edge, the ribbons by it
         "UI_Chara_555104": (CUT + r"\kangel_3_cut.png",
                             [(60, 90, -60, 84),           # the wing over the right edge
                              (-64, 64, 80, 132)]),        # her head above the window, over the LV strip
         "UI_Chara_555106": (CUT + r"\kangel_4_cut.png",
                             [(-64, 64, 80, 132)])}        # the top of her head and the bows above the window
SKIRT = {"UI_Chara_555103": (880, 1250)}   # card 2 cut-out px: the waist, and where the skirt's right edge leaves it


def load_cut(path):
    """BGRA cut-out; a flat green (or blue) background is keyed out"""
    cut = cv2.imread(path, cv2.IMREAD_UNCHANGED)
    if cut.shape[2] == 4: return cut
    b, g, r = [cut[..., i].astype(int) for i in range(3)]
    key = ((g > 170) & (r < 110) & (b < 110)) | ((b > 170) & (r < 90) & (g < 90))
    key = ndimage.binary_opening(key, iterations=1)
    a = np.where(key, 0, 255).astype(np.uint8)
    a = cv2.erode(a, np.ones((3, 3), np.uint8))            # drop the green fringe
    return np.dstack([cut, a])


def head_fill(src, cut_path):
    """the Codex cut-out, registered onto the original (SIFT + similarity): the cut-out and its cut-out px ->
    original px matrix"""
    s = cv2.imread(src); cut = load_cut(cut_path)
    cb = (cut[..., :3] * (cut[..., 3:4] / 255.0)).astype(np.uint8)
    sift = cv2.SIFT_create(4000)
    k1, d1 = sift.detectAndCompute(cv2.cvtColor(s, cv2.COLOR_BGR2GRAY), None)
    k2, d2 = sift.detectAndCompute(cv2.cvtColor(cb, cv2.COLOR_BGR2GRAY), None)
    good = [m for m, n in cv2.BFMatcher().knnMatch(d2, d1, k=2) if m.distance < .75 * n.distance]
    M, _ = cv2.estimateAffinePartial2D(np.float32([k2[m.queryIdx].pt for m in good]),
                                       np.float32([k1[m.trainIdx].pt for m in good]), ransacReprojThreshold=6)
    return cut, M


def card(name, src, crop):
    path = os.path.join(REF, src)
    im = np.array(Image.open(path).convert("RGBA"))
    x0, top, w = crop
    k = WW / w
    if name not in REACH:   # a plain cover crop in the window
        size = (round(im.shape[1] * k), round(im.shape[0] * k)); at = (round(WB[0] - x0 * k), round(WB[1] - top * k))
        out = Image.new("RGBA", (T, T)); out.alpha_composite(Image.fromarray(im).resize(size, Image.LANCZOS), at)
        a = np.array(out); a[..., 3] = np.where(WIN, a[..., 3], 0)
        return Image.fromarray(a)
    # a figure leaving the window: two layers. Background = the original picture (its black panel, sparkles, and the
    # body below the cut-out's bottom edge), shown only in the window, black panel above the original's top edge.
    # Figure = the whole Codex cut-out (complete head, hands, sleeve, one consistent drawing with its own outline and
    # transparent surround), registered onto the original and warped straight into the texture, over everything
    # within the reach; so no background leaves the window and nothing of her is cut off
    pad = max(0, -top)
    im = np.concatenate([np.tile(np.array([0, 0, 0, 255], np.uint8), (pad, im.shape[1], 1)), im])
    size = (round(im.shape[1] * k), round(im.shape[0] * k)); at = (round(WB[0] - x0 * k), round(WB[1] - (top + pad) * k))
    bg = Image.new("RGBA", (T, T)); bg.alpha_composite(Image.fromarray(im).resize(size, Image.LANCZOS), at)
    b = np.array(bg); b[..., 3] = np.where(WIN, b[..., 3], 0)
    cut, M = head_fill(path, REACH[name][0])       # cut-out px -> original px
    S = np.array([[k, 0, at[0]], [0, k, at[1] + pad * k], [0, 0, 1]])   # original px -> texture px
    A = (S @ np.vstack([M, [0, 0, 1]]))[:2]
    fg = cv2.cvtColor(cv2.warpAffine(cut, A, (T, T), flags=cv2.INTER_AREA), cv2.COLOR_BGRA2RGBA)
    # the cut-out's body runs off its own bottom edge: outside the window only what ends inside the drawing
    rows = cv2.warpAffine(np.repeat(np.arange(cut.shape[0], dtype=np.float32)[:, None], cut.shape[1], 1), A, (T, T),
                          flags=cv2.INTER_NEAREST, borderValue=1e9)
    cols = cv2.warpAffine(np.repeat(np.arange(cut.shape[1], dtype=np.float32)[None], cut.shape[0], 0), A, (T, T),
                          flags=cv2.INTER_NEAREST, borderValue=1e9)
    # her skirt stays behind the card's frame (the owner: it must not cover the border); the sleeve beside it,
    # above the skirt's right edge (a 45 degree line from the waist, read off the cut-out), still hangs out
    skirt = np.zeros((T, T), bool)
    if name in SKIRT:
        sk = SKIRT[name]; skirt = (rows > sk[1]) & ((rows - sk[1]) > (cols - sk[0]))
    reach = np.zeros((T, T), bool)
    for rx0, rx1, ry0, ry1 in REACH[name][1]:
        reach |= (sx >= rx0) & (sx <= rx1) & (sy >= ry0) & (sy <= ry1)
    out_ok = reach & (rows < cut.shape[0]) & ~skirt
    fg[..., 3] = np.where(WIN | out_ok, fg[..., 3], 0)
    lab, n = ndimage.label(fg[..., 3] > 0)          # slivers the skirt line leaves behind
    if n:
        sizes = ndimage.sum(fg[..., 3] > 0, lab, range(1, n + 1))
        fg[..., 3][np.isin(lab, 1 + np.where(sizes < 60)[0])] = 0
    out = Image.fromarray(b); out.alpha_composite(Image.fromarray(fg))
    return out


for name, src, crop in CARDS:
    card(name, src, crop).save(os.path.join(OUT, name + ".png"))
    print(name, src, "crop", crop)

if len(sys.argv) > 2:   # the row as the game stacks it: base, frame, then every picture above all frames
    base = Image.open(os.path.join(FRAMES, "UI_CMN_Chara_RBase_01.png"))
    frame = Image.open(FRAME)
    pv = Image.new("RGBA", (4 * 139 + 80, 360), (246, 214, 236, 255))
    for i in range(len(CARDS)):
        ox, oy = 40 + i * 139, 50
        pv.alpha_composite(base, (ox, oy)); pv.alpha_composite(frame, (ox, oy))
    order = sorted(range(len(CARDS)), key=lambda i: CARDS[i][0] == "UI_Chara_555103")   # card 2 on top (mod: "front")
    for i in order:
        name = CARDS[i][0]
        ox, oy = 40 + i * 139, 50
        pic = Image.open(os.path.join(OUT, name + ".png")).resize((int(DISPLAY), int(DISPLAY)), Image.LANCZOS)
        pv.alpha_composite(pic, (round(ox + FW / 2 - DISPLAY / 2), round(oy + FH / 2 - CY - DISPLAY / 2)))
    lv = Image.open(r"E:\cxref\maimai-ngo\out\ngo_chara_frames\UI_UPE_MBase_Lv.png").resize((36, 19), Image.NEAREST)
    star = Image.open(r"E:\cxref\maimai-ngo\tasks\refs_chara\now_UI_CMN_Chara_Star_01.png").convert("RGBA").resize((46, 46), Image.LANCZOS)
    for i in range(len(CARDS)):        # the card's marks, above every picture: "LV" (5 px down, NgoPartnerCards) and the star
        ox, oy = 40 + i * 139, 50
        pv.alpha_composite(lv, (ox + 24, oy + 21)); pv.alpha_composite(star, (ox + 51, oy + 222))
    pv.resize((pv.width * 2, pv.height * 2), Image.LANCZOS).save(sys.argv[2])
