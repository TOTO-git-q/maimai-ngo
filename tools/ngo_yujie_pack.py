"""Build the shareable "mysterious plate" package (owner 2026-11-07): one static nameplate (雨姐, art from
maimai-nameplate\\build\\yujie) as pure game data, no mod, no account edit. The game unlocks it by itself: its release
condition is Track / DIFFICULTY ANY (true for any song), checked after every track (NextTrackProcess), so it is owned
after the first song once installed (Total/Credit conditions are only checked when a credit ends normally). It stays
hidden in the collection until then (dispCond Lock).
  A588\\plate\\plate986001\\Plate.xml
  A588\\AssetBundleImages\\nameplate\\ui_plate_986001.png   (read by AquaMai LoadLocalImages)
usage: python tools/ngo_yujie_pack.py [--out <dir>] [--install <StreamingAssets>]"""
import argparse, os, re, shutil, zipfile
from PIL import Image

PID, OPTION = 986001, "A588"
ART = r"E:\claude workspace\maimai-nameplate\build\yujie\UI_Plate_yujie.png"
TEMPLATE = r"D:\maimai\Package\Sinmai_Data\StreamingAssets\A000\plate\plate709501\Plate.xml"
README = """神秘的铭牌（maimai DX 1.70）

安装
1. 把 A588 文件夹整个复制到游戏的 Package\\Sinmai_Data\\StreamingAssets 里（和 A000 放在一起）。
   你的整合包如果把选项包放在别处（例如 option 文件夹），就放到那边，和其他 Axxx 放一起。
2. 需要 AquaMai，并开启「直读图片」[GameSystem.Assets.LoadLocalImages]（装过自制谱的一般已经开着）。
3. 正常登录打一局。

卸载：删掉 A588 文件夹。
只加了一块铭牌（编号 986001），不改任何其他文件，也不碰账号设置。
请勿用于商业用途。
"""

ap = argparse.ArgumentParser()
ap.add_argument("--out", default=r"E:\claude workspace\maimai-partner-live2d\release\mystery_plate")
ap.add_argument("--install")
a = ap.parse_args()

xml = open(TEMPLATE, encoding="utf-8").read()
xml = xml.replace("<dataName>plate709501</dataName>", "<dataName>plate%06d</dataName>" % PID)
xml = re.sub(r"<name>\s*<id>709501</id>\s*<str>[^<]*</str>\s*</name>", "<name>\n    <id>%d</id>\n    <str>？？？</str>\n  </name>" % PID, xml)
xml = re.sub(r"<genre>\s*<id>3</id>\s*<str>[^<]*</str>\s*</genre>", "<genre>\n    <id>5</id>\n    <str>その他</str>\n  </genre>", xml)
xml = re.sub(r"<normText>[^<]*</normText>", "<normText>？？？</normText>", xml)
# release condition: category Track, first condition "any song at any difficulty"; the other four stay None (pass)
c0 = re.search(r"<condition0>.*?</condition0>", xml, re.S).group(0)
c0n = c0.replace("<kindTrack>None</kindTrack>", "<kindTrack>DIFFICULTY</kindTrack>")
xml = xml.replace(c0, c0n)
for must in ("<id>%d</id>" % PID, "<str>？？？</str>", "<category>Track</category>", "<kindTrack>DIFFICULTY</kindTrack>", "<difficultyId>ANY</difficultyId>", "<dispCond>Lock</dispCond>", "<disable>false</disable>"):
    if must not in xml: raise SystemExit("Plate.xml check failed: " + must)

im = Image.open(ART).convert("RGBA")
if im.size != (720, 116): raise SystemExit("art is %s, not 720x116" % (im.size,))

root = os.path.join(a.out, "神秘铭牌")
if os.path.exists(root): shutil.rmtree(root)
pdir = os.path.join(root, OPTION, "plate", "plate%06d" % PID); os.makedirs(pdir)
idir = os.path.join(root, OPTION, "AssetBundleImages", "nameplate"); os.makedirs(idir)
open(os.path.join(pdir, "Plate.xml"), "w", encoding="utf-8").write(xml)
im.save(os.path.join(idir, "ui_plate_%06d.png" % PID))
open(os.path.join(root, "说明.txt"), "w", encoding="utf-8").write(README)
zpath = os.path.join(a.out, "神秘铭牌.zip")
with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
    for dp, _, fs in os.walk(root):
        for f in fs:
            full = os.path.join(dp, f); z.write(full, os.path.relpath(full, a.out))
print("package", zpath, os.path.getsize(zpath), "bytes")
for dp, _, fs in os.walk(root):
    for f in fs: print("  ", os.path.relpath(os.path.join(dp, f), root))
if a.install:
    dst = os.path.join(a.install, OPTION)
    if os.path.exists(dst): raise SystemExit(dst + " exists, not overwriting")
    shutil.copytree(os.path.join(root, OPTION), dst); print("installed into", dst)
