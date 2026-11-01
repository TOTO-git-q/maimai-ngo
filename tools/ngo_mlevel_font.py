"""Level number atlases (UI_NUM_MLevel_01..05, _10) in the owner-chosen Codex pixel number font (font_2), coloured per
difficulty like the official: fill = the difficulty colour, outline = its dark shade (same palette as ngo_upe_hud.py).
Cells: 0-9 digits, 10 '+', 11 '-', 12 ',', 13 '.', 14 'LV'. Each new glyph is scaled to the stock glyph's height in
its cell and centred on it, so every screen that shows a level keeps its layout.
usage: python ngo_mlevel_font.py <out dir> [preview.png]"""
import os, sys
import numpy as np
import UnityPy
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

FONT = r"E:\cxref\maimai-ngo\out\ngo_digits_draw\font_2.png"
LVFONT = r"C:\Windows\Fonts\ARLRDBD.TTF"
COLORS = {  # atlas -> fill, outline
    "UI_NUM_MLevel_01": ((69, 193, 36), (14, 100, 48)),
    "UI_NUM_MLevel_02": ((255, 186, 1), (170, 70, 12)),
    "UI_NUM_MLevel_03": ((255, 90, 102), (160, 20, 52)),
    "UI_NUM_MLevel_04": ((176, 96, 236), (90, 20, 141)),
    "UI_NUM_MLevel_05": ((240, 226, 255), (120, 40, 200)),
    "UI_NUM_MLevel_10": ((250, 120, 250), (150, 30, 170)),
}
CELL = {i: c for i, c in enumerate("0123456789+-,.")}
CELL[14] = "LV"

f = np.array(Image.open(FONT).convert("RGB")).astype(int)
ink = f.min(2) < 225
cols = np.where(ink.any(0))[0]
runs, start = [], cols[0]
for a, b in zip(cols, cols[1:]):
    if b - a > 6: runs.append((start, a)); start = b
runs.append((start, cols[-1]))
glyph = {}
for ch, (x0, x1) in zip("0123456789.%", runs):
    sub = ink[:, x0:x1 + 1]; rows = np.where(sub.any(1))[0]
    rgb = f[rows[0]:rows[-1] + 1, x0:x1 + 1]; m = sub[rows[0]:rows[-1] + 1]
    glyph[ch] = (rgb, m)
H = glyph["8"][1].shape[0]
U = max(2, H // 14)                       # the font's pixel unit, roughly


def outline_fill(mask):
    """a mask -> (rgb, mask) in the font's look: pink fill, 1-unit dark outline"""
    big = ndimage.binary_dilation(mask, np.ones((2 * U + 1, 2 * U + 1)))
    rgb = np.zeros(big.shape + (3,), int); rgb[big] = (60, 20, 90); rgb[mask] = (255, 140, 200)
    return rgb, big


def snapped(text, height):
    fo = ImageFont.truetype(LVFONT, int(height * 1.4))
    l, t, r, b = fo.getbbox(text)
    im = Image.new("L", (r - l + 4, b - t + 4)); ImageDraw.Draw(im).text((-l + 2, -t + 2), text, font=fo, fill=255)
    a = np.asarray(im) > 128; a = a[np.any(a, 1)][:, np.any(a, 0)]
    h, w = a.shape; a = np.pad(a, ((0, -h % U), (0, -w % U)))
    s = a.reshape(a.shape[0] // U, U, a.shape[1] // U, U).mean(axis=(1, 3)) > .5
    return np.kron(s, np.ones((U, U), bool))


def bar(w, h):
    m = np.ones((h, w), bool); return outline_fill(np.pad(m, U))


glyph["-"] = bar(int(H * .45), U * 3)
pl = np.zeros((int(H * .55),) * 2, bool); c = pl.shape[0] // 2
pl[c - U:c + U, :] = True; pl[:, c - U:c + U] = True
glyph["+"] = outline_fill(np.pad(pl, U))
cm = np.zeros((U * 6, U * 3), bool); cm[:U * 3] = True; cm[U * 3:, U:U * 2 + 1] = True
glyph[","] = outline_fill(np.pad(cm, U))
glyph["LV"] = outline_fill(np.pad(snapped("LV", H * .8), U))


def colored(ch, fill, line):
    rgb, m = glyph[ch]
    v = rgb.max(2) / 255.; s = (rgb.max(2) - rgb.min(2)) / np.maximum(rgb.max(2), 1)
    is_fill = m & (v > .45) & (s > .12) | m & (v > .85)
    shade = np.clip(v / max(.01, np.median(v[is_fill]) if is_fill.any() else 1), .75, 1.15)
    out = np.zeros(m.shape + (4,), np.uint8)
    out[m] = line + (255,)
    fc = np.clip(np.array(fill)[None, None, :] * shade[..., None], 0, 255).astype(np.uint8)
    out[is_fill, :3] = fc[is_fill]
    return Image.fromarray(out)


def place(cell, g):
    w, h = cell.size
    bb = cell.getchannel("A").point(lambda v: 255 if v > 24 else 0).getbbox() or (0, 0, w, h)
    k = (bb[3] - bb[1]) / g.height
    if g.width * k > w: k = w / g.width
    gi = g.resize((max(1, round(g.width * k)), max(1, round(g.height * k))), Image.NEAREST)
    o = Image.new("RGBA", (w, h)); cx, cy = (bb[0] + bb[2]) / 2, (bb[1] + bb[3]) / 2
    o.alpha_composite(gi, (int(min(max(cx - gi.width / 2, 0), w - gi.width)), int(min(max(cy - gi.height / 2, 0), h - gi.height))))
    return o


if __name__ == "__main__":
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    env = UnityPy.load(r"D:\maimai\backup_ngo_skin\resources.assets")
    tex = {o.path_id: o for o in env.objects if o.type.name == "Texture2D" and o.peek_name() in COLORS}
    sprites = {}
    for o in env.objects:
        if o.type.name != "Sprite": continue
        try: s = o.read()
        except Exception: continue
        if s.m_RD.texture.path_id in tex: sprites.setdefault(s.m_RD.texture.path_id, []).append(s)
    previews = []
    for pid, o in tex.items():
        t = o.read(); name = t.m_Name; img = t.image.convert("RGBA"); res = Image.new("RGBA", img.size)
        fill, line = COLORS[name]
        for s in sprites[pid]:
            idx = int(s.m_Name.split("_")[-1]); ch = CELL.get(idx)
            r = s.m_Rect; box = (int(r.x), int(img.height - r.y - r.height), int(r.x + r.width), int(img.height - r.y))
            cell = img.crop(box)
            if ch is None or cell.getchannel("A").getbbox() is None: continue
            res.alpha_composite(place(cell, colored(ch, fill, line)), box[:2])
        res.save(os.path.join(out, name + ".png")); previews.append(res)
    if len(sys.argv) > 2:
        p = Image.new("RGBA", (202 * len(previews), 240), (255, 255, 255, 255))
        for i, im in enumerate(previews): p.alpha_composite(im, (i * 202, 0))
        p.save(sys.argv[2])
