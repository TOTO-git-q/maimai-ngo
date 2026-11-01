"""Mock of the result song plate per the Fable layout (plate px, 746x164): pink dialog panel (x0-700, y40-160, no title
bar), jacket well (12-100, 56-146), difficulty name in pixel letters at (112, baseline 82) in the difficulty colour,
solid plum title bar (108-700, 98-142), the approved popup window as the LV badge over the top-right corner
(566-746, 0-124) with plum "LV" and pastel-colour pixel digits, and a folder tab dx tag (112-200, 20-46).
Composited from the Codex pieces, then pasted over the old plate in a real capture.
usage: python ngo_plate_mock.py <capture.png> <out.png>"""
import sys
import cv2
import numpy as np
from PIL import Image

X = r"E:\cxref\maimai-ngo\out"
CAP, OUT = sys.argv[1], sys.argv[2]
PLUM = (77, 35, 207); PASTEL = (123, 228, 149); BAR = (68, 196, 65)   # BASIC

def cut(path):
    a = np.array(Image.open(path).convert("RGB")); near = np.all(a > 238, axis=2).astype("uint8")
    _, lab, _, _ = cv2.connectedComponentsWithStats(near, 8); edge = np.unique(np.r_[lab[0], lab[-1], lab[:, 0], lab[:, -1]])
    im = Image.fromarray(np.dstack([a, np.where(np.isin(lab, edge[edge != 0]), 0, 255).astype("uint8")]))
    return im.crop(im.getchannel("A").getbbox())
def fitbox(im, w, h, keep=0.12):
    """scale to height h, then stretch the middle columns to width w (ends kept)"""
    s = im.resize((max(1, round(im.width * h / im.height)), h), Image.LANCZOS)
    l = round(s.width * keep); r = s.width - l; mid = max(1, w - 2 * l)
    o = Image.new("RGBA", (w, h)); o.alpha_composite(s.crop((0, 0, l, h)), (0, 0))
    o.alpha_composite(s.crop((l, 0, r, h)).resize((mid, h), Image.LANCZOS), (l, 0)); o.alpha_composite(s.crop((r, 0, s.width, h)), (l + mid, 0)); return o
def recolor(im, rgb, light=True):
    a = np.array(im).astype(int); m = (a[..., :3].mean(2) > 150) & (a[..., 3] > 0) if light else (a[..., 3] > 0)
    for i in range(3): a[..., i] = np.where(m, rgb[i], a[..., i])
    return Image.fromarray(a.astype(np.uint8))

cap = Image.open(CAP).convert("RGBA")
plate = Image.new("RGBA", (746, 164))
plate.alpha_composite(fitbox(cut(X + r"\ngo_plate_draw\panel_1.png"), 700, 120, 0.2), (0, 40))
# jacket + title text from the capture (runtime elements)
plate.alpha_composite(cap.crop((92, 504, 136, 548)).resize((84, 84), Image.LANCZOS), (14, 58))
bar = Image.new("RGBA", (592, 44), PLUM + (255,)); plate.alpha_composite(bar, (108, 98))
title = cap.crop((240, 528, 330, 546)); ta = np.array(title); wm = ta[..., :3].min(2) > 200
tt = np.zeros_like(ta); tt[wm] = (255, 255, 255, 255); title = Image.fromarray(tt).resize((180, 36), Image.LANCZOS)
plate.alpha_composite(title, (108 + (592 - 180) // 2, 102))
word = recolor(cut(X + r"\ngo_plate_draw\word_basic_1.png"), PASTEL)
word = word.resize((round(word.width * 30 / word.height), 30), Image.LANCZOS); plate.alpha_composite(word, (112, 56))
tab = cut(X + r"\ngo_plate_draw\tab_1.png"); tab = tab.resize((96, round(tab.height * 96 / tab.width)), Image.LANCZOS)
plate.alpha_composite(tab, (112, 46 - tab.height + 4))
dx = cap.crop((330, 508, 376, 520)).resize((64, 17), Image.LANCZOS); plate.alpha_composite(dx, (120, 46 - tab.height + 6))
# LV badge
win = cut(X + r"\ngo_lv_draw2\lv_1.png"); t = np.array(win).astype(int)
ref = t[round(win.height * .12), round(win.width * .3), :3]
isbar = (np.abs(t[..., :3] - ref).sum(2) < 60) & (np.arange(win.height)[:, None] < win.height * .3)
shade = t[..., :3].mean(2) / ref.mean()
for i in range(3): t[..., i] = np.where(isbar, np.clip(BAR[i] * shade, 0, 255), t[..., i])
win = Image.fromarray(t.astype(np.uint8)).resize((180, 124), Image.LANCZOS); plate.alpha_composite(win, (566, 0))
font = cut(X + r"\ngo_digits_draw\font_2.png"); fa = np.array(font)[..., 3] > 0; cols = np.where(fa.any(0))[0]
runs, st = [], cols[0]
for p, q in zip(cols, cols[1:]):
    if q - p > 6: runs.append((st, p)); st = q
runs.append((st, cols[-1]))
six = font.crop((runs[6][0], 0, runs[6][1] + 1, font.height)); six = six.crop(six.getchannel("A").getbbox())
six = recolor(six, PASTEL); six = six.resize((round(six.width * 56 / six.height), 56), Image.NEAREST)
plate.alpha_composite(six, (640, 104 - 56))
lv = cap.crop((383, 510, 404, 523)); la = np.array(lv); lm = la[..., :3].min(2) > 170
lo = np.zeros_like(la); lo[lm] = PLUM + (255,); lv = Image.fromarray(lo).resize((34, 22), Image.NEAREST)
plate.alpha_composite(lv, (590, 104 - 22))
plate.save(OUT.replace(".png", "_plate.png"))
# into the capture (plate canvas drawn at half scale over the old plate)
scr = cap.copy()
old = (86, 478, 459, 558)
# hide the old plate: repeat the background band just above it (mock only; in game the texture is replaced)
band = scr.crop((old[0] - 4, old[1] - 28, old[2] + 4, old[1] - 8))
for y in range(old[1] - 8, old[1] + 22, band.height): scr.paste(band.crop((0, 0, band.width, min(band.height, old[1] + 22 - y))), (old[0] - 4, y))
scr.alpha_composite(plate.resize((373, 82), Image.LANCZOS), (old[0], old[1] - 2))
scr.save(OUT); print(OUT)
