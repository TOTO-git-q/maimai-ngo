"""List Texture2Ds whose name matches a regex, with size, format and the sprites cut from each (count + first names).
Reads the untouched pre-skin backup. usage: python ngo_tex_list.py <regex> [out.md]"""
import re, sys
import UnityPy

pat = re.compile(sys.argv[1])
env = UnityPy.load(r"D:\maimai\backup_ngo_skin\resources.assets")
tex, sprites = {}, {}
for o in env.objects:
    if o.type.name == "Texture2D":
        n = o.peek_name()
        if n and pat.search(n):
            t = o.read(); tex[o.path_id] = (n, t.m_Width, t.m_Height, str(t.m_TextureFormat).split(".")[-1])
for o in env.objects:
    if o.type.name == "Sprite":
        try:
            s = o.read(); tid = s.m_RD.texture.path_id
        except Exception:
            continue
        if tid in tex: sprites.setdefault(tid, []).append(s.m_Name)
lines = ["| texture | size | format | sprites |", "|---|---|---|---|"]
for pid, (n, w, h, f) in sorted(tex.items(), key=lambda kv: kv[1][0]):
    sp = sorted(sprites.get(pid, []))
    lines.append("| %s | %dx%d | %s | %d: %s |" % (n, w, h, f, len(sp), ", ".join(sp[:12]) + (" ..." if len(sp) > 12 else "")))
txt = "\n".join(lines); print(txt)
if len(sys.argv) > 2: open(sys.argv[2], "w", encoding="utf-8").write(txt + "\n")
