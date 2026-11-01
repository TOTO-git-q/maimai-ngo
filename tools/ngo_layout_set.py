"""Set top-level keys of the music-select preset (the `layout = {json}` line of select_layout.ini).
usage: python ngo_layout_set.py key=value [...]   (value parsed as JSON, e.g. tabLabelOffsetY=0)"""
import json, sys

INI = r"D:\maimai\Package\LocalAssets\PartnerLive2D\select_layout.ini"
lines = open(INI, encoding="utf-8").read().split("\n")
i = next(n for n, l in enumerate(lines) if l.startswith("layout = "))
d = json.loads(lines[i][len("layout = "):])
for kv in sys.argv[1:]:
    k, v = kv.split("=", 1); d[k] = json.loads(v)
lines[i] = "layout = " + json.dumps(d, separators=(",", ":"))
open(INI, "w", encoding="utf-8", newline="").write("\n".join(lines))
print("set", ", ".join(sys.argv[1:]))
