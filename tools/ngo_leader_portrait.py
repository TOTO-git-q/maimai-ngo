"""Leader portrait on the music-select top screen (UI_Chara_455102, 512x512, chara bundle ui_chara_455102.ab) from the
owner's Ame illustration with the background removed by Codex (out/ngo_leader_ame/variants). Clean-up: the glitch
bars Codex left are inpainted away (colour and alpha) inside hand-read boxes, then only the figure (largest opaque
component) is kept, so floating splashes go. Framing: the whole bust across x 60-421 of the 512 square, the part of the
leader image that shows between the screen's left edge (x 68) and the first partner card (x 423); see ngo_select_top_mock.py.
usage: python ngo_leader_portrait.py <variant png> <out dir> [preview.png]"""
import os, sys
import cv2
import numpy as np
from PIL import Image
from scipy import ndimage

SRC, OUT = sys.argv[1], sys.argv[2]
os.makedirs(OUT, exist_ok=True)
a = np.array(Image.open(SRC).convert("RGBA"))
# glitch bars (black blocks with red / blue / yellow fringes), boxes read off variant_1 in its own pixels: x0, y0, x1, y1
BARS = [(5, 355, 70, 400), (28, 398, 116, 450), (20, 448, 40, 498), (208, 510, 244, 556), (20, 566, 124, 612),
        (102, 368, 152, 386), (102, 866, 146, 882), (334, 836, 378, 874), (986, 98, 1055, 142), (896, 656, 964, 716),
        (972, 704, 1055, 746), (806, 814, 870, 858), (894, 776, 964, 822), (926, 854, 1055, 892), (1030, 626, 1055, 694)]
if os.path.basename(SRC) == "variant_1.png":
    # the black block and its pure-colour fringes, inside the boxes only; inpainted (colour and alpha) from around,
    # so hair strands and the outline carry on through where a bar was
    rgb = a[..., :3].astype(int)
    glitch = (rgb.max(-1) < 40) | (rgb.max(-1) - rgb.min(-1) > 170)
    m = np.zeros(glitch.shape, bool)
    for x0, y0, x1, y1 in BARS: m[y0:y1, x0:x1] = glitch[y0:y1, x0:x1]
    m = ndimage.binary_dilation(m, iterations=2).astype(np.uint8) * 255
    a[..., :3] = cv2.inpaint(np.ascontiguousarray(a[..., :3]), m, 6, cv2.INPAINT_TELEA)
    a[..., 3] = cv2.inpaint(np.ascontiguousarray(a[..., 3]), m, 6, cv2.INPAINT_TELEA)
    hit = m > 0   # no half-transparent smear where the alpha was filled in
    a[..., 3][hit] = np.where(a[..., 3][hit] >= 160, 255, 0)
op = a[..., 3] > 32
lab, n = ndimage.label(op)
if n:
    keep = 1 + np.argmax(ndimage.sum(op, lab, range(1, n + 1)))
    a[..., 3][lab != keep] = 0
im = Image.fromarray(a)
bb = im.getchannel("A").getbbox(); im = im.crop(bb)
# the leader image (512 x 0.6 on screen) runs off the top screen's left edge up to its x 68 and goes under the first
# partner card from x 423, so the figure fills x 60-421 (a few px of hair past the edge)
FIG = (60, 421)
S = round(im.width * 512 / (FIG[1] - FIG[0]))
sq = Image.new("RGBA", (S, S)); sq.alpha_composite(im.crop((0, 0, im.width, min(im.height, S))), (round(FIG[0] / 512 * S), 0))
sq.resize((512, 512), Image.LANCZOS).save(os.path.join(OUT, "UI_Chara_455102.png"))
if len(sys.argv) > 3:
    pv = Image.new("RGBA", (532, 532), (246, 214, 236, 255)); pv.alpha_composite(Image.open(os.path.join(OUT, "UI_Chara_455102.png")), (10, 10))
    pv.save(sys.argv[3])
