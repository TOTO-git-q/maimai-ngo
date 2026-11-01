"""Dump named Texture2Ds from the installed resources.assets (and the stock backup) to PNG, for review.
usage: python ngo_dump_tex.py <out dir> <name> [...]   (writes now_<name>.png and stock_<name>.png; a second texture
of the same name and another size goes to <tag>_<name>@<w>x<h>.png)"""
import os, sys
import UnityPy

OUT = sys.argv[1]; NAMES = set(sys.argv[2:])
os.makedirs(OUT, exist_ok=True)
SRC = {"now": r"D:\maimai\Package\Sinmai_Data\resources.assets",
       "stock": r"D:\maimai\backup_ngo_skin\resources.assets"}   # the stock file sits next to its .resS there
for tag, path in SRC.items():
    env = UnityPy.load(path)
    first = {}
    for o in env.objects:
        if o.type.name != "Texture2D" or o.peek_name() not in NAMES: continue
        t = o.read()
        size = (t.m_Width, t.m_Height)
        if first.setdefault(t.m_Name, size) == size: fn = "%s_%s.png" % (tag, t.m_Name)
        else: fn = "%s_%s@%dx%d.png" % (tag, t.m_Name, size[0], size[1])
        t.image.save(os.path.join(OUT, fn))
        print(tag, t.m_Name, t.m_Width, t.m_Height, t.m_TextureFormat)
