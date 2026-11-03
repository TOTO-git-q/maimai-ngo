"""Result-screen rank letters: rebuild the effect masks/overlays so the shine stays on the pixel letters.
Stock roles (from the stock textures): each glossy letter has a thin white overlay drawn in its own shape and used as
a Mask for the sweeping shine (UI_RSL_Rank_Effect_Slide_03) and as the white flash:
- UI_RSL_Rank_S_White01 (Null_eff/S_Base_Eff_Mask): the S's inner highlight line, the shine's mask;
- UI_RSL_Rank_S_White: the S's outline, flashed white;
- UI_RSL_Rank_A_mask / B_mask: the same for A and B;
- UI_RSL_Rank_Plus_White02..04: thin L-shaped glints, one per quadrant (02 upper right, 03 lower right, 04 lower
  left), flashed white over the plus on the same 148x148 canvas.
The NGO re-skin filled them with the letter's full bounding block, so the shine swept the whole rectangle around the
letter and looked like it flew about. Here every letter mask becomes the matching NGO letter's own silhouette (the
installed letter texture's alpha), shrunk 2 px so the shine sits inside the outline.
The plus is different: its face is pure white, so a white glint only shows on the purple outline. Each tick is a
4 px line down the middle of the outline band (chessboard distance 4..7 px from the face; the band is 10 px) around that quadrant's
inner corner, stopping 6 px short of the arm ends, never on the pink drop shadow. Same corner as the stock tick.
Inputs: tasks/refs_rank/now_*.png (ngo_dump_tex.py). usage: python ngo_rank_masks.py <out dir> [preview.png]"""
import os, sys
import numpy as np
from PIL import Image
from scipy import ndimage

D = r"E:\cxref\maimai-ngo\tasks\refs_rank"
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
# overlay -> (letter it belongs to, shrink px, alpha)
JOBS = {"UI_RSL_Rank_S_White01": ("UI_RSL_Rank_S_Yellow", 3, 255),
        "UI_RSL_Rank_S_White": ("UI_RSL_Rank_S_Yellow", 1, 255),
        "UI_RSL_Rank_A_mask": ("UI_RSL_Rank_A", 3, 255),
        "UI_RSL_Rank_B_mask": ("UI_RSL_Rank_B", 3, 255),
        "UI_RSL_Rank_Plus_White02": ("UI_RSL_Rank_Plus", 0, 255),
        "UI_RSL_Rank_Plus_White03": ("UI_RSL_Rank_Plus", 0, 255),
        "UI_RSL_Rank_Plus_White04": ("UI_RSL_Rank_Plus", 0, 255)}
PLUS_TICKS = {"UI_RSL_Rank_Plus_White02": (+1, -1), "UI_RSL_Rank_Plus_White03": (+1, +1), "UI_RSL_Rank_Plus_White04": (-1, +1)}


def load(n): return np.array(Image.open(os.path.join(D, "now_%s.png" % n)).convert("RGBA"))


def plus_tick(name):
    """The quadrant's glint on the NGO plus: the middle of the purple outline band around that inner corner."""
    sx, sy = PLUS_TICKS[name]
    a = load("UI_RSL_Rank_Plus").astype(int)
    face = (a[..., 3] > 128) & (a[..., :3].min(axis=2) > 220)
    outline = (a[..., 3] > 128) & (a[..., 0] < 120) & (a[..., 2] > 150)          # the purple, not the pink shadow
    ys, xs = np.nonzero(face)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()                       # arm ends
    cols, rows = np.nonzero(face[y0 + 5])[0], np.nonzero(face[:, x0 + 5])[0]
    vx0, vx1, hy0, hy1 = cols.min(), cols.max(), rows.min(), rows.max()           # the vertical / horizontal arm's sides
    d = ndimage.distance_transform_cdt(~face, metric="chessboard")
    yy, xx = np.mgrid[:a.shape[0], :a.shape[1]]
    quad = (xx > vx1 if sx > 0 else xx < vx0) & (yy < hy0 if sy < 0 else yy > hy1)
    keep = (yy >= y0 + 6) & (yy <= y1 - 6) & (xx >= x0 + 6) & (xx <= x1 - 6)     # stop short of the arm ends
    m = quad & keep & outline & (d >= 4) & (d <= 7)
    out = np.zeros(a.shape, np.uint8); out[..., :3] = 255; out[..., 3] = np.where(m, 255, 0)
    return Image.fromarray(out), int(m.sum())


