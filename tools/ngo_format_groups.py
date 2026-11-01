"""Stock-format study for several redraw groups at once: each group is a regex over Texture2D names; for every group the
matching stock textures go through ngo_stock_format.py (PNG + FORMAT.md) and sprite rects/pivots are dumped to
SPRITES.md (atlases such as digit fonts: every sprite's rect inside its texture).
usage: python ngo_format_groups.py <out root> name=regex [name=regex ...]"""
import os, re, subprocess, sys
import UnityPy

root = sys.argv[1]
groups = [a.split("=", 1) for a in sys.argv[2:]]
env = UnityPy.load(r"D:\maimai\backup_ngo_skin\resources.assets")
tex = {o.path_id: o.peek_name() for o in env.objects if o.type.name == "Texture2D"}
sprites = []
for o in env.objects:
    if o.type.name != "Sprite": continue
    try:
        s = o.read_typetree()
    except Exception:
        continue
    tid = s["m_RD"]["texture"]["m_PathID"]
    r = s["m_Rect"]; b = s["m_Border"]
    sprites.append((tex.get(tid, ""), s["m_Name"], r["x"], r["y"], r["width"], r["height"], s["m_Pivot"]["x"], s["m_Pivot"]["y"], (b["x"], b["y"], b["z"], b["w"])))
here = os.path.dirname(os.path.abspath(__file__))
for name, rx in groups:
    pat = re.compile(rx); out = os.path.join(root, name); os.makedirs(out, exist_ok=True)
    names = sorted({n for n in tex.values() if n and pat.search(n)})
    print(name, len(names), "textures")
    lines = ["| texture | sprite | rect x,y,w,h (y from bottom) | pivot | border L,B,R,T |", "|---|---|---|---|---|"]
    for t, sn, x, y, w, h, px, py, bd in sorted(s for s in sprites if s[0] in names):
        lines.append("| %s | %s | %g,%g,%g,%g | %.3f,%.3f | %s |" % (t, sn, x, y, w, h, px, py, bd))
    open(os.path.join(out, "SPRITES.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    for i in range(0, len(names), 40):
        subprocess.run([sys.executable, os.path.join(here, "ngo_stock_format.py"), out] + names[i:i + 40], capture_output=True)
        os.replace(os.path.join(out, "FORMAT.md"), os.path.join(out, "FORMAT_%02d.md" % (i // 40)))
