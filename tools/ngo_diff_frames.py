"""One NGO look for every per-difficulty song frame: gameplay upper panel (UI_UPE_*), music-select card (UI_MSS_MBase_*)
and track-start card (UI_TST_MBase_*). Each stock texture keeps its silhouette and inner structure (jacket hole, name
strips, white info panel); every stock pixel is classed on a P-pixel grid and repainted in one flat pixel style:
frame = difficulty colour with the same diagonal light stripes, pixel hearts, white top bevel and dark outline; the
navy strips become the NGO deep-purple "screen"; white panels stay pale. Difficulty words are redrawn in one rounded
pixel lettering. The gameplay panel's jacket frame and bar are drawn on one canvas so the pattern runs across them.
The score gauge at the top of the play circle (UI_GAM_Gauge_*, UI_GAM_Achieve_Base) is made here too, in the same
style: see gauge(). Install: tools/ngo_frames_apply.sh (also run at the end of ngo_endflow_apply.sh).
usage: python ngo_diff_frames.py <out dir> [preview.png]"""
import os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

REF = r"E:\cxref\maimai-ngo\tasks\refs_endflow"
FONT = r"C:\Windows\Fonts\ARLRDBD.TTF"   # Arial Rounded Bold: close to the official rounded heavy word, snapped to the grid
P = 4
SCREEN, SCREEN_DK, PANEL = (59, 31, 150), (38, 16, 104), (255, 247, 252)
DIFFS = {  # main, dark, light, word
    "BSC": ((69, 193, 36), (14, 100, 48), (150, 226, 110), "BASIC"),
    "ADV": ((255, 186, 1), (170, 70, 12), (255, 226, 90), "ADVANCED"),
    "EXP": ((255, 90, 102), (160, 20, 52), (255, 150, 160), "EXPERT"),
    "MST": ((159, 81, 220), (90, 20, 141), (196, 140, 240), "MASTER"),
    "MST_Re": ((210, 160, 255), (120, 40, 200), (240, 224, 255), "Re:MASTER"),
    "UTG": ((250, 120, 250), (150, 30, 170), (255, 196, 255), "U-TA-GE"),
    "DMY": ((170, 170, 170), (96, 96, 96), (210, 210, 210), "DUMMY"),
}
HEART = np.array([[0, 1, 0, 1, 0], [1, 1, 1, 1, 1], [1, 1, 1, 1, 1], [0, 1, 1, 1, 0], [0, 0, 1, 0, 0]], bool)
T, FRAME, HOLE, NAVY, NAVY_DK, WHITE = range(6)


