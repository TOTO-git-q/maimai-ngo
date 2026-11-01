"""Mock of the music-select top screen's character row at full resolution, laid out from the game's own UI
coordinates (09_select_Music.ui.json: 1 UI unit = 1 px, screen left x -1087, top y 968): the leader portrait clipped
to its mask (288x312) and the screen edge, the leader LV plate group scaled by K (ngo_leader_lv_scale.py), and the
four partner cards (frame, base, picture, "LV" and star). Card pictures come from ngo_partner_cards.py.
usage: python ngo_select_top_mock.py <leader png> <cards dir> <frames dir> <K> <out png>"""
import os, sys
from PIL import Image, ImageDraw

LEADER, CARDS, FRAMES, K, OUT = sys.argv[1], sys.argv[2], sys.argv[3], float(sys.argv[4]), sys.argv[5]
def px(x, y): return x + 1087, 968 - y   # UI -> screen px

can = Image.new("RGBA", (760, 470), (246, 214, 236, 255))
# leader: texture 512 x 0.6 centred on the mask (-974, 661); mask 288 x 312
mx0, my0 = px(-1118, 817); mx1, my1 = px(-830, 505)
lead = Image.open(LEADER).convert("RGBA").resize((307, 307), Image.LANCZOS)
layer = Image.new("RGBA", can.size); cx, cy = px(-974, 661)
layer.alpha_composite(lead, (round(cx - 153.5), round(cy - 153.5)))
clip = Image.new("L", can.size, 0); ImageDraw.Draw(clip).rectangle([max(mx0, 0), my0, mx1 - 1, my1 - 1], fill=255)
can.paste(layer, (0, 0), Image.composite(layer.getchannel("A"), Image.new("L", can.size, 0), clip))
# leader LV group, Null_object origin (-970, 652): plate (-2,-89) 144x64, Lv (-41.3,-79) x0.8, scaled by K about (-2,-81)
def sc(x, y): return -970 + (-2 + (x + 2) * K), 652 + (-81 + (y + 81) * K)
plate = Image.open(os.path.join(FRAMES, "UI_CMN_Chara_LFrame_01.png")).convert("RGBA")
pl = plate.resize((round(144 * K), round(64 * K)), Image.NEAREST); x, y = px(*sc(-2, -89))
can.alpha_composite(pl, (round(x - pl.width / 2), round(y - pl.height / 2)))
lvt = Image.open(r"E:\cxref\maimai-ngo\out\ngo_chara_frames\UI_UPE_MBase_Lv.png").convert("RGBA")
lv = lvt.resize((round(45 * .8 * K), round(24 * .8 * K)), Image.NEAREST); x, y = px(*sc(-41.3, -79))
can.alpha_composite(lv, (round(x - lv.width / 2), round(y - lv.height / 2)))
star = Image.open(r"E:\cxref\maimai-ngo\tasks\refs_chara\now_UI_CMN_Chara_Star_01.png").convert("RGBA")
st = star.resize((42, 42), Image.LANCZOS); x, y = px(-972, 538)
can.alpha_composite(st, (round(x - 21), round(y - 21)))
# partner cards: frame top-left (214 + 139 i, 184), 148x264; picture 300 px centred 8.96 px under the frame centre
base = Image.open(os.path.join(FRAMES, "UI_CMN_Chara_RBase_01.png")).convert("RGBA")
frame = Image.open(os.path.join(FRAMES, "UI_CMN_Chara_RFrame_01.png")).convert("RGBA")
names = ["UI_Chara_455101", "UI_Chara_555103", "UI_Chara_555104", "UI_Chara_555106"]
for i in range(4):
    can.alpha_composite(base, (214 + 139 * i, 184)); can.alpha_composite(frame, (214 + 139 * i, 184))
for i in sorted(range(4), key=lambda i: names[i] == "UI_Chara_555103"):     # card 2 is "front"
    pic = Image.open(os.path.join(CARDS, names[i] + ".png")).convert("RGBA").resize((300, 300), Image.LANCZOS)
    can.alpha_composite(pic, (214 + 139 * i + 74 - 150, round(184 + 132 + 8.96 - 150)))
lv1 = lvt.resize((36, 19), Image.NEAREST); st1 = star.resize((46, 46), Image.LANCZOS)
for i in range(4):
    can.alpha_composite(lv1, (214 + 139 * i + 24, 184 + 21)); can.alpha_composite(st1, (214 + 139 * i + 51, 184 + 222))
can.save(OUT)
