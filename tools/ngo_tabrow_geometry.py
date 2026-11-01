"""Tab row geometry pass (select_layout.ini), measured from the ui.json dump in screen px (y up):
- one baseline B = -481 (side tab bottoms): the board's title bar top, the dome's bottom and the tab bottoms meet there;
- board moved down (same size) so its bar top is B instead of -404 (it covered too much);
- side tabs shortened by ngo_tab_shorten.py (body -418..-481): labels down to the new body centre;
- dome node down 7 px; side tab gaps made even (8 px); the tray rail (SelectorTab/WhiteBase) hidden, the bar replaces it.
Scales: board image config px = screen px / 0.84; tab-group node offsets = screen px / 0.88; tab label offset in tab
local px = screen px / 1.43.
usage: python ngo_tabrow_geometry.py"""
import json

INI = r"D:\maimai\Package\LocalAssets\PartnerLive2D\select_layout.ini"
B, BOARD_TOP_NOW, LABEL_NOW = -481.0, -404.0, -430.0
lines = open(INI, encoding="utf-8").read().split("\n")
i = next(n for n, l in enumerate(lines) if l.startswith("layout = "))
d = json.loads(lines[i][len("layout = "):])
nodes = d["nodes"]
g = "Null_GenreControll/SelectorTab(Clone)/"

d["images"]["board"]["y"] = round(d["images"]["board"]["y"] + (B - BOARD_TOP_NOW) / 0.84, 1)
d["tabLabelOffsetY"] = round(((-418 + B) / 2 - LABEL_NOW) / 1.43, 1)
nodes[g + "Tab_CenterBase"]["offsetY"] = round(nodes[g + "Tab_CenterBase"]["offsetY"] - 7 / 0.88, 1)
for name, dx in (("Null_Tab_L02", 3), ("Null_Tab_R02", -5), ("Null_Tab_L03", 5), ("Null_Tab_R01", -9)):
    nodes[g + name]["offsetX"] = round(nodes[g + name]["offsetX"] + dx / 0.88, 1)
nodes[g + "WhiteBase"] = {"scale": 1, "offsetX": 0, "offsetY": 0, "hidden": True, "sprite": None}
lines[i] = "layout = " + json.dumps(d, separators=(",", ":"))
open(INI, "w", encoding="utf-8", newline="").write("\n".join(lines))
print("board y", d["images"]["board"]["y"], "| label offset", d["tabLabelOffsetY"], "| centre",
      nodes[g + "Tab_CenterBase"]["offsetY"], "|", {k: nodes[g + k]["offsetX"] for k in ("Null_Tab_L03", "Null_Tab_L02", "Null_Tab_R02", "Null_Tab_R01")})
