"""Mode-select left/right buttons (UI_MDS_Btn_Arrow, 292x168; the game mirrors it for the right one): the same pixel
purple sphere and pixel arrow as the music-select arrows (UI_CMN_Btn_01A + UI_CMN_Arrow, the installed NGO versions,
dumped by ngo_dump_tex.py), pointing left, centred, at 1:1 so their pixel grid stays intact.
usage: python ngo_mode_arrow.py <out dir> [preview.png]"""
import os, sys
from PIL import Image, ImageOps

REF = r"E:\cxref\maimai-ngo\tasks\refs_audit3"
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
W, H = 292, 168
ball = Image.open(os.path.join(REF, "now_UI_CMN_Btn_01A.png")).convert("RGBA")     # 158x158 pixel sphere
arrow = Image.open(os.path.join(REF, "now_UI_CMN_Arrow.png")).convert("RGBA")      # 128x128, points right
arrow = ImageOps.mirror(arrow.crop(arrow.getchannel("A").getbbox()))
out = Image.new("RGBA", (W, H))
bx, by = (W - ball.width) // 2, (H - ball.height) // 2
out.alpha_composite(ball, (bx, by))
out.alpha_composite(arrow, (bx + (ball.width - arrow.width) // 2 - 4, by + (ball.height - arrow.height) // 2))
out.save(os.path.join(OUT, "UI_MDS_Btn_Arrow.png"))
if len(sys.argv) > 2:
    pv = Image.new("RGBA", (W * 2 + 20, H), (246, 214, 236, 255))
    pv.alpha_composite(out, (0, 0)); pv.alpha_composite(ImageOps.mirror(out), (W + 20, 0))
    pv.resize((pv.width * 2, pv.height * 2), Image.NEAREST).save(sys.argv[2])
