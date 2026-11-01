"""Bake the layered NGO background (terra's layout.txt: file x y layer drift plane, sections '# main' / '# sub') into
the images ngo_bg_assets.py installs, plus a calibration grid for finding how the main sky plane maps onto the screen.
usage: python ngo_bg_bake.py <concept_dir> <out_dir>
writes main.png (all main layers), main_static.png (main without floats), sub.png (sub without floats, 1078x450),
floats/onpu01..09.png (120x148) + symbol.png (276x400), calib.png"""
import os, sys
from PIL import Image, ImageDraw

SRC, OUT = sys.argv[1], sys.argv[2]
os.makedirs(os.path.join(OUT, "floats"), exist_ok=True)

sections, cur = {"main": [], "sub": []}, None
plane_of = {}
for line in open(os.path.join(SRC, "layout.txt"), encoding="utf-8"):
    line = line.strip()
    if line in ("# main", "# sub"): cur = line[2:]; continue
    if not line or line.startswith("#") or cur is None: continue
    f, x, y, layer, _drift, plane = line.split()[:6]
    sections[cur].append((int(layer), f, int(x), int(y)))
    plane_of[f] = plane


# in the motion bake the big windows leave the paintings and bob as one particle per screen (see ngo_bg_motion.py):
# they are drawn at their layout positions into 1024 x 768 blocks of the atlas, the main one bottom-left centred on
# main-image (540, 340), the sub one bottom-right centred on sub-strip (440, 225)
MAIN_BOB_WINDOWS, BOB_CENTRE, BOB_SIZE = ("window_01.png", "window_02.png"), (540, 340), (1024, 768)
SUB_BOB_WINDOWS, SUB_BOB_CENTRE = ("window_01.png", "window_02.png"), (440, 225)


