"""Track-start song card = the music-select card (owner: "和外面一样"): the track-start textures take the music-select
card's pieces (out/ngo_card_frames): its tab (Tab_01) for the deluxe tab, its LV bump (LvBase) for the LV plate, and
its difficulty strip with the word (Text) in place of the word baked into the track-start card. Layout from the
in-game rects: TST card 420x636 (tab 420x96 drawn 42 px above the card top, LV plate 180x112 at card x 223 / y 307),
the MSS strip sits right under the jacket and runs under the LV bump.
usage: python ngo_tst_like_select.py <out dir>"""
import os, sys
import numpy as np
from PIL import Image

C = r"E:\cxref\maimai-ngo\out\ngo_card_frames\replacements"
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
DIFFS = ["BSC", "ADV", "EXP", "MST", "MST_Re", "UTG", "DMY"]

def load(n):
    p = os.path.join(C, n + ".png")
    return Image.open(p).convert("RGBA") if os.path.exists(p) else None

for d in DIFFS:
    tab = load("UI_MSS_MBase_%s_Tab_01" % d)
    if tab is not None:   # 284x68 -> fills the 420 width, bottom aligned like the select card's tab
        t = tab.resize((420, round(68 * 420 / 284)), Image.LANCZOS)
        out = Image.new("RGBA", (420, 96)); out.alpha_composite(t.crop((0, max(0, t.height - 96), 420, t.height)), (0, max(0, 96 - t.height)))
        # the select tab carries its own window buttons; the card body's title bar already has them (two sets showed)
        o = np.array(out); green = o[o.shape[0] - 6, 60, :3].copy()
        right = o[:, 250:]; btn = (right[..., 3] > 0) & (np.abs(right[..., :3].astype(int) - green.astype(int)).sum(2) > 60)
        right[btn, :3] = green; out = Image.fromarray(o)
        out.save(os.path.join(OUT, "UI_TST_MBase_%s_Tab.png" % d))
    lv = load("UI_MSS_MBase_LvBase_%s" % d)
    if lv is not None:
        lv.resize((180, 112), Image.LANCZOS).save(os.path.join(OUT, "UI_TST_MBase_LV_%s.png" % d))
    base, word = load("UI_TST_MBase_%s" % d), load("UI_MSS_MBase_%s_Text" % d)
    if base is not None and word is not None:
        a = np.array(base)
        face = a[600, 210].copy()                 # card body colour below the title bar
        row = np.where((np.abs(a[600, :, :3].astype(int) - face[:3]).sum(1) < 12))[0]   # inner body columns
        x0, x1 = int(row.min()), int(row.max()) + 1
        a[366:418, x0:x1] = face                  # wipe the baked word (rows 372-406; jacket well ends 364, title bar 420)
        b = Image.fromarray(a)
        strip = word.resize((x1 - x0, 46), Image.LANCZOS)   # 240x28 strip, across the card body
        b.alpha_composite(strip, (x0, 368))          # right under the jacket, like the select card
        b.save(os.path.join(OUT, "UI_TST_MBase_%s.png" % d))
    print(d, "tab" if tab is not None else "-", "lv" if lv is not None else "-", "word" if word is not None else "-")
