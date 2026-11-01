"""Cut only the white login card out of the game's login panel (game_login_account / App_Login): the card with Ame's
account icon, the name and the password field, as an RGBA with clean rounded corners (the purple grid behind it is
dropped). Prints the field and checkmark rects as card fractions for NgoBootScreen.
usage: python ngo_boot_card.py <login_account.png> <out.png>"""
import sys
import numpy as np
from PIL import Image, ImageDraw

src = Image.open(sys.argv[1]).convert("RGBA"); a = np.array(src).astype(int)
white = (a[..., :3].min(2) > 245)
rows = np.where(white.sum(1) > 200)[0]; cols = np.where(white.sum(0) > 200)[0]
x0, x1, y0, y1 = cols.min(), cols.max() + 1, rows.min(), rows.max() + 1
card = src.crop((x0, y0, x1, y1)); w, h = card.size
# corner radius: first white pixel along the top row
r = int(np.argmax(white[y0, x0:x1])) + 2
mask = Image.new("L", (w, h), 0); ImageDraw.Draw(mask).rounded_rectangle((0, 0, w - 1, h - 1), r, fill=255)
card.putalpha(mask); card.save(sys.argv[2])
fx0, fy0, fx1, fy1 = 181 - x0, 211 - y0, 460 - x0, 262 - y0       # password box in the panel art
cx0, cy0, cx1, cy1 = 404 - x0, 278 - y0, 432 - x0, 302 - y0       # checkmark spot (installer layout, x2)
print("card", (x0, y0, x1, y1), "size", (w, h), "radius", r)
print("field  %.4f %.4f %.4f %.4f" % (fx0 / w, 1 - fy1 / h, fx1 / w, 1 - fy0 / h))
print("check  %.4f %.4f %.4f %.4f" % (cx0 / w, 1 - cy1 / h, cx1 / w, 1 - cy0 / h))
