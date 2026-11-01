"""Cut the owner-chosen Codex number font (out/ngo_digits_draw/font_2.png: pink pixel digits, dark purple outline,
0-9 . %) into every stock number atlas. Each stock sprite cell keeps its rect; the new glyph is scaled to the stock
glyph's height in that cell and centred on the stock glyph. Colour tiers stay (the game switches atlases by
achievement): fill recoloured per atlas, outline kept. Shadow atlases (_Base of the score pairs) get a dark
translucent silhouette like the stock; Count atlases are white masks the game tints.
usage: python ngo_digits_from_font.py <out dir>"""
import colorsys, os, sys
import numpy as np
import UnityPy
from PIL import Image, ImageDraw

FONT = r"E:\cxref\maimai-ngo\out\ngo_digits_draw\font_2.png"
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)

# --- glyphs from the font sheet: one row, split on empty columns ---
f = np.array(Image.open(FONT).convert("RGB")).astype(int)
ink = (f.min(2) < 225)
cols = np.where(ink.any(0))[0]
runs, start = [], cols[0]
for a, b in zip(cols, cols[1:]):
    if b - a > 6: runs.append((start, a)); start = b
runs.append((start, cols[-1]))
assert len(runs) == 12, "expected 0-9 . %% , got %d" % len(runs)
glyph = {}
for ch, (x0, x1) in zip("0123456789.%", runs):
    sub = ink[:, x0:x1 + 1]; rows = np.where(sub.any(1))[0]
    rgb = f[rows[0]:rows[-1] + 1, x0:x1 + 1]; m = sub[rows[0]:rows[-1] + 1]
    a = np.where(m, 255, 0).astype(np.uint8)
    glyph[ch] = Image.fromarray(np.dstack([rgb.astype(np.uint8), a]))
OUTLINE = tuple(int(v) for v in np.median(f[ink & (f.sum(2) < 250)], axis=0))

