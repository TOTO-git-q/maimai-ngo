"""Track-start card: the deluxe tab and the LV plate were still the stock rounded shapes. Rebuild both as small Windose
windows cut from the approved play-screen song plate (out/ngo_end_result UI_UPE_MBase_<DIFF>, its lower window),
9-sliced to size, so they share its outline, bevel, difficulty-coloured title bar and window buttons.
- Tab (420x96): one small window on the raised-label area (x 15..211, full height; the logo sits on it). The stock
  lower strip that ran under the card's own title bar is dropped.
- LV plate (180x112): a small window in the stock plate's area (x 40..180, y 44..112).
usage: python ngo_tst_tabs.py <out dir>"""
import os, sys
import numpy as np
from PIL import Image

SRC = r"E:\cxref\maimai-ngo\out\ngo_end_result\replacements"
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
L, T, R, B = 30, 34, 100, 12   # 9-slice borders in source px; the right one holds the whole window-button block

def window(diff):
    u = Image.open(os.path.join(SRC, "UI_UPE_MBase_%s.png" % diff)).convert("RGBA")
    a = np.array(u)[..., 3] > 20
    rows = np.where(a[150:].any(1))[0] + 150; cols = np.where(a[rows[0]:rows[-1] + 1].any(0))[0]
    return u.crop((cols[0], rows[0], cols[-1] + 1, rows[-1] + 1))

def slice9(w, size, scale=1.0):
    """9-slice w to size; borders scaled by `scale` (keeps outline/title bar proportionate on small targets)"""
    W, H = size; l, t, r, b = [max(1, round(v * scale)) for v in (L, T, R, B)]
    sw, sh = w.size; out = Image.new("RGBA", size)
    xs = [(0, L, 0, l), (L, sw - R, l, W - r), (sw - R, sw, W - r, W)]
    ys = [(0, T, 0, t), (T, sh - B, t, H - b), (sh - B, sh, H - b, H)]
    for sx0, sx1, dx0, dx1 in xs:
        for sy0, sy1, dy0, dy1 in ys:
            if dx1 > dx0 and dy1 > dy0:
                out.alpha_composite(w.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.NEAREST), (dx0, dy0))
    return out

for diff in ("BSC", "ADV", "EXP", "MST", "MST_Re", "UTG", "DMY"):
    w = window(diff)
    tab = Image.new("RGBA", (420, 96)); tab.alpha_composite(slice9(w, (196, 90), 0.5), (15, 6))
    tab.save(os.path.join(OUT, "UI_TST_MBase_%s_Tab.png" % diff))
    lv = Image.new("RGBA", (180, 112)); lv.alpha_composite(slice9(w, (140, 68), 0.5), (40, 44))
    lv.save(os.path.join(OUT, "UI_TST_MBase_LV_%s.png" % diff))
print("written", len(os.listdir(OUT)))
