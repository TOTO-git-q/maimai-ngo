"""Credit-entry flash after mode select (UI_CRD_Enty_*, "ENTRY!"): the random "dolly" characters in the middle become
Ame and KAngel, drawn by Codex as new pictures (out/ngo_crd_pair, flat green; the owner rejected pasting the earlier
sketch pieces together):
- CharaA_01 / _02 (368x308, two animation frames): the pair standing on a cloud / hopping, cheering;
- CharaB_01 / CharaC_01 (100x164, the left and right side characters): P-chan, waving / hopping (the owner keeps the
  two girls in the middle only; out/ngo_crd_pchan).
All the green is keyed out, pockets enclosed between the figures too (neither girl wears green), plus the
green-tinted edge pixels next to it.
usage: python ngo_crd_entry.py <out dir> [preview.png]"""
import os, sys
import numpy as np
from PIL import Image
from scipy import ndimage

SRC = r"E:\cxref\maimai-ngo\out\ngo_crd_pair"
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)


def key(path, size):
    a = np.array(Image.open(path).convert("RGB")).astype(int)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    green = (g > 170) & (r < 130) & (b < 130)
    tinted = (g > r + 50) & (g > b + 50)                 # anti-aliased edge pixels mixed with the green
    clear = green | (tinted & ndimage.binary_dilation(green, iterations=2))
    out = np.dstack([a.astype(np.uint8), np.where(clear, 0, 255).astype(np.uint8)])
    im = Image.fromarray(out)
    return im if im.size == size else im.resize(size, Image.NEAREST)


def clean_pchan(im):
    """the slim P-chan: the two little circles on its body (a joke in the owner's reference, not part of P-chan) are
    filled with the body pink: dark outline-coloured blobs that do not touch the outer outline. Leftover green-tinted
    pixels are dropped (edge) or turned to the outline colour (inside)."""
    a = np.array(im).astype(int)
    rgb, al = a[..., :3], a[..., 3] > 0
    dark = al & (rgb.sum(-1) < 420)
    lab, n = ndimage.label(dark)
    outer = ndimage.binary_dilation(~al, iterations=1)
    body = np.median(rgb[al & (rgb.sum(-1) > 600)], 0).astype(int)
    ys, xs = np.where(al); x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    for i in range(1, n + 1):
        m = lab == i
        cy, cx = ndimage.center_of_mass(m)
        # only on the body: right of the head (she faces left, the head is the left ~40%) and in its lower half
        on_body = cx > x0 + .4 * (x1 - x0) and cy > y0 + .45 * (y1 - y0)
        if on_body and m.sum() < 40 and not (m & outer).any():
            fill = ndimage.binary_dilation(m, iterations=1) & al & ~outer
            rgb[fill] = body
    g = (rgb[..., 1] > rgb[..., 0] + 20) & (rgb[..., 1] > rgb[..., 2] + 20) & al
    a[..., 3][g & outer] = 0
    outline = np.median(rgb[dark & outer], 0).astype(int) if (dark & outer).any() else np.array([110, 50, 90])
    rgb[g & ~outer] = outline
    a[..., :3] = rgb
    return Image.fromarray(a.astype(np.uint8))


