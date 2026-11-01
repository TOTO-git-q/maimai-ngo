"""Difficulty arc and music-select difficulty buttons in pixel style, fitted from the Codex sheets
(out/ngo_diff_arc_btns: arc.png, buttons.png; white/grey art on green).
- UI_CMN_TitleBase_Button 700x232: the arc stays white and grey (the game tints it by difficulty), fitted into the
  stock arc's bounding box; UI_CMN_TitleBase_Shadow goes transparent (no soft shadows in the skin).
- UI_MSS_Btn_* : each stock texture keeps its size and its own colour (difficulty semantics); the Codex shape for its
  size (216x128 trapezoid tab, 80x80 rounded square, 72x72 small pill) is fitted into the stock shape's bounding box and
  its four grey levels become that colour's ramp (face = the stock colour, then two shades and the outline).
Each sheet is snapped to its grey levels and reduced to the texture grid by block majority.
usage: python ngo_diff_btn_fit.py <out dir> [preview.png]"""
import os, sys
import numpy as np
from PIL import Image
from scipy import ndimage

SRC = r"E:\cxref\maimai-ngo\out\ngo_diff_arc_btns"
STOCK = r"E:\cxref\maimai-ngo\tasks\refs_audit2"      # stock_<name>.png, from ngo_dump_tex.py
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
BTNS = ["UI_MSS_Btn_01_01_A", "UI_MSS_Btn_01_01_B", "UI_MSS_Btn_01_02_A", "UI_MSS_Btn_01_02_B", "UI_MSS_Btn_01_03_A",
        "UI_MSS_Btn_01_03_B", "UI_MSS_Btn_01_04_A", "UI_MSS_Btn_01_04_B", "UI_MSS_Btn_01_05_A", "UI_MSS_Btn_01_05_B",
        "UI_MSS_Btn_02_01_A", "UI_MSS_Btn_02_01_B", "UI_MSS_Btn_03_01_A", "UI_MSS_Btn_03_01_B", "UI_MSS_Btn_04_01_A",
        "UI_MSS_Btn_04_01_B", "UI_MSS_Btn_04_02_A", "UI_MSS_Btn_04_02_B",
        "UI_CMN_Btn_03_01_A", "UI_CMN_Btn_03_01_B"]   # the round yellow arrow buttons by the music-select LV tabs
SHAPE = {(216, 128): 0, (80, 80): 1, (124, 124): 1, (72, 72): 2}     # Codex sheet order, left to right


def levels(path):
    """the sheet as grey-level indices: -1 background, 0 darkest (outline) .. 3 lightest (face)"""
    rgb = np.asarray(Image.open(path).convert("RGB")).astype(int)
    bg = (rgb[..., 1] > 170) & (rgb[..., 0] < 120) & (rgb[..., 2] < 120)
    lum = rgb.mean(-1)
    cuts = [85, 160, 225]                              # dark outline / mid grey / light grey / white
    lv = np.digitize(lum, cuts)
    lv[bg] = -1
    return lv


def reduce(lv, box, w, h):
    """crop lv to box and reduce to w x h by block majority (background wins only with a majority)"""
    x0, y0, x1, y1 = box; c = lv[y0:y1, x0:x1]
    ys = np.linspace(0, c.shape[0], h + 1).astype(int); xs = np.linspace(0, c.shape[1], w + 1).astype(int)
    out = np.full((h, w), -1)
    for j in range(h):
        for i in range(w):
            blk = c[ys[j]:ys[j + 1], xs[i]:xs[i + 1]].ravel() + 1
            out[j, i] = np.bincount(blk, minlength=5).argmax() - 1
    return out


def bbox(mask):
    ys, xs = np.where(mask); return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def place(lv_small, size, box, ramp):
    a = np.zeros((size[1], size[0], 4), np.uint8)
    x0, y0, x1, y1 = box
    for k, col in enumerate(ramp):
        m = lv_small == k
        a[y0:y1, x0:x1][m] = tuple(col) + (255,)
    return Image.fromarray(a)