def cells(a, f):
    """reduce an HxW label image to the P grid by majority vote, back to full size"""
    h, w = a.shape
    a = np.pad(a, ((0, -h % P), (0, -w % P)))
    g = a.reshape(a.shape[0] // P, P, a.shape[1] // P, P).transpose(0, 2, 1, 3).reshape(a.shape[0] // P, a.shape[1] // P, -1)
    out = np.apply_along_axis(lambda v: np.bincount(v, minlength=6).argmax(), 2, g)
    return np.kron(out, np.ones((P, P), int))[:h, :w] if f else out


def classify(rgba):
    a = rgba.astype(int); r, g, b, al = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
    mx, mn = a[..., :3].max(-1), a[..., :3].min(-1); s = (mx - mn) / np.maximum(mx, 1)
    lab = np.full(al.shape, FRAME)
    lab[al < 128] = T
    lab[(al >= 128) & (mx < 80) & (s < .35)] = HOLE
    navy = (al >= 128) & (b > r + 40) & (b >= g) & (mx < 175) & (g < b - 20)
    lab[navy] = NAVY
    lab[navy & (mx < 105)] = NAVY_DK
    lab[(al >= 128) & (s < .1) & (mn > 215)] = WHITE
    return lab


def word_mask(text, height):
    f = ImageFont.truetype(FONT, int(height * 1.3))
    gap = 3 * P
    widths = [f.getbbox(ch)[2] - f.getbbox(ch)[0] if ch not in " -" else P * 2 for ch in text]
    im = Image.new("L", (sum(widths) + gap * len(text) + 8, int(height * 2))); dr = ImageDraw.Draw(im)
    x = 4
    for ch, w in zip(text, widths):
        if ch == "-":   # the U·TA·GE middle dot
            dr.rectangle((x, int(height * .62), x + P * 2 - 1, int(height * .62) + P * 2 - 1), fill=255)
        elif ch != " ":
            dr.text((x - f.getbbox(ch)[0], 4), ch, font=f, fill=255)
        x += w + gap
    a = np.asarray(im) > 128
    a = a[np.any(a, 1)][:, np.any(a, 0)]
    h, w = a.shape; a = np.pad(a, ((0, -h % P), (0, -w % P)))
    s = a.reshape(a.shape[0] // P, P, a.shape[1] // P, P).mean(axis=(1, 3)) > .5
    return np.kron(s, np.ones((P, P), bool))


def stamp_word(img, d, x0, y0, height, max_w=None):
    main, dark, light, word = DIFFS[d]
    wm = word_mask(word, height)
    if max_w and wm.shape[1] > max_w:
        k = max_w / wm.shape[1]
        wm = np.asarray(Image.fromarray(wm.astype(np.uint8) * 255).resize((max_w, max(P, int(wm.shape[0] * k))), Image.NEAREST)) > 0
    H, W = img.shape[:2]
    full = np.zeros((H, W), bool)
    hh, ww = min(wm.shape[0], H - y0), min(wm.shape[1], W - x0)
    full[y0:y0 + hh, x0:x0 + ww] = wm[:hh, :ww]
    out = ndimage.binary_dilation(full, np.ones((2 * P + 1, 2 * P + 1)))
    sh = np.roll(np.roll(out, P, 0), P, 1)
    img[sh & ~out] = dark + (255,)
    img[out] = dark + (255,)
    img[full] = (255, 255, 255, 255)


def ngoify(rgba, d, word_at=None, min_panel=2500, pal=None, lab=None):
    """rgba: stock pixels. word_at: (x, y, height) where the difficulty word is redrawn; white blobs smaller than
    min_panel px (the stock word) are folded into the frame first. pal: (main, dark, light) instead of DIFFS[d];
    lab: a ready label image instead of classifying rgba."""
    main, dark, light = (pal or DIFFS[d])[:3]
    if lab is None:
        lab = classify(rgba)
        wl, n = ndimage.label(lab == WHITE)
        for i in range(1, n + 1):                     # stock word letters / glints: small white islands -> frame
            if (wl == i).sum() < min_panel: lab[wl == i] = FRAME
    lab = cells(lab, True)
    H, W = lab.shape
    img = np.zeros((H, W, 4), np.uint8)
    frame = lab == FRAME; shape = lab != T
    img[frame] = main + (255,)
    yy, xx = np.mgrid[0:H, 0:W]; gy, gx = yy // P, xx // P
    band = (gx + gy) % 64
    img[frame & ((band < 6) | ((band >= 9) & (band < 11)))] = light + (255,)
    for cy in range(4, H // P - 6, 22):               # hearts on a lattice, only where one fits inside the frame
        for cx in range(6 + (cy // 22 % 2) * 17, W // P - 6, 34):
            sub = frame[cy * P:(cy + 5) * P:P, cx * P:(cx + 5) * P:P]
            if sub.shape == (5, 5) and sub.all():
                m = np.zeros((H, W), bool)
                m[cy * P:(cy + 5) * P, cx * P:(cx + 5) * P] = np.kron(HEART, np.ones((P, P), bool))
                img[m & frame] = light + (255,)
    img[lab == HOLE] = rgba[lab == HOLE]
    img[lab == NAVY] = SCREEN + (255,)
    img[lab == NAVY_DK] = SCREEN_DK + (255,)
    img[lab == WHITE] = PANEL + (255,)
    # bevel on the frame: white top-left, dark bottom-right; dark line where the frame meets a panel; dark outline
    up = frame & ~np.roll(frame, P, 0); lf = frame & ~np.roll(frame, P, 1)
    dn = frame & ~np.roll(frame, -P, 0); rt = frame & ~np.roll(frame, -P, 1)
    img[up | lf] = (255, 255, 255, 255)
    img[dn | rt] = dark + (255,)
    panel = shape & ~frame
    seam = frame & ndimage.binary_dilation(panel, np.ones((2 * P + 1, 2 * P + 1)))
    img[seam] = dark + (255,)
    edge = shape & ~ndimage.binary_erosion(shape, np.ones((2 * P + 1, 2 * P + 1)))
    img[edge] = dark + (255,)
    if word_at: stamp_word(img, d, *word_at)
    return img


def load(path):
    return np.asarray(Image.open(path).convert("RGBA"))


def upe(d, out):
    """gameplay upper panel: jacket frame + bar on one canvas (in-game offsets: jacket rect 286 px left, 22 px lower)"""
    j = load(os.path.join(REF, "gametop", "UI_UPE_MusicJacket_Base_%s.png" % d))
    m = load(os.path.join(REF, "gametop", "UI_UPE_MBase_%s.png" % d))
    W, H = max(316, 286 + m.shape[1]), max(22 + j.shape[0], m.shape[0])
    can = np.zeros((H, W, 4), np.uint8)
    can[22:22 + j.shape[0], 0:j.shape[1]][j[..., 3] > 0] = j[j[..., 3] > 0]
    can[0:m.shape[0], 286:286 + m.shape[1]][m[..., 3] > 0] = m[m[..., 3] > 0]
    img = ngoify(can, d, word_at=(286 + 40, 22, 44))
    main, dark, light, _ = DIFFS[d]
    # the official knob (top-right circle) holds the level number: white disc ringed in the dark shade
    al = m[..., 3] > 128; cols = np.where(al[:20].any(0))[0]
    if len(cols):
        cx, cy = 286 + int(cols.mean()), 58
        yy, xx = np.mgrid[0:H, 0:W]
        disc = (yy - cy) ** 2 + (xx - cx) ** 2 <= 44 ** 2
        disc = cells(disc.astype(int), True).astype(bool) & (img[..., 3] > 0)
        ring = disc & ~ndimage.binary_erosion(disc, np.ones((2 * P + 1, 2 * P + 1)))
        img[disc] = dark + (255,) if d == "MST_Re" else (255, 255, 255, 255)   # Re:MASTER digits are near white
        img[ring] = dark + (255,)
    # jacket well: dark line around the jacket, white inner line like the official
    jx, jy = 0, 22
    img[jy + 34:jy + 255, jx + 48:jx + 269] = dark + (255,)
    img[jy + 38:jy + 251, jx + 52:jx + 265] = (255, 255, 255, 255)
    im = Image.fromarray(img)
    im.crop((0, 22, j.shape[1], 22 + j.shape[0])).save(os.path.join(out, "UI_UPE_MusicJacket_Base_%s.png" % d))
    im.crop((286, 0, 286 + m.shape[1], m.shape[0])).save(os.path.join(out, "UI_UPE_MBase_%s.png" % d))
    return im


# score gauge at the top of the play circle (GameUpperGaugeCtrl). Offsets of each piece's top-left on the band's rect,
# from the gameplay UI dump (Base 337x82 at x -708 / bottom 41; Line +4,+5; Color +4,+6; Achieve +228,+10; the judge
# count panels Judge_B -702 / bottom 48, 324x68 -> +6,+7, Judge_A assumed at the same place).
# Which pieces show depends on the player's option: achievement (gauge + rank + Achieve_Base) or judge counts
# (Achieve_Base_02 with CRITICAL, _01 without; they cover the gauge, only the band's edge stays visible).
GAUGE_AT = {"UI_GAM_Gauge_Base": (0, 0), "UI_GAM_Gauge_Line": (4, 5), "UI_GAM_Gauge_Color": (4, 6),
            "UI_GAM_Achieve_Base": (228, 10), "UI_GAM_Achieve_Base_01": (6, 7), "UI_GAM_Achieve_Base_02": (6, 7)}
# band: the stock texture is a mask the game tints at run time (Image.color #EF8CB8 in play although the prefab holds
# white, so it is set from code / animation; keep that role). Its stock values are kept: body light grey, outline
# blue; light (stripes, hearts, bevel) = white. In play: body (210,123,161), light #EF8CB8, outline (45,35,127).
GAUGE = ((224, 224, 224), (48, 64, 176), (255, 255, 255))
GAUGE_TINT = (239, 140, 184)
LINE_DK, TROUGH = (30, 12, 90), SCREEN_DK          # screen_box border; the gauge's empty part (stock black)
PIXFONT = r"E:\cxref\amuproj\app\assets\dialog\PixelMplus12-Regular.ttf"  # the in-game pixel font (ngo_font_data.py)


def gauge(out):
    """The textures are cut from one canvas on one P grid, so band, gauge frame, achievement box and judge panels
    share the pixel steps. Stock roles are kept: Base = mask tinted by the game, Line = frame over the fill, Color = white
    fill mask (tinted by the game per rank; its empty part, stock black, is tinted TROUGH at install), Achieve_Base =
    box under the achievement number, Achieve_Base_01/02 = judge count panels.
    Look: band like every song frame (stripes, hearts, top bevel, dark outline); gauge frame and boxes as the stock
    pair (light rim + dark inside) in white around the deep-purple screen of the score boxes; the stock "CLEAR" mark
    set in the pixel font."""
    g = {n: load(os.path.join(REF, "gametop", n + ".png")) for n in GAUGE_AT}
    H, W = g["UI_GAM_Gauge_Base"].shape[:2]
    one = np.ones((2 * P + 1, 2 * P + 1))

    def canvas(m, n):
        x, y = GAUGE_AT[n]; c = np.zeros((H, W), bool)
        c[y:y + m.shape[0], x:x + m.shape[1]] = m[:H - y, :W - x]
        return c

    def snap(m):
        return cells(m.astype(int), True).astype(bool)

    def cut(img, n):
        x, y = GAUGE_AT[n]; h, w = g[n].shape[:2]
        return Image.fromarray(img[y:y + h, x:x + w])

    # band
    band = snap(canvas(g["UI_GAM_Gauge_Base"][..., 3] > 128, "UI_GAM_Gauge_Base"))
    base = ngoify(g["UI_GAM_Gauge_Base"], None, pal=GAUGE, lab=np.where(band, FRAME, T))
    # gauge frame: white rim around an open trough (stock: light rim + a black line that merges with the black
    # trough); the CLEAR divider is a white column
    ln = g["UI_GAM_Gauge_Line"]; al = ln[..., 3] > 128
    S = snap(canvas(ndimage.binary_fill_holes(al), "UI_GAM_Gauge_Line"))
    e1 = ndimage.binary_erosion(S, one)
    line = np.zeros((H, W, 4), np.uint8)
    line[S & ~e1] = (255, 255, 255, 255)
    light = al & (ln[..., :3].mean(-1) > 128)
    cols = [x for x in range(20, ln.shape[1] - 20) if light[:, x].sum() > 25]   # stock divider: x 129-131
    div = np.zeros(ln.shape[:2], bool); div[:, min(cols):max(cols) + 1] = True
    div = snap(canvas(div, "UI_GAM_Gauge_Line")) & e1
    line[div] = (255, 255, 255, 255)
    # CLEAR: the stock word runs top to bottom just right of the divider; same place, pixel font, hard pixels,
    # letters set 1 px apart so the word fits the trough
    f = ImageFont.truetype(PIXFONT, 12)
    glyphs = []
    for ch in "CLEAR":
        im = Image.new("L", (16, 16)); dr = ImageDraw.Draw(im); dr.fontmode = "1"; dr.text((2, 0), ch, font=f, fill=255)
        a = np.asarray(im) > 128; glyphs.append(a[:, np.any(a, 0)])
    wh = np.concatenate([np.pad(gl, ((0, 0), (0, 1))) for gl in glyphs], 1)[:, :-1]
    wh = wh[np.any(wh, 1)]
    wm = np.rot90(wh, -1)                                   # 90 degrees clockwise: reads top to bottom
    x0 = np.where(div.any(0))[0].max() + 3
    rows = np.where(e1[:, x0:x0 + wm.shape[1]].all(1))[0]  # trough rows that fit the word's width
    y0 = int(round(rows.mean() - wm.shape[0] / 2))
    word = np.zeros((H, W), bool); word[y0:y0 + wm.shape[0], x0:x0 + wm.shape[1]] = wm
    line[word & e1] = (255, 255, 255, 255)
    # fill mask: everything inside the white rim, so no stock soft edge shows
    color = np.zeros((H, W, 4), np.uint8); color[e1] = (255, 255, 255, 255)
    # achievement box: the same white rim around the score boxes' screen (screen_box: LINE_DK border, SCREEN face)
    A = snap(canvas(g["UI_GAM_Achieve_Base"][..., 3] > 128, "UI_GAM_Achieve_Base"))
    a1 = ndimage.binary_erosion(A, one); a2 = ndimage.binary_erosion(a1, one)
    box = np.zeros((H, W, 4), np.uint8)
    box[A] = (255, 255, 255, 255); box[a1] = LINE_DK + (255,); box[a2] = SCREEN + (255,)
    for img, n in ((base, "UI_GAM_Gauge_Base"), (line, "UI_GAM_Gauge_Line"), (color, "UI_GAM_Gauge_Color"),
                   (box, "UI_GAM_Achieve_Base")):
        cut(img, n).save(os.path.join(out, n + ".png"))
    # judge count panels: stock = black face, light rim, light dividers between the counters (5 or 4 columns) and a
    # thin coloured capsule per column for its label (the label text carries the judge colour). Same rim and screen
    # as the achievement box; dividers white; capsules LINE_DK.
    judges = []
    for n in ("UI_GAM_Achieve_Base_02", "UI_GAM_Achieve_Base_01"):
        s = g[n].astype(int); al = s[..., 3] > 128
        mx, mn = s[..., :3].max(-1), s[..., :3].min(-1)
        light = al & (mx - mn < 40) & (mn > 150)
        lab, k = ndimage.label(light)
        main = np.argmax(np.bincount(lab.ravel())[1:]) + 1          # the rim; the dividers are tall thin islands
        dv = np.zeros(al.shape, bool)
        for i in range(1, k + 1):
            ys, xs = np.where(lab == i)
            if i != main and np.ptp(ys) >= 20 and np.ptp(xs) <= 8: dv |= lab == i
        fl, fk = ndimage.label(al & (mx <= 45))                       # dark: the face + one island per capsule
        sz = np.bincount(fl.ravel()); caps = np.zeros(al.shape, bool)
        for i in range(1, fk + 1):
            if 200 < sz[i] < 2000: caps |= ndimage.binary_dilation(fl == i, iterations=2)   # with its thin outline
        # the stock bottom edge dips a few px between the capsules; on the P grid those notches only make noise
        A = snap(canvas(ndimage.binary_closing(np.pad(al, 13), np.ones((1, 25)))[13:-13, 13:-13], n))
        A = ndimage.binary_opening(A, np.ones((2 * P, 2 * P)))    # no single-cell nubs on the outline
        a1 = ndimage.binary_erosion(A, one)
        cap = snap(canvas(caps, n)) & a1
        panel = np.zeros((H, W, 4), np.uint8)
        panel[A] = (255, 255, 255, 255); panel[a1] = SCREEN + (255,); panel[cap] = LINE_DK + (255,)
        dvc = canvas(dv, n); cell_dv = np.zeros((H, W), bool)
        for y in range(0, H, P):                                     # one cell per row band, at the line's centre
            xs = np.where(dvc[y:y + P].any(0))[0]
            for grp in np.split(xs, np.where(np.diff(xs) > 4)[0] + 1):
                if len(grp): cx = int(grp.mean()) // P * P; cell_dv[y:y + P, cx:cx + P] = True
        cell_dv &= a1 & ~cap
        face = a1 & ~cap
        dl, dk = ndimage.label(cell_dv, np.ones((3, 3)))
        for i in range(1, dk + 1):                                   # run each divider on to the rim / capsule
            ys, xs = np.where(dl == i)
            for y, step in ((ys.max() + 1, P), (ys.min() - P, -P)):   # from its lowest cell down, its highest up
                cx = xs[ys == (ys.max() if step > 0 else ys.min())].min() // P * P
                while 0 <= y < H and face[y, cx]: cell_dv[y:y + P, cx:cx + P] = True; y += step
        panel[cell_dv & face] = (255, 255, 255, 255)
        cut(panel, n).save(os.path.join(out, n + ".png")); judges.append(panel)
    # preview as the game draws it: band (tinted), trough (fill mask tinted TROUGH), frame, box; then band + judge panel
    base = base.copy(); base[..., :3] = (base[..., :3] * (np.array(GAUGE_TINT) / 255.0)).round().astype(np.uint8)
    pv = Image.fromarray(base); tr = color.copy(); tr[..., :3] = TROUGH
    for layer in (tr, line, box): pv.alpha_composite(Image.fromarray(layer))
    both = Image.new("RGBA", (W, H * 3 + 8))
    both.alpha_composite(pv, (0, 0))
    for i, jp in enumerate(judges):
        b = Image.fromarray(base); b.alpha_composite(Image.fromarray(jp)); both.alpha_composite(b, (0, (H + 4) * (i + 1)))
    return both


def screen_box(size, border, out_name, out, color=SCREEN):
    """the navy sliced boxes -> deep-purple screen with stepped corners inside the 9-slice border"""
    w, h = size; a = np.zeros((h, w, 4), np.uint8)
    u = 2 if min(w, h) < 48 else P
    m = np.ones((h, w), bool)
    for i, c in enumerate([3 * u, 2 * u, u]):
        for y in (i * u, h - (i + 1) * u):
            m[y:y + u, :c] = False; m[y:y + u, w - c:] = False
    inner = ndimage.binary_erosion(m, np.ones((2 * u + 1, 2 * u + 1)))
    a[m] = (30, 12, 90, 255); a[inner] = color + (255,)
    Image.fromarray(a).save(os.path.join(out, out_name + ".png"))


def word_tex(d, size, out_name, out):
    w, h = size; img = np.zeros((h, w, 4), np.uint8)
    stamp_word(img, d, P, P, h - 3 * P, max_w=w - 3 * P)
    Image.fromarray(img).save(os.path.join(out, out_name + ".png"))


if __name__ == "__main__":
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    prev = []
    for d in DIFFS:
        prev.append(upe(d, out))
        row = []
        for sub, name, word in [("selectcard", "UI_MSS_MBase_%s" % d, None), ("selectcard", "UI_MSS_MBase_%s_Mini" % d, None),
                                ("selectcard", "UI_MSS_MBase_%s_Tab_01" % d, None), ("selectcard", "UI_MSS_MBase_%s_Tab_02" % d, None),
                                ("selectcard", "UI_MSS_MBase_LvBase_%s" % d, None),
                                ("trackstart", "UI_TST_MBase_%s" % d, "tst"), ("trackstart", "UI_TST_MBase_%s_Tab" % d, None),
                                ("trackstart", "UI_TST_MBase_LV_%s" % d, None)]:
            p = os.path.join(REF, sub, name + ".png")
            if not os.path.exists(p): continue
            st = load(p)
            wa = None
            if word == "tst":   # stock word sits under the jacket, left
                wa = (60, 372, 36)
            im = Image.fromarray(ngoify(st, d, wa)); im.save(os.path.join(out, name + ".png")); row.append(im)
        tp = os.path.join(REF, "selectcard", "UI_MSS_MBase_%s_Text.png" % d)
        if os.path.exists(tp): word_tex(d, Image.open(tp).size, "UI_MSS_MBase_%s_Text" % d, out)
        prev.append(row)
    for n, sz, b in [("UI_MSS_MBase_Box_01", (24, 24), 10), ("UI_MSS_MBase_Box_02", (40, 36), 10),
                     ("UI_TST_MBase_Box_01", (24, 24), 10), ("UI_TST_MBase_Box_02", (40, 32), 10)]:
        screen_box(sz, b, n, out)
    for n, sz in [("UI_MSS_MBase_Box_03", (24, 24)), ("UI_TST_MBase_Box_03", (24, 24))]:
        screen_box(sz, 10, n, out, color=PANEL)
    Image.new("RGBA", (80, 80)).save(os.path.join(out, "UI_MSS_MBase_Icon_Blank.png"))   # empty medal slot: nothing
    prev.append(gauge(out))
    if len(sys.argv) > 2:
        rows = []
        for item in prev:
            if isinstance(item, list):
                w = sum(i.width for i in item) + 10 * len(item); h = max(i.height for i in item)
                r = Image.new("RGBA", (w, h)); x = 0
                for i in item: r.alpha_composite(i, (x, 0)); x += i.width + 10
                rows.append(r)
            else:
                rows.append(item)
        W = max(r.width for r in rows); H = sum(r.height + 10 for r in rows)
        pv = Image.new("RGBA", (W + 20, H + 10), (250, 228, 242, 255)); y = 10
        for r in rows: pv.alpha_composite(r, (10, y)); y += r.height + 10
        pv.save(sys.argv[2])
