"""Second pass after ngo_patch_colors.py, written straight into resources.assets:
- the runtime selector colors live in Null_Selector_ColorBase._colors (indexed by difficulty: BASIC green, ADVANCED
  orange, EXPERT red, MASTER purple, Re:MASTER, utage...) and in _selectorColor. Difficulty colors mean something and
  stay; only maimai's category / level-view blues become NGO lavender;
- the BASIC greens ngo_patch_colors.py turned pink on the panel images are put back (green = BASIC).
usage: python ngo_patch_color_fields.py <ngo_patch_colors log.csv> <log.csv>"""
import csv, sys
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

FIRST_LOG, LOG = sys.argv[1], sys.argv[2]
ASSETS = r"D:\maimai\Package\Sinmai_Data\resources.assets"
BLUE = {"0A87F0": "C3A6E3", "2E94F4": "CEB2E8"}   # same targets as the panel pass (ngo_ramp violet, tint range)
KEEP_GREEN = {"43C143", "6FC943"}                  # BASIC difficulty greens

env = UnityPy.load(ASSETS)
gen = TypeTreeGenerator("2018.4.7f1")
gen.load_local_dll_folder(r"D:\maimai\Package\Sinmai_Data\Managed")
env.typetree_generator = gen

revert = {int(r["path_id"]): r["before"] for r in csv.DictReader(open(FIRST_LOG, encoding="utf-8")) if r["before"] in KEEP_GREEN}
hx = lambda c: "%02X%02X%02X" % tuple(round(c[k] * 255) for k in "rgb")


def put(c, h):
    c["r"], c["g"], c["b"] = (int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


rows = []
for o in env.objects:
    if o.type.name != "MonoBehaviour": continue
    if o.path_id not in revert:
        # cheap filter: only components that can hold the fields we change
        pass
    try:
        t = o.read_typetree()
    except Exception:
        continue
    changed = False
    if o.path_id in revert and "m_Color" in t:
        rows.append([o.path_id, "m_Color", hx(t["m_Color"]), revert[o.path_id]]); put(t["m_Color"], revert[o.path_id]); changed = True
    for i, c in enumerate(t.get("_colors") or []):
        if isinstance(c, dict) and hx(c) in BLUE:
            rows.append([o.path_id, "_colors[%d]" % i, hx(c), BLUE[hx(c)]]); put(c, BLUE[hx(c)]); changed = True
    c = t.get("_selectorColor")
    if isinstance(c, dict) and hx(c) in BLUE:
        rows.append([o.path_id, "_selectorColor", hx(c), BLUE[hx(c)]]); put(c, BLUE[hx(c)]); changed = True
    if changed: o.save_typetree(t)

data = env.file.save()
assert len(data) > 60 * 1024 * 1024, "output unexpectedly small: %d bytes" % len(data)
with open(ASSETS + ".new", "wb") as f:
    f.write(data)
with open(LOG, "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f); w.writerow(["path_id", "field", "before", "after"]); w.writerows(rows)
print("changed", len(rows), "colors")