def s_shine_mask():
    """UI_RSL_Rank_S_White01 is drawn on a 260x380 node at scale 0.55 (143x209 on screen, uniform), while the letter
    UI_RSL_Rank_S_* is stretched onto a 176x212 node; both centred on the same point (prefab Null_eff/S_Base_Eff_Mask and
    Parts_S_Base (1)). The mask must hold the letter as it appears on screen, so each mask pixel samples the letter
    texture through the two mappings. Only the yellow face counts (not the outline or the pink drop shadow), eroded
    2 px, so the shine runs inside the letter."""
    ref = load("UI_RSL_Rank_S_Yellow").astype(int)
    face = (ref[..., 3] > 128) & (ref[..., 0] > 200) & (ref[..., 1] > 180) & (ref[..., 2] < 170)
    W, H = 260, 380
    um, vm = np.meshgrid(np.arange(W) + .5, np.arange(H) + .5)
    sx, sy = 176 / 260 / 0.55, 212 / 380 / 0.55            # letter texture px -> mask texture px
    ul = ((um - W / 2) / sx + W / 2).astype(int); vl = ((vm - H / 2) / sy + H / 2).astype(int)
    ok = (ul >= 0) & (ul < W) & (vl >= 0) & (vl < H)
    m = np.zeros((H, W), bool); m[ok] = face[vl[ok], ul[ok]]
    m = ndimage.binary_erosion(m, iterations=2)
    out = np.zeros((H, W, 4), np.uint8); out[..., :3] = 255; out[..., 3] = np.where(m, 255, 0)
    return Image.fromarray(out)


for over, (letter, shrink, alpha) in JOBS.items():
    if over == "UI_RSL_Rank_S_White01":
        im = s_shine_mask(); im.save(os.path.join(OUT, over + ".png")); print(over, "remapped to the on-screen S face"); continue
    if over in PLUS_TICKS:
        im, n = plus_tick(over); im.save(os.path.join(OUT, over + ".png")); print(over, "outline glint pixels", n); continue
    ref = load(letter)
    size = Image.open(os.path.join(D, "now_%s.png" % over)).size
    sil = ref[..., 3] > 128
    sil = ndimage.binary_fill_holes(sil)                      # the letter's whole shape, counters included
    if ref.shape[1::-1] != size:                              # S_White* share the S's canvas; others match their letter
        sil = np.array(Image.fromarray(sil.astype(np.uint8) * 255).resize(size, Image.NEAREST)) > 0
    # inside the letter's own pixels only (not its counters), shrunk away from the outline
    body = ref[..., 3] > 128
    if body.shape[::-1] != size: body = np.array(Image.fromarray(body.astype(np.uint8) * 255).resize(size, Image.NEAREST)) > 0
    m = ndimage.binary_erosion(body, iterations=shrink)
    out = np.zeros((size[1], size[0], 4), np.uint8); out[..., :3] = 255; out[..., 3] = np.where(m, alpha, 0)
    Image.fromarray(out).save(os.path.join(OUT, over + ".png"))
    print(over, size, "pixels", int(m.sum()), "of letter", int(body.sum()))

if len(sys.argv) > 2:   # the S as on screen (176x212) with the shine mask as drawn (143x209, centred) in pink
    letter = Image.fromarray(load("UI_RSL_Rank_S_Yellow")).resize((176, 212), Image.NEAREST)
    m = np.array(Image.open(os.path.join(OUT, "UI_RSL_Rank_S_White01.png")))
    m[..., :3] = (255, 60, 200); m[..., 3] = m[..., 3] // 2
    m = Image.fromarray(m).resize((143, 209), Image.NEAREST)
    pv = Image.new("RGBA", (176, 212), (40, 40, 60, 255)); pv.alpha_composite(letter); pv.alpha_composite(m, ((176 - 143) // 2, (212 - 209) // 2))
    pv.resize((176 * 3, 212 * 3), Image.NEAREST).save(sys.argv[2])
    sys.exit()
if len(sys.argv) > 3:   # each letter with its new mask shown as a translucent pink overlay
    names = list(JOBS)
    pv = Image.new("RGBA", (300 * 4, 400 * 2), (40, 40, 60, 255))
    for i, over in enumerate(names):
        letter = JOBS[over][0]
        a = Image.fromarray(load(letter)); m = np.array(Image.open(os.path.join(OUT, over + ".png")))
        m[..., :3] = (255, 60, 200); m[..., 3] = m[..., 3] // 2
        m = Image.fromarray(m).resize(a.size, Image.NEAREST)
        a.alpha_composite(m); pv.alpha_composite(a, ((i % 4) * 300, (i // 4) * 400))
    pv.save(sys.argv[2])
