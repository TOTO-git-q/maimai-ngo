"""Build the NGO skin test theme folder from the dot handoff repo (maimai-ngo-skin) for PartnerLive2D's theme override.
Install list = registry/redraw_status.json selected outputs, with current_selection_overlay.json current_selected taking
precedence. Left out (the stock texture stays): holds / failed / blocked / owner-review-only, original reuse, owner-rejected
(Star00, Touch01, Utage2P), Nationaldata S/SS/SSS/SSSp, CardCover, identities only listed as nonselected diagnostics,
drafts/, and PNGs whose size differs from the original.
usage: python build_ngo_theme.py <repo> <out_dir>"""
import csv, hashlib, json, os, shutil, struct, sys

REPO, OUT = sys.argv[1], sys.argv[2]
SKIP_NAMES = {"UI_MSS_Nationaldata_S", "UI_MSS_Nationaldata_SS", "UI_MSS_Nationaldata_SSS", "UI_MSS_Nationaldata_SSSp"}
BAD_WORDS = ("hold", "blocked", "failed", "not_publishable", "no_acceptable", "original_reuse", "awaiting_specific")

def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    return struct.unpack(">II", head[16:24]) if head[:8] == b"\x89PNG\r\n\x1a\n" else None

def sha256(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()

reg = json.load(open(os.path.join(REPO, "registry", "redraw_status.json"), encoding="utf-8"))
ov = json.load(open(os.path.join(REPO, "current_selection_overlay.json"), encoding="utf-8"))
rejected = {os.path.basename(p) for p in reg["approval_sources"]["owner_20261028_first_three_rejected"]["resolved_original_paths"]}
nonselected = {d.get("identity") for d in ov["nonselected_dispositions"]}

chosen, skipped = {}, []
for a in reg["assets"]:
    ident, out = a.get("original_path"), a.get("output_path")
    tf = (a.get("stages", {}).get("technical_fit") or {}).get("status", "")
    oa = (a.get("stages", {}).get("owner_approved") or {}).get("status", "")
    why = None
    if not out: why = "no output"
    elif out.startswith("drafts/"): why = "draft"
    elif any(w in (tf + "|" + oa + "|" + str(a.get("next_action"))).lower() for w in BAD_WORDS): why = "hold/failed/review-only: " + tf
    elif ident in nonselected: why = "nonselected diagnostic"
    if why: skipped.append((ident, why)); continue
    chosen[ident] = ("registry", out)
for c in ov["current_selected"]:
    chosen[c["identity"]] = ("overlay_r2", c["selected_png"]["path"])

os.makedirs(OUT, exist_ok=True)
rows, names = [], {}
for ident, (src, out) in sorted(chosen.items()):
    name = os.path.splitext(os.path.basename(ident))[0]
    orig, new = os.path.join(REPO, ident), os.path.join(REPO, out)
    why = None
    if os.path.basename(ident) in rejected: why = "owner rejected"
    elif name in SKIP_NAMES: why = "Nationaldata technical gate"
    elif "CardCover" in name: why = "collectible scope"
    elif not os.path.exists(new): why = "output missing in repo"
    elif os.path.exists(orig) and png_size(orig) != png_size(new): why = "size differs from original %s vs %s" % (png_size(orig), png_size(new))
    elif name in names: why = "duplicate texture name (also %s)" % names[name]
    if why: skipped.append((ident, why)); continue
    names[name] = ident
    shutil.copyfile(new, os.path.join(OUT, name + ".png"))
    rows.append({"texture": name, "identity": ident, "source": src, "png": out, "sha256": sha256(new), "size": "%dx%d" % png_size(new)})

with open(os.path.join(OUT, "_manifest.csv"), "w", newline="", encoding="utf-8") as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0].keys())); w.writeheader(); w.writerows(rows)
with open(os.path.join(OUT, "_skipped.csv"), "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f); w.writerow(["identity", "reason"]); w.writerows(skipped)
print("installed", len(rows), "| overlay", sum(r["source"] == "overlay_r2" for r in rows), "| skipped", len(skipped))
from collections import Counter
print(Counter(r.split(":")[0] for _, r in skipped).most_common())
