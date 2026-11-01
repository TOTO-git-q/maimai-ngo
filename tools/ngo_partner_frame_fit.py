"""NGO partner-card frame, card base and leader LV plate, fitted from the Codex Windose window paintings
(out/ngo_partner_frame2/raw). Each painting is snapped to its own palette and reduced to the texture's pixel grid by
block majority, then cut at its plain runs and re-stacked to the game's layout:
- card frame 148x264: the painting's title bar (rows 0-16); a violet tool strip made by repeating the painting's own
  band under the title bar (rows 17-43), where the game draws the white "LV" and level digits (NgoPartnerCards moves
  them 5 px down into it); the window's top edge, the window with its side rails (rows 48-219, plain rail rows
  dropped), the bottom border and the star's stand tab. Below the frame and the window opening are transparent;
- card base: the generated checker, cut to the frame's body;
- leader plate 144x64: the stepped-end plate with a violet face (white text on it); its star tab is dropped, the
  star badge hangs under the plate by itself.
usage: python ngo_partner_frame_fit.py <out dir>"""
import os, sys
import numpy as np
from PIL import Image

RAW = r"E:\cxref\maimai-ngo\out\ngo_partner_frame2\raw"
FRAME_SRC = os.path.join(RAW, "exec-e09f8d70-f111-4152-b236-b59618804cd3.png")
PLATE_SRC = os.path.join(RAW, "exec-3db2acb6-a5dc-443b-8d8e-97e76b413211.png")
BASE_SRC = r"E:\cxref\maimai-ngo\out\ngo_partner_frame2\UI_CMN_Chara_RBase_01.png"
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)

# the paintings' colours (k-means of the frame painting) -> texture colours; the painting's mid blue becomes the
# skin's violet #674AFB, and the plate's pink face too, so the game's white letters read on it
PAL = {"g": (4, 248, 8), "W": (251, 251, 253), "N": (5, 10, 138), "L": (214, 213, 250), "B": (3, 161, 253),
       "P": (253, 109, 252), "C": (95, 236, 253), "M": (5, 78, 231), "S": (122, 132, 216)}
KEYS = list(PAL)
OUTCOL = dict(PAL, M=(103, 74, 251), N=(9, 14, 94))


def grid(path, w, h):
    """palette index per texture pixel: snap the painting, then the majority of each pixel's block"""
    raw = np.asarray(Image.open(path).convert("RGB")).astype(float)
    cols = np.array([PAL[k] for k in KEYS], float)
    idx = np.argmin(((raw[:, :, None, :] - cols[None, None]) ** 2).sum(-1), -1)
    ys = np.linspace(0, raw.shape[0], h + 1).astype(int); xs = np.linspace(0, raw.shape[1], w + 1).astype(int)
    g = np.zeros((h, w), int)
    for j in range(h):
        for i in range(w):
            g[j, i] = np.bincount(idx[ys[j]:ys[j + 1], xs[i]:xs[i + 1]].ravel(), minlength=len(KEYS)).argmax()
    return np.array(KEYS)[g]


def rgba(chars, clear):
    a = np.zeros(chars.shape + (4,), np.uint8)
    for k, c in OUTCOL.items(): a[chars == k] = c + (255,)
    a[clear] = 0
    return a


# card frame: source rows -> texture rows
f = grid(FRAME_SRC, 148, 264)
rows = (list(range(0, 17))            # outline, white edge, pink title bar with the three buttons
        + [17] * 27                    # tool strip for LV: the band under the title bar, repeated (rows 17-43)
        + list(range(19, 27))          # cyan line, white, the window's top keyline and stepped corners (44-51)
        + list(range(52, 220))         # window with side rails (52-219)
        + list(range(220, 264)))       # bottom border and the stand tab
fr = f[rows]
assert fr.shape == (264, 148), fr.shape
fr = np.where((fr == "B") & (np.arange(264)[:, None] > 222) & (np.arange(148)[None] > 48) & (np.arange(148)[None] < 99),
              "L", fr)                 # the tab's placeholder blue face -> lilac stand
clear = fr == "g"
inside = np.zeros_like(clear)
for j in range(234, 264):              # under the frame only the stand tab stays: between its left and right outline
    n = np.where(fr[j, 40:108] == "N")[0]
    if len(n): inside[j, 40 + n[0]:40 + n[-1] + 1] = True
clear[234:] = ~inside[234:]
Image.fromarray(rgba(fr, clear)).save(os.path.join(OUT, "UI_CMN_Chara_RFrame_01.png"))
hole = clear.copy(); hole[234:] = False
print("frame: window opening rows", np.where(hole.any(1))[0][[0, -1]], "cols", np.where(hole.any(0))[0][[0, -1]])

# card base: the generated checker under the frame's body only
base = np.array(Image.open(BASE_SRC).convert("RGBA"))
base[234:] = 0
Image.fromarray(base).save(os.path.join(OUT, "UI_CMN_Chara_RBase_01.png"))

# leader LV plate: plate rows 9-35 kept, the star tab under it dropped
p = grid(PLATE_SRC, 144, 64)
p = np.where(p == "P", "M", np.where(p == "B", "C", p))
pc = p == "g"; pc[36:] = True
Image.fromarray(rgba(p, pc)).save(os.path.join(OUT, "UI_CMN_Chara_LFrame_01.png"))
print("wrote", OUT)
