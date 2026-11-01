"""Add hidden nodes to the music-select preset (select_layout.ini), keeping everything else.
usage: python ngo_select_hide_node.py <node path under Canvas/Main> [...]"""
import json, os, sys

INI = r"D:\maimai\Package\LocalAssets\PartnerLive2D\select_layout.ini"
lines = open(INI, encoding="utf-8").read().splitlines()
for i, line in enumerate(lines):
    if line.startswith("layout = "):
        layout = json.loads(line[len("layout = "):])
        for path in sys.argv[1:]:
            layout["nodes"][path] = {"scale": 1, "offsetX": 0, "offsetY": 0, "hidden": True, "sprite": None}
            print("hidden:", path)
        lines[i] = "layout = " + json.dumps(layout, separators=(",", ":"))
open(INI, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
