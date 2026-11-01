"""Music select, round 3 (light Windose tab strip + NGO board), file side (run with the game stopped):
- back up into D:\\maimai\\backup_ngo_skin\\select_c\\ (once; never overwritten);
- sort-tab colours: every StreamingAssets XML Color listed in Codex out/select_art3/colors.json -> its proposed light
  NGO colour (level: #F7E1FB; genre / version / name / rank tables: four pastel faces in turn);
- select_sprites: tabbase_flat / tabwhite_flat (out/select_art3) and board (out/select_board2);
- select_layout.ini: hide the Genre scroll indicator's skyline map (Null_Indicator/Map).
usage: python ngo_select_c_files.py"""
import json, os, re, shutil

SEL = r"D:\maimai\Package\LocalAssets\PartnerLive2D"
BAK = r"D:\maimai\backup_ngo_skin\select_c"
COLORS = r"E:\cxref\maimai-ngo\out\select_art3\colors.json"
ART = {"tabbase_flat.png": r"E:\cxref\maimai-ngo\out\select_art3\tabbase_flat.png",
       "tabwhite_flat.png": r"E:\cxref\maimai-ngo\out\select_art3\tabwhite_flat.png",
       "board.png": r"E:\cxref\maimai-ngo\out\select_board2\board.png"}
SKYLINE = ("Null_SelectorBackgroundTarget/UI_MSS_GenreSelect(Clone)/Null_UI_MSS_GenreSelect/UI_SelectorBG/"
           "Null_CanvasGroup/Null_Indicator/Map")

entries = json.load(open(COLORS, encoding="utf-8"))["entries"]
if not os.path.exists(BAK):
    os.makedirs(BAK)
    for e in entries:
        rel = os.path.relpath(e["path"], r"D:\maimai\Package")
        dst = os.path.join(BAK, rel)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(e["path"], dst)
    shutil.copytree(os.path.join(SEL, "select_sprites"), os.path.join(BAK, "select_sprites"))
    shutil.copy2(os.path.join(SEL, "select_layout.ini"), BAK)
    print("backup ->", BAK)

pat = re.compile(r"(<Color>\s*<R>)(\d+)(</R>\s*<G>)(\d+)(</G>\s*<B>)(\d+)(</B>)")
changed = skipped = 0
for e in entries:
    t = open(e["path"], encoding="utf-8").read()
    m = pat.search(t)
    assert m, e["path"]
    rgb = [int(m.group(i)) for i in (2, 4, 6)]
    new = e["proposed_rgb"]
    if rgb == new: skipped += 1; continue
    assert rgb == e["original_rgb"], (e["path"], rgb, e["original_rgb"])
    t = t[:m.start()] + m.group(1) + str(new[0]) + m.group(3) + str(new[1]) + m.group(5) + str(new[2]) + m.group(7) + t[m.end():]
    open(e["path"], "w", encoding="utf-8", newline="").write(t)
    changed += 1
print("sort colours changed:", changed, "already set:", skipped, "of", len(entries))

for name, src in ART.items():
    shutil.copy2(src, os.path.join(SEL, "select_sprites", name))
print("select_sprites updated:", ", ".join(ART))

ini = os.path.join(SEL, "select_layout.ini")
lines = open(ini, encoding="utf-8").read().splitlines()
for i, line in enumerate(lines):
    if line.startswith("layout = "):
        layout = json.loads(line[len("layout = "):])
        layout["nodes"][SKYLINE] = {"scale": 1, "offsetX": 0, "offsetY": 0, "hidden": True, "sprite": None}
        lines[i] = "layout = " + json.dumps(layout, separators=(",", ":"))
        print("skyline map hidden")
open(ini, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