def owner_drawing(path, size):
    """the owner's own drawing on a pale pink page: its closed plum outline with everything inside filled is the
    figure; the page around it goes. Fitted to the texture, feet on the bottom, centred."""
    a = np.array(Image.open(path).convert("RGB")).astype(int)
    line = a.sum(-1) < 420
    fig = ndimage.binary_fill_holes(ndimage.binary_closing(line, iterations=2))
    lab, n = ndimage.label(fig)
    fig = lab == 1 + np.argmax(ndimage.sum(fig, lab, range(1, n + 1)))      # the cat, not stray specks
    im = Image.fromarray(np.dstack([a.astype(np.uint8), np.where(fig, 255, 0).astype(np.uint8)]))
    im = im.crop(im.getchannel("A").getbbox())
    w, h = size; s = min((w - 4) / im.width, (h - 4) / im.height)
    im = im.resize((round(im.width * s), round(im.height * s)), Image.LANCZOS)
    out = Image.new("RGBA", size); out.alpha_composite(im, ((w - im.width) // 2, h - im.height - 2))
    return out


def match_pair(left, right, size):
    """the two side P-chans side by side: the left one takes the colours of the right one (the owner's drawing) by a
    Lab mean/spread transfer over the figure, and both are scaled to the same figure height, feet on one baseline"""
    import cv2
    def lab(im):
        a = np.array(im); m = a[..., 3] > 0
        return a, m, cv2.cvtColor(np.ascontiguousarray(a[..., :3]), cv2.COLOR_RGB2LAB).astype(float)
    la, lm, ll = lab(left); ra, rm, rl = lab(right)
    for c in range(3):
        s, t = ll[..., c][lm], rl[..., c][rm]
        ll[..., c][lm] = (s - s.mean()) / max(s.std(), 1e-3) * t.std() + t.mean()
    la[..., :3][lm] = cv2.cvtColor(np.clip(ll, 0, 255).astype(np.uint8), cv2.COLOR_LAB2RGB)[lm]
    # the line work: the outline (dark, touching the outside) takes the right one's outline colour, the dark parts
    # inside (eyes, mouth) its eye colour
    def darks(a, m):
        d = m & (a[..., :3].astype(int).sum(-1) < 330)
        outer = ndimage.binary_dilation(~m, iterations=2)
        lab_, n = ndimage.label(d)
        touch = np.isin(lab_, np.unique(lab_[d & outer]))
        return d & touch, d & ~touch
    # the body fill: shift the left one's light tones so its main pink equals the right one's
    rb = rm & (ra[..., :3].astype(int).sum(-1) > 600); lb = lm & (la[..., :3].astype(int).sum(-1) > 560)
    if rb.any() and lb.any():
        d = np.median(ra[..., :3][rb], 0) - np.median(la[..., :3][lb], 0)
        la[..., :3][lb] = np.clip(la[..., :3][lb].astype(float) + d, 0, 255).astype(np.uint8)
    ro, ri = darks(ra, rm); lo, li = darks(la, lm)
    if ro.any(): la[..., :3][lo] = np.median(ra[..., :3][ro], 0).astype(np.uint8)
    if ri.any(): la[..., :3][li] = np.median(ra[..., :3][ri], 0).astype(np.uint8)
    figs = [Image.fromarray(la), right]
    figs = [f.crop(f.getchannel("A").getbbox()) for f in figs]
    w, h = size
    th = min(h - 4, *[(w - 4) * f.height / f.width for f in figs])   # one height that fits both widths
    out = []
    for f in figs:
        s = th / f.height
        f = f.resize((round(f.width * s), round(th)), Image.LANCZOS)
        o = Image.new("RGBA", size); o.alpha_composite(f, ((w - f.width) // 2, h - f.height - 2)); out.append(o)
    return out


PCHAN = r"E:\cxref\maimai-ngo\out\ngo_crd_pchan"
OWNER_PCHAN = r"E:\cxref\maimai-ngo\tasks\refs_crd\pchan_owner_drawing.png"   # the owner drew the right one, 2026-10-31
for src, name, size in ((os.path.join(SRC, "pair_01.png"), "UI_CRD_Enty_CharaA_01", (368, 308)),
                        (os.path.join(SRC, "pair_02.png"), "UI_CRD_Enty_CharaA_02", (368, 308)),
                        (os.path.join(PCHAN, "pchan_left.png"), "UI_CRD_Enty_CharaB_01", (100, 164)),
                        (os.path.join(PCHAN, "pchan_right.png"), "UI_CRD_Enty_CharaC_01", (100, 164))):
    if name == "UI_CRD_Enty_CharaC_01" and os.path.exists(OWNER_PCHAN): im = owner_drawing(OWNER_PCHAN, size)
    else:
        im = key(src, size)
        if name == "UI_CRD_Enty_CharaC_01": im = clean_pchan(im)
    im.save(os.path.join(OUT, name + ".png"))
# the two side P-chans: one colour scheme and one size (the owner, 2026-10-31)
b, c = (Image.open(os.path.join(OUT, n + ".png")).convert("RGBA") for n in ("UI_CRD_Enty_CharaB_01", "UI_CRD_Enty_CharaC_01"))
b, c = match_pair(b, c, (100, 164))
b.save(os.path.join(OUT, "UI_CRD_Enty_CharaB_01.png")); c.save(os.path.join(OUT, "UI_CRD_Enty_CharaC_01.png"))
W, H = 368, 308
if len(sys.argv) > 2:
    pv = Image.new("RGBA", (W * 2 + 260, H + 20), (246, 214, 236, 255)); x = 10
    for n in ("UI_CRD_Enty_CharaA_01", "UI_CRD_Enty_CharaA_02", "UI_CRD_Enty_CharaB_01", "UI_CRD_Enty_CharaC_01"):
        im = Image.open(os.path.join(OUT, n + ".png")); pv.alpha_composite(im, (x, 10)); x += im.width + 10
    pv.save(sys.argv[2])
