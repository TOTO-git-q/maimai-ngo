"""Print the background part of a /api/test/renderers dump (renderers.json from a BURST walk).
usage: python ngo_renderers_print.py <renderers.json>"""
import json, sys

d = json.load(open(sys.argv[1], encoding="utf-8"))
for c in d["cameras"]:
    print("CAM", c)
for r in d["renderers"]:
    p = r["path"]
    if any(k in p for k in ("Hold", "Release", "Tap", "Grass", "Pillar", "FiveLine")): continue
    print(p.split("Adjust")[-1][:48], r["type"][:6], "vis", r["visible"], "off@" + r.get("inactiveAt", ""), "L", r["layer"], r["sortingLayer"], r["sortingLayerValue"],
          "o", r["order"], "q", r["queue"], r["shader"][-16:], r.get("color"), "n", r.get("particles"), r["center"], r["size"],
          r["cameras"])
