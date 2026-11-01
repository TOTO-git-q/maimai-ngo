"""Music-select tab row, my fixes on top of Codex round 3 (out/select_art3):
- selected tab: its overlay (Tab_CenterBase/WhiteBase, sprite tabwhite_flat) is alpha-animated by the game and stays
  near invisible, so the violet "active" face never showed. The face moves into the base (tabbase_flat; tinted
  #F7E1FB, which barely shifts the violet) and the overlay becomes empty.
- the band behind the tabs (UI_CMN_Selector_Deco_02, a soft white fade) becomes a hard-edged Windose tab trough:
  transparent above, a 2 px #4D23CF line, a 2 px white line, then a #C3A6E3 body, so the light tabs sit on a darker
  strip that leads into the board's title bar.
Writes <out_dir>/select_sprites/{tabbase_flat,tabwhite_flat}.png and <out_dir>/replacements/UI_CMN_Selector_Deco_02.png
usage: python ngo_tabstrip.py <stock UI_CMN_Selector_Deco_02.png> <out_dir>"""
import os, sys
from PIL import Image

STOCK_DECO, OUT = sys.argv[1], sys.argv[2]
ART3 = r"E:\cxref\maimai-ngo\out\select_art3"
os.makedirs(os.path.join(OUT, "select_sprites"), exist_ok=True)
os.makedirs(os.path.join(OUT, "replacements"), exist_ok=True)

face = Image.open(os.path.join(ART3, "tabwhite_flat.png")).convert("RGBA")
face.save(os.path.join(OUT, "select_sprites", "tabbase_flat.png"))
Image.new("RGBA", face.size, (0, 0, 0, 0)).save(os.path.join(OUT, "select_sprites", "tabwhite_flat.png"))

w, h = Image.open(STOCK_DECO).size
TOP = round(h * 0.34)                                 # stock fade ends about here; the trough starts below it
deco = Image.new("RGBA", (w, h), (0, 0, 0, 0))
px = deco.load()
for y in range(TOP, h):
    c = (0x4D, 0x23, 0xCF, 255) if y < TOP + 2 else (255, 255, 255, 255) if y < TOP + 4 else (0xC3, 0xA6, 0xE3, 255)
    for x in range(w): px[x, y] = c
deco.save(os.path.join(OUT, "replacements", "UI_CMN_Selector_Deco_02.png"))
print("tab strip art ->", OUT)
