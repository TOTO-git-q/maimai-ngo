"""Top strip of the boot cover: the NGO login card (game_login_account, from the game's App_Login prefab) cut into one
compact row: white rounded card, Ame's account icon on the left, the empty password field on the right (the mod types
"angelkawaii2" into it as the self-check advances). Field rect is printed in strip-local fractions for NgoBootScreen.
usage: python ngo_boot_strip.py <login_account.png> <out.png>"""
import sys
from PIL import Image, ImageDraw

src = Image.open(sys.argv[1]).convert("RGBA")
W, H, R = 560, 100, 18
card = Image.new("RGBA", (W, H))
ImageDraw.Draw(card).rounded_rectangle((0, 0, W - 1, H - 1), R, fill=(255, 255, 255, 255), outline=(77, 35, 207, 255), width=4)
icon = src.crop((295, 65, 345, 115)).resize((64, 64), Image.NEAREST)
card.alpha_composite(icon, (18, (H - 64) // 2))
field = src.crop((181, 211, 460, 262)).resize((446, 56), Image.NEAREST)
fx, fy = 96, (H - 56) // 2
card.alpha_composite(field, (fx, fy))
card.save(sys.argv[2])
print("field fractions x %.3f-%.3f y(from bottom) %.3f-%.3f" % (fx / W, (fx + 446) / W, (H - fy - 56) / H, (H - fy) / H))
