"""Static layer of the boot cover's upper screen (the cabinet's top monitor, 1080x450): the game's login background
(widened), one white card across it, Ame's account icon (x4, pixel-crisp), the "angelkawaii" name in the game's
pixel lettering (x2) and an empty password box. NgoBootScreen types the password into the box and shows the
checkmark. Prints the box rect as fractions of the 1080x450 band.
usage: python ngo_boot_top.py <boot asset dir>"""
import os, sys
import numpy as np
from PIL import Image, ImageDraw

D = sys.argv[1]
W, H = 1080, 450
PLUM, GREY = (77, 35, 207, 255), (150, 140, 170, 255)

bg = Image.open(os.path.join(D, "login.png")).convert("RGBA")          # 512x384: pink bar / light body / pink bar
k = H / bg.height; bg = bg.resize((round(bg.width * k), H), Image.LANCZOS)
out = Image.new("RGBA", (W, H)); out.paste(bg, (0, 0))
out.paste(bg.crop((bg.width - 2, 0, bg.width - 1, H)).resize((W - bg.width, H)), (bg.width, 0))

src = Image.open(os.path.join(D, "login_account.png")).convert("RGBA")
icon = src.crop((295, 65, 345, 115)).resize((200, 200), Image.NEAREST)
a = np.array(src.crop((190, 120, 450, 180))).astype(int)                # the name, purple pixel letters on white
ink = a[..., :3].min(2) < 200
ys, xs = np.where(ink); name = src.crop((190 + xs.min(), 120 + ys.min(), 190 + xs.max() + 1, 120 + ys.max() + 1))
na = np.array(name); na[..., 3] = np.where(na[..., :3].min(2) < 200, 255, 0); name = Image.fromarray(na)
name = name.resize((name.width * 2, name.height * 2), Image.NEAREST)

d = ImageDraw.Draw(out)
cx0, cy0, cx1, cy1 = 50, 72, 1030, 378                                   # white card, inside the pink bars
d.rounded_rectangle((cx0, cy0, cx1, cy1), 26, fill=(255, 255, 255, 255), outline=PLUM, width=6)
out.alpha_composite(icon, (cx0 + 44, (cy0 + cy1) // 2 - 100))
tx = cx0 + 44 + 200 + 48
out.alpha_composite(name, (tx, cy0 + 40))
bx0, by0, bx1, by1 = tx, cy0 + 40 + name.height + 34, cx1 - 44, cy1 - 40  # password box
d.rectangle((bx0, by0, bx1, by1), fill=(255, 255, 255, 255), outline=GREY, width=4)
d.line((bx0 + 4, by0 + 4, bx1 - 4, by0 + 4), fill=(210, 205, 220, 255), width=3)   # inset shade like the game's box
out.save(os.path.join(D, "top_static.png"))
print("box %.4f %.4f %.4f %.4f" % (bx0 / W, 1 - by1 / H, bx1 / W, 1 - by0 / H), "name", name.size)
