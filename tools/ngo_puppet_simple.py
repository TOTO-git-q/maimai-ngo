"""Simple puppet from the owner-approved chibi drawings (no limb animation, no cable; the owner: "不需要完全还原 maimai
原来的", "不用那个电源线", and no extra hands/feet on the P cat).
- DXkuma = Ame: Head/HeadRing from the in-game-approved v5 cut. Everything below the head (torso, both jacket sleeves,
  skirt, legs) goes into ONE body piece, cut from the drawing with the same similarity transform v5 used for the
  result pose, so the figure moves as one and nothing can come apart. Hands, legs and the tail cable are transparent.
- LLkuma = KAngel (Lemon) and the P cat (Lime): each whole figure goes into its own body texture. The shared hands,
  legs, eyes, mouth and decorations are transparent, because both characters share those textures.
usage: python ngo_puppet_simple.py <out dir>"""
import os, shutil, sys
import cv2
import numpy as np
from PIL import Image

X = r"E:\cxref\maimai-ngo"
STOCK = os.path.join(X, r"tasks\refs_endflow\puppet")
V5 = os.path.join(X, r"out\ngo_end_puppet5\replacements")
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
for f in os.listdir(V5): shutil.copyfile(os.path.join(V5, f), os.path.join(OUT, f))

def stock_size(name): return Image.open(os.path.join(STOCK, name + ".png")).size
def save(name, im):
    a = np.array(im.convert("RGBA")); a[..., 3] = np.where(a[..., 3] >= 128, 255, 0)
    Image.fromarray(a).save(os.path.join(OUT, name + ".png"))
def clear(name): save(name, Image.new("RGBA", stock_size(name)))

def cut(path):
    """drawing on white -> RGBA; only the white connected to the border becomes transparent"""
    a = np.array(Image.open(path).convert("RGB"))
    near = np.all(a > 245, axis=2).astype("uint8")
    _, lab, _, _ = cv2.connectedComponentsWithStats(near, 8)
    edge = np.unique(np.r_[lab[0], lab[-1], lab[:, 0], lab[:, -1]])
    bg = np.isin(lab, edge[edge != 0])
    return Image.fromarray(np.dstack([a, np.where(bg, 0, 255).astype("uint8")]))

def fit(src, size, margin=0.02, anchor="bottom"):
    """whole figure, aspect kept, centred horizontally, standing on the bottom edge"""
    bb = src.getchannel("A").getbbox(); src = src.crop(bb)
    w, h = size; k = min(w * (1 - 2 * margin) / src.width, h * (1 - margin) / src.height)
    im = src.resize((max(1, round(src.width * k)), max(1, round(src.height * k))), Image.LANCZOS)
    dst = Image.new("RGBA", size)
    dst.alpha_composite(im, ((w - im.width) // 2, h - im.height if anchor == "bottom" else (h - im.height) // 2))
    return dst

# --- Ame: same placement as v5 (result pose, 2 samples per display unit) ---
ame = cut(os.path.join(X, r"out\ngo_chibi_v2\ame.png"))
s = 2; scale = .32 * s; ox = -41.6 * s
canvas = Image.new("RGBA", (700, 900))
canvas.alpha_composite(ame.resize((round(ame.width * scale), round(ame.height * scale)), Image.LANCZOS), (round(ox), 0))
a = np.array(canvas); a[..., 3] = np.where(a[..., 3] >= 128, 255, 0)
y, x = np.indices(a.shape[:2]); sx = (x - ox) / scale; sy = y / scale
# One horizontal cut at the neck: above it head + hair (Head and HeadRing move together), below it everything else,
# twin-tail ends included. v5's ring mask also caught sleeve pixels, which then rode on the head and left holes.
# The stock head rect is big enough for everything above the waist at the drawing's own scale, the body rect for
# everything below it; cut at the waist so neither piece is shrunk, with an overlap band so motion opens no gap.
WAIST, OVERLAP = 830, 40
NECK = WAIST - OVERLAP   # body piece starts here (the band is drawn in both pieces)
opaque = a[..., 3] > 0
headm = opaque & (sy < WAIST); body = opaque & (sy >= NECK)
h = a.copy(); h[..., 3] = np.where(headm, 255, 0)
hr = (0, 0, 341, 341)   # stock result head rect, display units
headpiece = Image.fromarray(h).crop((hr[0] * s, hr[1] * s, (hr[0] + hr[2]) * s, (hr[1] + hr[3]) * s))
print("Ame head piece: lost", int(headm.sum()) - int((np.array(headpiece)[..., 3] > 0).sum()), "px")
for n in ("UI_CMN_DXkuma_Head_01", "UI_CMN_DXkuma_HeadRing_01", "UI_CMN_DXkuma_HeadRing_02"):
    save(n, headpiece.resize(stock_size(n), Image.LANCZOS))
b = a.copy(); b[..., 3] = np.where(body, 255, 0)
rx, ry, rw, rh = 58, 195, 225, 245   # stock result body rect, display units
piece = Image.fromarray(b).crop((rx * s, ry * s, (rx + rw) * s, (ry + rh) * s))
print("Ame body piece: lost %d px" % (int(body.sum()) - int((np.array(piece)[..., 3] > 0).sum())))
save("UI_CMN_DXkuma_Body_01", piece.resize(stock_size("UI_CMN_DXkuma_Body_01"), Image.LANCZOS))
for n in ("UI_CMN_DXkuma_Body_02", "UI_CMN_DXkuma_S_Body_01"):   # other states: the same piece in their canvas
    save(n, piece.resize(stock_size(n), Image.LANCZOS))
for f in os.listdir(OUT):
    n = f[:-4]
    if n.startswith("UI_CMN_DXkuma_") and any(t in n for t in ("_Hand_", "_S_Hand_", "_Leg_", "_S_Leg_", "_Tail_")): clear(n)

# --- side pair: whole figures in the body textures, shared parts empty ---
kangel = cut(os.environ.get("KANGEL", os.path.join(X, r"out\ngo_chibi_v2\kangel.png")))   # v3: out\ngo_chibi_v3
pcat = cut(os.environ.get("PCAT", os.path.join(X, r"out\ngo_chibi_pcat_round\pcat.png")))
for n in ("UI_CMN_LLkuma_Body_Lemon", "UI_CMN_LLkuma_Body_Lemon_Afuro"): save(n, fit(kangel, stock_size(n)))
for n in ("UI_CMN_LLkuma_Body_Lime", "UI_CMN_LLkuma_Body_Lime_Afuro"): save(n, fit(pcat, stock_size(n)))
for f in os.listdir(STOCK):
    n = f[:-4]
    if n.startswith("UI_CMN_LLkuma_") and "_Body_" not in n: clear(n)
print("written", len(os.listdir(OUT)), "textures to", OUT)
