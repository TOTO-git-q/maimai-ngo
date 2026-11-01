"""One overview sheet of the skinned screens from a skin walk (ngo_skin_walk.py): picks the title frame from the attract
captures (the one with the most logo magenta in the logo band), the most closed transition frame from the burst, and the first
capture of each screen, labels them and lays them out in a grid.
usage: python ngo_overview.py <walk_dir> <out.png> [--transition-from <other walk_dir>]"""
import glob, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

D, OUT = sys.argv[1], sys.argv[2]
TD = sys.argv[sys.argv.index("--transition-from") + 1] if "--transition-from" in sys.argv else D
SCREENS = [("EntryProcess", "登录"), ("PlInformationProcess", "玩家信息"), ("ModeSelectProcess", "模式选择"),
           ("LoginBonusProcess", "登录奖励"), ("RegionalSelectProcess", "区域选择"), ("TicketSelectProcess", "票券选择"),
           ("PhotoShootProcess", "拍照"), ("TutorialSelectProces", "教程选择"), ("select_Genre", "选歌·分类"),
           ("select_Music", "选歌·曲目"), ("select_Difficulty", "选歌·难度"), ("select_Menu", "选歌·菜单")]


def magenta(im, box):
    """share of the NGO logo's hot magenta in box (the title frame has the most of it)"""
    a = np.asarray(im.convert("RGB").crop(box)).astype(int)
    return float(((a[..., 0] > 180) & (a[..., 1] < 90) & (a[..., 2] > 170)).mean())


tiles = []
att = sorted(glob.glob(os.path.join(D, "attract_*.png")))
if att:
    best = max(att, key=lambda p: magenta(Image.open(p), (40, 600, 500, 760)))
    tiles.append((best, "标题页"))
bur = sorted(glob.glob(os.path.join(TD, "burst_*.png")))
if bur:   # the closed transition: the frame whose circle differs most from the last (settled) frame
    last = np.asarray(Image.open(bur[-1]).convert("RGB")).astype(float)
    best = max(bur, key=lambda p: np.abs(np.asarray(Image.open(p).convert("RGB")).astype(float) - last)[420:].mean())
    tiles.append((best, "转场"))
for key, label in SCREENS:
    hits = sorted(p for p in glob.glob(os.path.join(D, "*_%s*.png" % key)) if "_b" not in os.path.basename(p))
    if hits: tiles.append((hits[0], label))

W, H, cols = 270, 480, 5
rows = (len(tiles) + cols - 1) // cols
sheet = Image.new("RGB", (cols * (W + 10) + 10, rows * (H + 40) + 10), (24, 18, 34))
d = ImageDraw.Draw(sheet)
try:
    font = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 20)
except OSError:
    font = ImageFont.load_default()
for i, (p, label) in enumerate(tiles):
    x, y = 10 + (i % cols) * (W + 10), 10 + (i // cols) * (H + 40)
    sheet.paste(Image.open(p).convert("RGB").resize((W, H)), (x, y + 30))
    d.text((x, y + 2), "%d  %s" % (i + 1, label), fill=(240, 220, 250), font=font)
sheet.save(OUT)
print(len(tiles), "tiles ->", OUT)
