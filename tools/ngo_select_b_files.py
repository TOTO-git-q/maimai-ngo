"""Music select, option B, file side (run with the game stopped):
- back up into D:\\maimai\\backup_ngo_skin\\select_b\\ (once; an existing backup is never overwritten);
- level-sort tab colour: every StreamingAssets/A000/musicLevel/*/MusicLevel.xml Color (251,114,95) -> NGO plum
  #9E3C8C (158,60,140) (the colour only marks the level-sort family; the labels carry the levels);
- select_sprites: new tabbase_flat / tabwhite_flat / board (Codex out/select_art2);
- select_layout.ini: drop the BACK/NEXT FlatButton sprite overrides, so both use the game's own buttons.
usage: python ngo_select_b_files.py"""
import glob, json, os, re, shutil

PKG = r"D:\maimai\Package"
LEVELS = os.path.join(PKG, r"Sinmai_Data\StreamingAssets\A000\musicLevel")
SEL = os.path.join(PKG, r"LocalAssets\PartnerLive2D")
ART = r"E:\cxref\maimai-ngo\out\select_art2\replacements"
BAK = r"D:\maimai\backup_ngo_skin\select_b"
OLD, NEW = (251, 114, 95), (158, 60, 140)
BUTTONS = ("UI_Buttons/UI_Button04/FlatButton(Clone)/IMG_Button", "UI_Buttons/UI_Button05/FlatButton(Clone)/IMG_Button")

xmls = sorted(glob.glob(os.path.join(LEVELS, "*", "MusicLevel.xml")))
assert xmls, "no MusicLevel.xml found"
if not os.path.exists(BAK):
    os.makedirs(os.path.join(BAK, "musicLevel"))
    for x in xmls:
        d = os.path.join(BAK, "musicLevel", os.path.basename(os.path.dirname(x)))
        os.makedirs(d); shutil.copy2(x, d)
    shutil.copytree(os.path.join(SEL, "select_sprites"), os.path.join(BAK, "select_sprites"))
    shutil.copy2(os.path.join(SEL, "select_layout.ini"), BAK)
    print("backup ->", BAK)

pat = re.compile(r"(<Color>\s*<R>)(\d+)(</R>\s*<G>)(\d+)(</G>\s*<B>)(\d+)(</B>)")
changed = 0
for x in xmls:
    t = open(x, encoding="utf-8").read()
    m = pat.search(t)
    assert m, x
    rgb = tuple(int(m.group(i)) for i in (2, 4, 6))
    if rgb == NEW: continue
    assert rgb == OLD, (x, rgb)
    t = t[:m.start()] + m.group(1) + str(NEW[0]) + m.group(3) + str(NEW[1]) + m.group(5) + str(NEW[2]) + m.group(7) + t[m.end():]
    open(x, "w", encoding="utf-8", newline="").write(t)
    changed += 1
print("MusicLevel colours changed:", changed, "of", len(xmls))

for n in ("tabbase_flat.png", "tabwhite_flat.png", "board.png"):
    shutil.copy2(os.path.join(ART, n), os.path.join(SEL, "select_sprites", n))
print("select_sprites updated")

ini = os.path.join(SEL, "select_layout.ini")
lines = open(ini, encoding="utf-8").read().splitlines()
for i, line in enumerate(lines):
    if line.startswith("layout = "):
        layout = json.loads(line[len("layout = "):])
        removed = [k for k in BUTTONS if layout["nodes"].pop(k, None) is not None]
        lines[i] = "layout = " + json.dumps(layout, separators=(",", ":"))
        print("button overrides removed:", len(removed))
open(ini, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
