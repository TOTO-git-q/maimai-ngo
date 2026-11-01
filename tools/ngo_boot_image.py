"""Windose 20 boot picture for the cabinet's circle screen: crop the 4:3 boot frame (ngo-pack boot_logo.png) to a
centred square, scale to 1080, black outside the circle. Output goes to LocalAssets/PartnerLive2D/boot/ (read by
NgoBootScreen.cs). usage: python ngo_boot_image.py <src png> <out png>"""
import sys
from PIL import Image, ImageDraw

import cv2, numpy as np
src = Image.open(sys.argv[1]).convert("RGB")
w, h = src.size
# the small corner caption ("Needy Girl Overdose(c)", top right) would be cut by the circle: paint it out
a = np.array(src); m = np.zeros(a.shape[:2], np.uint8); m[int(h * .03):int(h * .11), int(w * .6):int(w * .88)] = 255
dark = (a.max(2) < 90).astype(np.uint8) * 255; m = cv2.dilate(m & dark, np.ones((5, 5), np.uint8))
src = Image.fromarray(cv2.inpaint(a, m, 9, cv2.INPAINT_TELEA))
bb = src.point(lambda v: 255 if v > 24 else 0).convert("L").getbbox()   # drop the black side bars
src = src.crop(bb); w, h = src.size
s = min(w, h); sq = src.crop(((w - s) // 2, (h - s) // 2, (w - s) // 2 + s, (h - s) // 2 + s)).resize((1080, 1080), Image.LANCZOS)
mask = Image.new("L", (1080, 1080), 0); ImageDraw.Draw(mask).ellipse((0, 0, 1079, 1079), fill=255)
out = Image.new("RGB", (1080, 1080), (0, 0, 0)); out.paste(sq, (0, 0), mask)
out.save(sys.argv[2]); print("saved", sys.argv[2])
