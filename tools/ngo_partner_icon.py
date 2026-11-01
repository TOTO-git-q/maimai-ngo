"""Collection icon for the NGO pair partner (UI_Partner_000502 look-alike, 128x128 RGBA).

The frame, the striped background and the speaker badge are taken from the game's own partner icons: the 29 stock
icons agree on them pixel for pixel, so their median is the template. The stripes behind the characters are
rebuilt from the agreed background pixels (they repeat along x+y). The art is the pair's rest frame from the
select test (premultiplied render, un-premultiplied here).
usage: python tools/ngo_partner_icon.py <stock icon dir> <pair rest frame png> <out dir>
writes icon_nobadge.png (owner's framing, no badge) and icon_badge.png (badge kept, framing moved so the faces clear it)
and preview.png (both next to a stock icon, 1x and 3x)"""
import os, sys, glob
import numpy as np
from PIL import Image

REF, FRAME, OUT = sys.argv[1:4]
os.makedirs(OUT, exist_ok=True)

stock = np.stack([np.array(Image.open(f).convert("RGBA")).astype(int)
                  for f in sorted(glob.glob(os.path.join(REF, "UI_Partner_0*.png"))) if not f.endswith("000000.png")])
med = np.median(stock, axis=0)
agree = (np.abs(stock - med).max(axis=3) <= 12).mean(axis=0)
H, W = med.shape[:2]
yy, xx = np.mgrid[0:H, 0:W]

# border ring + transparent corners: the outer 4 px band, as the stock icons have it
edge = (xx < 4) | (yy < 4) | (xx >= W - 4) | (yy >= H - 4)
corner = med[:, :, 3] < 250
frame_mask = (edge | corner) & (agree >= 0.6)

# speaker badge: the agreed, non-stripe pixels in the bottom-right corner
inner = ~edge & (agree >= 0.8)
bg_pix = inner & (xx < 70) & (yy < 40)                      # top-left: stripes only
palette = med[bg_pix][:, :3]
def stripe_like(c):
    return np.min(np.abs(palette[None, :, :] - c[:, None, :]).sum(axis=2), axis=1) <= 18
corner_px = inner & (xx >= 78) & (yy >= 78)
cand = med[corner_px][:, :3]
badge_mask = np.zeros_like(inner)
badge_mask[corner_px] = ~stripe_like(cand)
# close small holes inside the badge (glyph anti-aliasing that happened to match a stripe tone)
ys, xs = np.nonzero(badge_mask)
cy, cx = ys.mean(), xs.mean(); r = np.sqrt(((ys - cy) ** 2 + (xs - cx) ** 2)).max()
badge_mask = ((yy - cy) ** 2 + (xx - cx) ** 2 <= (r + 0.5) ** 2) & ~edge

# stripes: colour as a function of (x + y) mod period, learnt from agreed background pixels
bgsel = inner & ~badge_mask & (agree >= 0.8)
best = None
for p in range(6, 40):
    s = (xx + yy)[bgsel] % p; cols = med[bgsel][:, :3]
    var = sum(cols[s == k].var(axis=0).sum() * (s == k).sum() for k in range(p) if (s == k).any())
    if best is None or var < best[0]: best = (var, p)
P = best[1]
s_all = (xx + yy)[bgsel] % P; cols = med[bgsel]
table = np.array([np.median(cols[s_all == k], axis=0) if (s_all == k).any() else cols.mean(axis=0) for k in range(P)])
bg = table[(xx + yy) % P]
print("stripe period", P, "badge radius", round(float(r), 1))

def art_layer(box, size=W):
    a = np.array(Image.open(FRAME).convert("RGBA")).astype(float)
    al = a[:, :, 3:4]
    rgb = np.where(al > 0, np.clip(a[:, :, :3] * 255.0 / np.maximum(al, 1), 0, 255), 0)
    im = Image.fromarray(np.concatenate([rgb, al], 2).astype(np.uint8)).crop(box)
    return np.array(im.resize((size, size), Image.LANCZOS)).astype(float)

def compose(box, badge):
    out = bg.copy(); out[:, :, 3] = 255
    art = art_layer(box); a = art[:, :, 3:4] / 255.0
    out[:, :, :3] = out[:, :, :3] * (1 - a) + art[:, :, :3] * a
    keep = frame_mask | (badge_mask if badge else np.zeros_like(frame_mask))
    out[keep] = med[keep]
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8))

# owner's framing (from their crop of the 1536 rest frame): Ame's head and chest top-left, K's head bottom-right
owner = (544, 105, 1312, 873)
# badge kept: wider, so K's face sits up-left of the badge
moved = (494, 16, 1479, 1001)
a = compose(owner, badge=False); a.save(os.path.join(OUT, "icon_nobadge.png"))
b = compose(moved, badge=True); b.save(os.path.join(OUT, "icon_badge.png"))

ref = Image.open(os.path.join(REF, "UI_Partner_000017.png")).convert("RGBA")
pv = Image.new("RGBA", (3 * 128 * 3 + 4 * 16, 128 * 3 + 128 + 48), (235, 235, 235, 255))
for i, im in enumerate([ref, a, b]):
    pv.alpha_composite(im.resize((384, 384), Image.NEAREST), (16 + i * (384 + 16), 16))
    pv.alpha_composite(im, (16 + i * (384 + 16), 384 + 32))
pv.save(os.path.join(OUT, "preview.png"))
