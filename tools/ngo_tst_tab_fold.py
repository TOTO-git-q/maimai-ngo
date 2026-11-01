"""Track-start deluxe tab as ONE folder tab growing out of the card's top-left edge, so tab and card read as one
window (the layered select-card tab left two green bands and doubled outlines). Matches the card body
(out/ngo_card_frames UI_TST_MBase_<D>): 2 px plum outline, 2 px white bevel, the card's own title-bar green.
Geometry (tab texture 420x96 is drawn 54 px above the card top; card outline at card y 16 -> tab y 70, title bar
from card y 20 -> tab y 74; card left outline x 16-17; the game's "でらっくす" logo covers tab x 13-213, y 3-67).
usage: python ngo_tst_tab_fold.py <out dir>"""
import os, sys
import numpy as np
from PIL import Image

C = r"E:\cxref\maimai-ngo\out\ngo_card_frames\replacements"
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
PLUM, WHITE = (77, 35, 207, 255), (255, 255, 255, 255)
X0, X1, TOP, JOIN, BAR = 16, 224, 0, 70, 74       # bump x range, top row, card-top row, title-bar row
STEP = [6, 4, 2]                                    # stepped (pixel) top corners: inset per 2-px row

for d in ["BSC", "ADV", "EXP", "MST", "MST_Re", "UTG", "DMY"]:
    card = np.array(Image.open(os.path.join(C, "UI_TST_MBase_%s.png" % d)).convert("RGBA"))
    fill = tuple(int(v) for v in card[24, 60]) # title-bar colour of this difficulty
    a = np.zeros((96, 420, 4), np.uint8)
    for y in range(TOP, BAR):
        r = (y - TOP) // 2
        inset = STEP[r] if r < len(STEP) else 0
        l, rr = X0 + inset, X1 - inset          # outer bounds of this row
        a[y, l:rr] = fill
        a[y, l:l + 2] = PLUM; a[y, rr - 2:rr] = PLUM                  # side outline
        if y < JOIN: a[y, l + 2:l + 4] = WHITE                        # left bevel (the card's bevel continues below)
    for y in range(TOP, TOP + 2): a[y, X0 + STEP[0]:X1 - STEP[0]] = PLUM          # top outline
    for i, s in enumerate(STEP):                                                   # stepped corner outline
        y = TOP + 2 * i
        a[y:y + 2, X0 + s:X0 + s + (STEP[i - 1] - s if i else 0) + 2] = PLUM
        a[y:y + 2, X1 - s - (STEP[i - 1] - s if i else 0) - 2:X1 - s] = PLUM
    a[TOP + 2:TOP + 4, X0 + STEP[0] + 2:X1 - STEP[0] - 2] = WHITE                  # top bevel
    a[JOIN:BAR, X0 + 2:X0 + 4] = WHITE                   # card bevel continues
    a[JOIN:BAR, X1 - 2:X1] = fill                         # open the joint: the card's top outline runs under, not through
    Image.fromarray(a).save(os.path.join(OUT, "UI_TST_MBase_%s_Tab.png" % d))
print("tabs written to", OUT)