def stock(name): return np.asarray(Image.open(os.path.join(STOCK, "stock_%s.png" % name)).convert("RGBA"))


# arc: white/grey, tinted by the game
arc = levels(os.path.join(SRC, "arc.png"))
st = stock("UI_CMN_TitleBase_Button"); box = bbox(st[..., 3] > 128)
small = reduce(arc, bbox(arc >= 0), box[2] - box[0], box[3] - box[1])
GREY = [(96, 96, 96), (150, 150, 150), (214, 214, 214), (255, 255, 255)]
place(small, (st.shape[1], st.shape[0]), box, GREY).save(os.path.join(OUT, "UI_CMN_TitleBase_Button.png"))
Image.new("RGBA", (700, 232)).save(os.path.join(OUT, "UI_CMN_TitleBase_Shadow.png"))

# buttons: the three shapes on the sheet, left to right
lv = levels(os.path.join(SRC, "buttons.png"))
lab, n = ndimage.label(lv >= 0)
sizes = ndimage.sum(lv >= 0, lab, range(1, n + 1))
comps = sorted((1 + np.argsort(sizes)[-3:]).tolist(), key=lambda i: bbox(lab == i)[0])
shapes = []
for i in comps:
    m = lab == i; sub = np.where(ndimage.binary_fill_holes(m), lv, -1); shapes.append((sub, bbox(m)))
# some names are two textures of different sizes (ngo_dump_tex.py: stock_<name>@<w>x<h>.png); those go to <out>/alt,
# patched in a second pass (ngo_patch_assets.py only writes a texture whose size matches)
jobs = [(name, name, OUT) for name in BTNS]
for name in BTNS:
    for f in os.listdir(STOCK):
        if f.startswith("stock_%s@" % name): jobs.append((name, f[6:-4], os.path.join(OUT, "alt")))
os.makedirs(os.path.join(OUT, "alt"), exist_ok=True)
for name, key, out in jobs:
    st = stock(key); h, w = st.shape[:2]
    op = st[..., 3] > 128
    box = bbox(st[..., 3] > 200)                       # the button itself, not its soft glow
    sub, sb = shapes[SHAPE[(w, h)]]
    small = reduce(sub, sb, box[2] - box[0], box[3] - box[1])
    base = np.median(st[..., :3][op], 0)               # the button's own colour
    ramp = [base * .38, base * .62, base * .82, base]
    if base.min() > 200:                               # white buttons: lavender greys instead of dirty greys
        ramp = [np.array((77, 35, 207)), np.array((195, 166, 227)), np.array((223, 210, 245)), np.array((255, 255, 255))]
    place(small, (w, h), box, [np.clip(c, 0, 255).astype(int) for c in ramp]).save(os.path.join(out, name + ".png"))
print("wrote", len(jobs) + 2, "textures to", OUT)

if len(sys.argv) > 2:   # stock | new for every texture, on a pink page
    names = ["UI_CMN_TitleBase_Button"] + BTNS
    pv = Image.new("RGBA", (1500, 40 + sum(max(stock(nm).shape[0], 40) + 8 for nm in names) // 2 + 260), (246, 214, 236, 255))
    x, y, col_h = 10, 10, 0
    for nm in names:
        s = Image.fromarray(stock(nm)); nw = Image.open(os.path.join(OUT, nm + ".png"))
        if nm == "UI_CMN_TitleBase_Button":            # show the arc tinted green like BASIC
            t = np.asarray(nw).astype(float); t[..., :3] *= np.array((112, 212, 59)) / 255; nw = Image.fromarray(t.astype(np.uint8))
            s0 = np.asarray(s).astype(float); s0[..., :3] *= np.array((112, 212, 59)) / 255; s = Image.fromarray(s0.astype(np.uint8))
        if y + s.height > pv.height - 10: x += 520; y = 10
        pv.alpha_composite(s, (x, y)); pv.alpha_composite(nw, (x + s.width + 10, y)); y += s.height + 8
    pv.save(sys.argv[2])
