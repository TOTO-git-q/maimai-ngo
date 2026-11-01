"""Set every music-select sort-category tab colour (the StreamingAssets XMLs listed in Codex select_art3/colors.json)
either to white (painted tab textures show their own colours) or, with `ngo`, to deep NGO tints for the white-mask
tabs (out/tabset_v3) under the stock white label text.
Backups of the original XMLs are in D:\\maimai\\backup_ngo_skin\\select_c (made by ngo_select_c_files.py).
usage: python ngo_sort_colors_white.py [white|ngo]"""
import json, re, sys

mode = sys.argv[1] if len(sys.argv) > 1 else "white"
entries = json.load(open(r"E:\cxref\maimai-ngo\out\select_art3\colors.json", encoding="utf-8"))["entries"]
pat = re.compile(r"(<Color>\s*<R>)(\d+)(</R>\s*<G>)(\d+)(</G>\s*<B>)(\d+)(</B>)")
DEEP = {"#F7E1FB": "#815CDD", "#7FD8F0": "#3A9FD6", "#FCE1F1": "#E05AB4", "#FFE36E": "#4D23CF"}
hexrgb = lambda h: [int(h[i:i + 2], 16) for i in (1, 3, 5)]
n = 0
for e in entries:
    # ngo: the labels keep the stock white text, so the tabs need dark tints like the stock (#9E3C8C for every level):
    # levels violet, other tables cycle four deep NGO colours in place of the pastel proposals
    want = [255, 255, 255] if mode == "white" else hexrgb("#815CDD" if e["table"] == "musicLevel" else DEEP[e["proposed_hex"]])
    t = open(e["path"], encoding="utf-8").read()
    m = pat.search(t)
    assert m, e["path"]
    if [int(m.group(i)) for i in (2, 4, 6)] == want: continue
    t = t[:m.start()] + m.group(1) + str(want[0]) + m.group(3) + str(want[1]) + m.group(5) + str(want[2]) + m.group(7) + t[m.end():]
    open(e["path"], "w", encoding="utf-8", newline="").write(t); n += 1
print("sort colours set to", mode + ":", n, "of", len(entries))