def bake(items, size, skip_floats):
    """skip_floats: False keeps every float, True drops them all, "moving" drops only the mid/front ones (those become
    particles; the far ones stay painted in)"""
    canvas = Image.new("RGBA", size, (0, 0, 0, 0))
    for layer, f, x, y in sorted(items, key=lambda t: t[0]):
        if skip_floats is True and f.startswith("float_"): continue
        if skip_floats == "moving" and f.startswith("float_") and plane_of[f] != "back": continue
        if skip_floats == "moving" and size[1] == 450 and f in SUB_BOB_WINDOWS: continue
        if skip_floats == "moving" and size[1] == 1080 and f in MAIN_BOB_WINDOWS: continue
        im = Image.open(os.path.join(SRC, f)).convert("RGBA")
        canvas.alpha_composite(im, (x - im.width // 2, y - im.height // 2)) if 0 <= x - im.width // 2 and 0 <= y - im.height // 2 \
            else canvas.paste(im, (x - im.width // 2, y - im.height // 2), im)
    return canvas


bake(sections["main"], (1080, 1080), False).save(os.path.join(OUT, "main.png"))
bake(sections["main"], (1080, 1080), True).save(os.path.join(OUT, "main_static.png"))
bake(sections["sub"], (1080, 450), True).crop((1, 0, 1079, 450)).save(os.path.join(OUT, "sub.png"))
bake(sections["main"], (1080, 1080), "moving").save(os.path.join(OUT, "main_motion.png"))
bake(sections["sub"], (1080, 450), "moving").crop((1, 0, 1079, 450)).save(os.path.join(OUT, "sub_motion.png"))

# particle atlas for the reused bokeh systems, 2048 x 1536: the top 768px are 8 x 3 tiles of 256px, one row per particle
# system (texture-sheet row), each icon centred at native size so a 256-unit particle draws it 1:1; the bottom-left
# 1024 x 768 block holds the bobbing windows (that system reads the sheet as a 2 x 2 grid, frame 2)
TILE, COLS = 256, 8
ATLAS_ROWS = [
    ["float_sparkle_small_mid", "float_star_small_mid", "float_sparkle_small", "float_star_small",
     "float_sparkle", "float_sparkle_small", "float_star_small_mid", "float_heart_cyan_mid"],                    # twinkle
    ["float_hp_mid", "float_alert_mid", "float_folder_mid", "float_pill_cyan_mid", "float_jine_mid",
     "float_heart_yellow_mid", "float_like_mid", "float_cd_mid"],                                               # mid drift
    ["float_heart", "float_pill", "float_cursor", "float_loading", "float_heart_cyan", "float_cd", "float_jine",
     "float_heart_yellow"],                                                                                      # front drift
]
atlas = Image.new("RGBA", (TILE * COLS, TILE * len(ATLAS_ROWS) + BOB_SIZE[1]), (0, 0, 0, 0))
for r, row in enumerate(ATLAS_ROWS):
    for c, n in enumerate(row):
        im = Image.open(os.path.join(SRC, n + ".png")).convert("RGBA")
        atlas.paste(im, (c * TILE + (TILE - im.width) // 2, r * TILE + (TILE - im.height) // 2), im)
bx0, by0 = BOB_CENTRE[0] - BOB_SIZE[0] // 2, BOB_CENTRE[1] - BOB_SIZE[1] // 2
for _l, f, x, y in sorted(sections["main"]):
    if f not in MAIN_BOB_WINDOWS: continue
    im = Image.open(os.path.join(SRC, f)).convert("RGBA")
    atlas.paste(im, (x - im.width // 2 - bx0, TILE * len(ATLAS_ROWS) + y - im.height // 2 - by0), im)
sx0, sy0 = SUB_BOB_CENTRE[0] - BOB_SIZE[0] // 2, SUB_BOB_CENTRE[1] - BOB_SIZE[1] // 2
for _l, f, x, y in sorted(sections["sub"]):
    if f not in SUB_BOB_WINDOWS: continue
    im = Image.open(os.path.join(SRC, f)).convert("RGBA")
    atlas.paste(im, (BOB_SIZE[0] + x - im.width // 2 - sx0, TILE * len(ATLAS_ROWS) + y - im.height // 2 - sy0), im)
atlas.save(os.path.join(OUT, "floats_atlas.png"))

# float icons for the sub screen's animated note slots, kept on their own 4px pixel grid, centered
ONPU = ["float_heart", "float_heart_cyan", "float_heart_yellow", "float_jine", "float_like", "float_pill_cyan",
        "float_sparkle", "float_cd", "float_folder"]
for i, n in enumerate(ONPU, 1):
    im = Image.open(os.path.join(SRC, n + ".png")).convert("RGBA")
    c = Image.new("RGBA", (120, 148), (0, 0, 0, 0)); c.paste(im, ((120 - im.width) // 2, (148 - im.height) // 2), im)
    c.save(os.path.join(OUT, "floats", "onpu%02d.png" % i))
im = Image.open(os.path.join(SRC, "float_alert.png")).convert("RGBA")
im = im.resize((im.width * 2, im.height * 2), Image.NEAREST)
c = Image.new("RGBA", (276, 400), (0, 0, 0, 0)); c.paste(im, ((276 - im.width) // 2, (400 - im.height) // 2), im)
c.save(os.path.join(OUT, "floats", "symbol.png"))
# the stock treble-clef slot sits on top of window_01, which now bobs with the sub windows block: leave it empty
Image.new("RGBA", (276, 400), (0, 0, 0, 0)).save(os.path.join(OUT, "floats", "symbol_empty.png"))

# calibration: 1080x1080, quadrants in distinct colors, 10x10 labelled cells, a ring at the circle edge
cal = Image.new("RGBA", (1080, 1080), (255, 255, 255, 255))
d = ImageDraw.Draw(cal)
quad = [(255, 120, 120), (120, 200, 255), (140, 230, 140), (250, 220, 90)]   # TL red, TR blue, BL green, BR yellow
for qi, (x0, y0) in enumerate([(0, 0), (540, 0), (0, 540), (540, 540)]):
    d.rectangle((x0, y0, x0 + 539, y0 + 539), fill=quad[qi])
for i in range(11):
    d.line((i * 108, 0, i * 108, 1080), fill=(0, 0, 0), width=3); d.line((0, i * 108, 1080, i * 108), fill=(0, 0, 0), width=3)
for r in range(10):
    for c_ in range(10):
        d.text((c_ * 108 + 8, r * 108 + 8), "%d,%d" % (c_, r), fill=(0, 0, 0))
d.ellipse((0, 0, 1079, 1079), outline=(160, 0, 160), width=8)
d.text((520, 530), "TOP-LEFT=RED", fill=(0, 0, 0))
cal.save(os.path.join(OUT, "calib.png"))
print("baked ->", OUT)
