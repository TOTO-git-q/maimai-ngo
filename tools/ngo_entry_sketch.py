"""Entry screen (エントリーしました) from the owner-approved simple flat drawings (out/ngo_entry_sketch): Ame takes the left
entered machine, KAngel the right one, the stand becomes the pink bench. The owner wants no computer and no gloves: the
idle machine, its outline and the glove characters (UI_CNT_Bukuro_*, shared with the continue screen) are emptied.
usage: python ngo_entry_sketch.py <out dir> [preview.png]"""
import os, sys
import cv2
import numpy as np
from PIL import Image

X = r"E:\cxref\maimai-ngo"
SK = os.path.join(X, r"out\ngo_entry_sketch")
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)


def cut(path):
    """drawing on white -> RGBA; only the white connected to the border becomes transparent"""
    a = np.array(Image.open(path).convert("RGB"))
    near = np.all(a > 238, axis=2).astype("uint8")
    _, lab, _, _ = cv2.connectedComponentsWithStats(near, 8)
    edge = np.unique(np.r_[lab[0], lab[-1], lab[:, 0], lab[:, -1]])
    im = Image.fromarray(np.dstack([a, np.where(np.isin(lab, edge[edge != 0]), 0, 255).astype("uint8")]))
    return im.crop(im.getchannel("A").getbbox())


def fit(src, size, box):
    """aspect kept, centred in box=(x0, y0, x1, y1), standing on its bottom edge"""
    x0, y0, x1, y1 = box
    k = min((x1 - x0) / src.width, (y1 - y0) / src.height)
    im = src.resize((round(src.width * k), round(src.height * k)), Image.LANCZOS)
    dst = Image.new("RGBA", size)
    dst.alpha_composite(im, (x0 + (x1 - x0 - im.width) // 2, y1 - im.height))
    return dst


def save(name, im):
    im.save(os.path.join(OUT, name + ".png"))


SLOT = (240, 368)
ame, kangel = cut(os.path.join(SK, "ame.png")), cut(os.path.join(SK, "kangel.png"))
save("UI_ENT_Img_MachineActive_Left", fit(ame, SLOT, (4, 8, 236, 362)))
save("UI_ENT_Img_MachineActive_Right", fit(kangel, SLOT, (4, 8, 236, 362)))
for n in ("UI_ENT_Img_Machine", "UI_ENT_Img_MachineLine"): save(n, Image.new("RGBA", SLOT))
# stock stand: pole in the middle, slab across the bottom (y ~ 328..408); the bench takes the slab's place
save("UI_ENT_Img_MachineBase", fit(cut(os.path.join(SK, "stage.png")), (508, 412), (0, 318, 508, 410)))
for c in ("Body", "Green_Body", "Orange_Body", "Pink_Body"): save("UI_CNT_Bukuro_" + c, Image.new("RGBA", (188, 264)))
for c in ("Blue_Leg", "Green_Leg", "Oramge_Leg", "Pink_Leg"): save("UI_CNT_Bukuro_" + c, Image.new("RGBA", (36, 64)))

if len(sys.argv) > 2:   # stock arrangement: stand under two machines side by side
    pv = Image.new("RGBA", (560, 460), (234, 200, 190, 255))
    pv.alpha_composite(Image.open(os.path.join(OUT, "UI_ENT_Img_MachineBase.png")), (26, 40))
    pv.alpha_composite(Image.open(os.path.join(OUT, "UI_ENT_Img_MachineActive_Left.png")), (36, 0))
    pv.alpha_composite(Image.open(os.path.join(OUT, "UI_ENT_Img_MachineActive_Right.png")), (284, 0))
    pv.save(sys.argv[2])
