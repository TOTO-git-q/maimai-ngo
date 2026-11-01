"""Grep a walk's *.ui.json (/api/test/ui) for Images whose sprite / texture / parent path contains any keyword.
usage: python ngo_ui_grep.py <file.ui.json> <keyword> [...]"""
import json, sys

d = json.load(open(sys.argv[1], encoding="utf-8"))
keys = [k.lower() for k in sys.argv[2:]]
for i in d["items"]:
    s = " ".join(str(i.get(k, "")) for k in ("sprite", "texture", "path")).lower()
    if any(k in s for k in keys):
        print(i["sprite"], "|", i["texture"], "|", i.get("color"), "|", i.get("x"), i.get("y"), i.get("w"), i.get("h"), "|", i["path"])
