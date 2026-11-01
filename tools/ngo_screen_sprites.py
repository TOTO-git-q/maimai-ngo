"""List the sprites a captured screen draws (from the walk's NN_<tag>.ui.json dumps): texture, sprite, rect, Image type,
tint, GameObject path; one table per dump, biggest rects first. Used to scope a redraw before the stock-format study.
usage: python ngo_screen_sprites.py <out.md> <ui.json> [...]"""
import json, os, sys

out = [];
for f in sys.argv[2:]:
    d = json.load(open(f, encoding="utf-8"))
    rows = {}
    for x in d.get("items", []):
        tex = x.get("texture") or x.get("sprite") or ""
        if not tex: continue
        x = dict({"w": 0, "h": 0, "type": x.get("renderer", "")}, **x)   # SpriteRenderers carry no rect
        key = (tex, x.get("path", "")[-70:])
        if key not in rows: rows[key] = x
    out.append("## %s (%d sprites)\n" % (os.path.basename(f), len(rows)))
    out.append("| texture | w x h | type | tint | path |\n|---|---|---|---|---|")
    for (tex, path), x in sorted(rows.items(), key=lambda kv: -abs(kv[1]["w"] * kv[1]["h"])):
        out.append("| %s | %dx%d | %s | %s | %s |" % (tex, x["w"], x["h"], x.get("type", ""), x.get("color", ""), path))
    out.append("")
open(sys.argv[1], "w", encoding="utf-8").write("\n".join(out))
print("\n".join(out)[:6000])