def bar(w, h):   # '-' and the arms of '+', ',' : built from the font's own outline + fill colours
    fill = tuple(int(v) for v in np.median(np.array(glyph["8"])[..., :3][np.array(glyph["8"])[..., 3] > 0].reshape(-1, 3), axis=0))
    im = Image.new("RGBA", (w, h), OUTLINE + (255,)); ImageDraw.Draw(im).rectangle((w // 6, h // 4, w - 1 - w // 6, h - 1 - h // 4), fill=fill + (255,))
    return im
H = glyph["8"].height; T = max(6, H // 5)
glyph["-"] = bar(int(H * 0.6), T)
plus = Image.new("RGBA", (int(H * 0.7), int(H * 0.7)))
hb = bar(plus.width, T); vb = bar(T, plus.height)
plus.alpha_composite(vb, ((plus.width - T) // 2, 0)); plus.alpha_composite(hb, (0, (plus.height - T) // 2)); glyph["+"] = plus
comma = Image.new("RGBA", (glyph["."].width, int(glyph["."].height * 1.8)))
comma.alpha_composite(glyph["."], (0, 0)); comma.alpha_composite(bar(glyph["."].width // 2, comma.height - glyph["."].height), (glyph["."].width // 4, glyph["."].height - 2))
glyph[","] = comma
CELL = {i: c for i, c in enumerate("0123456789+-,.%")}

def recolor(im, rgb):
    """fill pixels (the pink) -> rgb, keeping their lightness steps; outline untouched"""
    a = np.array(im).astype(float); px = a[..., :3] / 255
    out = a.copy()
    th, ts, tv = colorsys.rgb_to_hsv(*[c / 255 for c in rgb])
    for y, x in zip(*np.where(a[..., 3] > 0)):
        h, s, v = colorsys.rgb_to_hsv(*px[y, x])
        if v > 0.45 and s > 0.15:   # pink fill
            r, g, b = colorsys.hsv_to_rgb(th, ts, min(1, tv * v / 0.93))
            out[y, x, :3] = (r * 255, g * 255, b * 255)
    return Image.fromarray(out.astype(np.uint8))
def silhouette(im, rgba):
    a = np.array(im); o = np.zeros_like(a); o[..., :3] = rgba[:3]; o[..., 3] = np.where(a[..., 3] > 0, rgba[3], 0)
    return Image.fromarray(o)

PINK, BLUE, GOLD, RED, VIOLET = None, (126, 200, 245), (255, 222, 96), (255, 92, 150), (205, 160, 255)
STYLE = {   # atlas -> ("color", rgb | None=font pink) | ("shadow",) | ("mask",)
    "UI_NUM_Timer": ("color", PINK), "UI_NUM_Timer_Limit": ("color", RED),
    "UI_NUM_Score_play_achievement": ("color", PINK),
    "UI_Num_Score_1110000_Blue": ("color", BLUE), "UI_Num_Score_1110000_Gold": ("color", GOLD), "UI_Num_Score_1110000_Red": ("color", RED),
    "UI_NUM_Score_0001111_Blue": ("color", BLUE), "UI_NUM_Score_0001111_Gold": ("color", GOLD), "UI_NUM_Score_0001111_Red": ("color", RED),
    "UI_NUM_Score_1110000_Base": ("shadow",), "UI_NUM_Score_0001111_Base": ("shadow",),
    "UI_NUM_Count_1110000_Base": ("mask",), "UI_NUM_Count_1110000_Base02": ("mask",), "UI_NUM_Count_1110000_Upper": ("mask",),
    "UI_NUM_Count_0001111_Base": ("mask",), "UI_NUM_Count_0001111_Base02": ("mask",), "UI_NUM_Count_0001111_Upper": ("mask",),
}
SINGLE = {"UI_NUM_Score_play_achievement_per": ("color", PINK), "UI_RSL_Score_Per_Blue": ("color", BLUE),
          "UI_RSL_Score_Per_Gold": ("color", GOLD), "UI_RSL_Score_Per_Red": ("color", RED)}

def styled(ch, style, stock_cell):
    g = glyph[ch]
    if style[0] == "color": return g if style[1] is None else recolor(g, style[1])
    if style[0] == "mask": return silhouette(g, (255, 255, 255, 255))
    sa = np.array(stock_cell)[..., 3]; level = int(np.median(sa[sa > 10])) if (sa > 10).any() else 120
    return silhouette(g, (42, 27, 61, level))

def place(stock_cell, g):
    """scale g to the stock glyph's height in this cell, centre it on the stock glyph"""
    w, h = stock_cell.size
    bb = stock_cell.getchannel("A").point(lambda v: 255 if v > 24 else 0).getbbox() or (0, 0, w, h)
    th = bb[3] - bb[1]; k = th / g.height
    if g.width * k > w: k = w / g.width
    gi = g.resize((max(1, round(g.width * k)), max(1, round(g.height * k))), Image.NEAREST if k < 1 else Image.LANCZOS)
    cell = Image.new("RGBA", (w, h))
    cx, cy = (bb[0] + bb[2]) / 2, (bb[1] + bb[3]) / 2
    cell.alpha_composite(gi, (int(min(max(cx - gi.width / 2, 0), w - gi.width)), int(min(max(cy - gi.height / 2, 0), h - gi.height))))
    a = np.array(cell); a[..., 3] = np.where(a[..., 3] >= 128, a[..., 3] if style_is_shadow else 255, 0)
    return Image.fromarray(a)

env = UnityPy.load(r"D:\maimai\backup_ngo_skin\resources.assets")
tex = {o.path_id: o for o in env.objects if o.type.name == "Texture2D" and o.peek_name() in set(STYLE) | set(SINGLE)}
sprites = {}
for o in env.objects:
    if o.type.name != "Sprite": continue
    try: s = o.read()
    except Exception: continue
    if s.m_RD.texture.path_id in tex: sprites.setdefault(s.m_RD.texture.path_id, []).append(s)
for pid, o in tex.items():
    t = o.read(); name = t.m_Name; img = t.image.convert("RGBA"); out = Image.new("RGBA", img.size)
    if name in SINGLE:
        style_is_shadow = False
        out.alpha_composite(place(img, styled("%", SINGLE[name], img)), (0, 0))
    else:
        style = STYLE[name]; style_is_shadow = style[0] == "shadow"
        cells = sprites.get(pid, [])
        idxs = {int(s.m_Name.split("_")[-1]) for s in cells}
        cellmap = dict(CELL)
        if 13 not in idxs and 14 in idxs: cellmap[14] = "."   # e.g. UI_Num_Score_1110000_*: slot 13 unused, '.' in 14
        for s in cells:
            idx = int(s.m_Name.split("_")[-1]); ch = cellmap.get(idx)
            r = s.m_Rect; box = (int(r.x), int(img.height - r.y - r.height), int(r.x + r.width), int(img.height - r.y))
            cell = img.crop(box)
            if ch is None or cell.getchannel("A").getbbox() is None: continue   # blank stock cells stay blank
            out.alpha_composite(place(cell, styled(ch, style, cell)), box[:2])
    out.save(os.path.join(OUT, name + ".png")); print(name, img.size)
