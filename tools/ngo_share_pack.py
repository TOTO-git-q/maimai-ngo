"""Build the shareable NGO nameplate package (owner 2026-11-07): the four animated 雨 plates + the 雨姐 mystery plate.
Pure game data plus one small standalone mod; nothing from this machine's account or settings.
  Package\\Sinmai_Data\\StreamingAssets\\A588\\plate\\plate<id>\\Plate.xml        5 plates
  Package\\Sinmai_Data\\StreamingAssets\\A588\\AssetBundleImages\\nameplate\\ui_plate_<id>.png   stills (AquaMai LoadLocalImages)
  Package\\LocalAssets\\NgoPlates\\<id>\\{ame,kangel,to_kangel,to_ame}.bc7     frames (tools\\ngo_plate_pack.py format)
  Package\\Mods\\NgoPlates.dll                                                 plates_mod: animation + Ame/KAngel switch
Every plate unlocks by itself: release condition Track / DIFFICULTY ANY (true for any song), checked after every track,
and it stays hidden in the collection until then (dispCond Lock).
usage: python tools/ngo_share_pack.py [--out <dir>]"""
import argparse, os, re, shutil, zipfile
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
TEMPLATE = r"D:\maimai\Package\Sinmai_Data\StreamingAssets\A000\plate\plate709501\Plate.xml"
NAMEPLATE = r"E:\claude workspace\maimai-nameplate"
FRAMES = r"D:\maimai\Package\LocalAssets\NgoPlates"           # written by ngo_plate_pack.py --frames-out
DLL = os.path.join(HERE, r"..\plates_mod\bin\NgoPlates.dll")
OPTION = "A588"
PLATES = [  # id, shown name, still
    (986001, "？？？", os.path.join(NAMEPLATE, r"build\yujie\UI_Plate_yujie.png"), False),
    (990001, "雨極", os.path.join(NAMEPLATE, r"final\雨極\UI_Plate_goku_ame.png"), True),
    (990002, "雨将", os.path.join(NAMEPLATE, r"final\雨将\UI_Plate_shou_ame.png"), True),
    (990003, "雨神", os.path.join(NAMEPLATE, r"final\雨神\UI_Plate_kami_ame.png"), True),
    (990004, "雨舞舞", os.path.join(NAMEPLATE, r"final\雨舞舞\UI_Plate_maimai_ame.png"), True),
]
README = """NGO 铭牌包（maimai DX 1.70）

内容：5 块铭牌（4 块会动的「雨」牌 + 1 块神秘的牌子）。
「雨」牌在菜单和选歌时是糖糖，开始打歌时变身超天酱，回到选歌再变回糖糖。

安装
1. 把压缩包里的 Package 文件夹合并到你游戏的 Package 文件夹（Sinmai.exe 所在的那个）。
   你的整合包如果把选项包放在别处（例如 option 文件夹），就把 A588 放到那边，和其他 Axxx 放一起。
2. 需要 MelonLoader + AquaMai，并开启 AquaMai 的「直读图片」[GameSystem.Assets.LoadLocalImages]
   （装过自制谱的一般已经开着）。
3. 正常登录打一首歌，牌子会自己出现在收藏的铭牌里（「その他」分类）。

只放静态图、不想要动画：不放 Mods\\NgoPlates.dll 和 LocalAssets\\NgoPlates 就行，牌子还在，只是不会动。
卸载：删掉 A588、Mods\\NgoPlates.dll、LocalAssets\\NgoPlates。
编号 986001、990001–990004；不改游戏原有文件，也不碰账号设置。
请勿用于商业用途。
"""

def plate_xml(template, pid, name):
    xml = template.replace("<dataName>plate709501</dataName>", "<dataName>plate%06d</dataName>" % pid)
    xml = re.sub(r"<name>\s*<id>709501</id>\s*<str>[^<]*</str>\s*</name>", "<name>\n    <id>%d</id>\n    <str>%s</str>\n  </name>" % (pid, name), xml)
    xml = re.sub(r"<genre>\s*<id>3</id>\s*<str>[^<]*</str>\s*</genre>", "<genre>\n    <id>5</id>\n    <str>その他</str>\n  </genre>", xml)
    xml = re.sub(r"<normText>[^<]*</normText>", "<normText>？？？</normText>", xml)
    c0 = re.search(r"<condition0>.*?</condition0>", xml, re.S).group(0)
    xml = xml.replace(c0, c0.replace("<kindTrack>None</kindTrack>", "<kindTrack>DIFFICULTY</kindTrack>"))
    for must in ("<id>%d</id>" % pid, "<str>%s</str>" % name, "<category>Track</category>", "<kindTrack>DIFFICULTY</kindTrack>",
                 "<difficultyId>ANY</difficultyId>", "<dispCond>Lock</dispCond>", "<disable>false</disable>"):
        if must not in xml: raise SystemExit("Plate.xml check failed for %d: %s" % (pid, must))
    return xml

ap = argparse.ArgumentParser(); ap.add_argument("--out", default=os.path.join(HERE, r"..\release\ngo_plates_share"))
a = ap.parse_args()
out = os.path.abspath(a.out); root = os.path.join(out, "NGO铭牌包"); pkg = os.path.join(root, "Package")
if os.path.exists(root): shutil.rmtree(root)
template = open(TEMPLATE, encoding="utf-8").read()
sa = os.path.join(pkg, "Sinmai_Data", "StreamingAssets", OPTION)
for pid, name, still, animated in PLATES:
    d = os.path.join(sa, "plate", "plate%06d" % pid); os.makedirs(d)
    open(os.path.join(d, "Plate.xml"), "w", encoding="utf-8").write(plate_xml(template, pid, name))
    im = Image.open(still).convert("RGBA")
    if im.size != (720, 116): raise SystemExit("%s is %s" % (still, im.size))
    idir = os.path.join(sa, "AssetBundleImages", "nameplate"); os.makedirs(idir, exist_ok=True)
    im.save(os.path.join(idir, "ui_plate_%06d.png" % pid))
    if animated:
        src = os.path.join(FRAMES, str(pid))
        for clip in ("ame", "kangel", "to_kangel", "to_ame"):
            f = os.path.join(src, clip + ".bc7")
            if not os.path.isfile(f) or os.path.getsize(f) % (720 * 116): raise SystemExit("bad frames " + f)
        shutil.copytree(src, os.path.join(pkg, "LocalAssets", "NgoPlates", str(pid)))
os.makedirs(os.path.join(pkg, "Mods")); shutil.copy2(DLL, os.path.join(pkg, "Mods", "NgoPlates.dll"))
open(os.path.join(root, "说明.txt"), "w", encoding="utf-8").write(README)

# nothing private may ride along: only the files above, no ini/log/json/txt besides the readme
allowed = {".xml", ".png", ".bc7", ".dll"}
for dp, _, fs in os.walk(root):
    for f in fs:
        if f == "说明.txt" and dp == root: continue
        if os.path.splitext(f)[1].lower() not in allowed: raise SystemExit("unexpected file " + os.path.join(dp, f))
zpath = os.path.join(out, "NGO铭牌包.zip")
with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
    for dp, _, fs in os.walk(root):
        for f in fs:
            full = os.path.join(dp, f); z.write(full, os.path.relpath(full, out))
n = sum(len(fs) for _, _, fs in os.walk(root))
print("package", zpath, "%.1f MB" % (os.path.getsize(zpath) / 1e6), n, "files")
