"""Split the release into zips of at most ~180 MB (GitHub upload timed out on the 772 MB one).
Every part keeps the folder paths, so extracting all parts into one folder rebuilds the package."""
import hashlib, os, sys, zipfile

REL = r"E:\claude workspace\maimai-ngo-public-release"
OUT = os.path.join(REL, "upload")
LIMIT = 180 * 1024 * 1024
os.makedirs(OUT, exist_ok=True)


def pack(items, base_name):
    """items: list of (arcname, size, reader); greedy fill, smallest files first so part 1 holds the installer"""
    items.sort(key=lambda x: (x[0].lower().endswith(".bin"), x[0]))
    parts, cur, size = [], [], 0
    for it in items:
        if cur and size + it[1] > LIMIT:
            parts.append(cur); cur, size = [], 0
        cur.append(it); size += it[1]
    if cur: parts.append(cur)
    names = []
    for i, part in enumerate(parts, 1):
        name = "%s.part%dof%d.zip" % (base_name, i, len(parts))
        with zipfile.ZipFile(os.path.join(OUT, name), "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
            for arc, _, reader in part:
                z.writestr(zipfile.ZipInfo(arc, date_time=(2026, 11, 1, 0, 0, 0)), reader(), compress_type=zipfile.ZIP_DEFLATED)
        names.append(name)
        print(name, round(os.path.getsize(os.path.join(OUT, name)) / 2**20), "MB", len(part), "files", flush=True)
    return names


# the package folder
stage = os.path.join(REL, "maimai-ngo-skin-20261101")
items = []
for dp, _, fn in os.walk(stage):
    for f in fn:
        p = os.path.join(dp, f)
        arc = "maimai-ngo-skin-20261101/" + os.path.relpath(p, stage).replace("\\", "/")
        items.append((arc, os.path.getsize(p), (lambda p=p: open(p, "rb").read())))
skin = pack(items, "maimai-ngo-skin-20261101")

# the art source zip: repack its members
src = zipfile.ZipFile(os.path.join(REL, "maimai-ngo-art-source-20261101.zip"))
items = [(i.filename, i.file_size, (lambda n=i.filename: src.read(n))) for i in src.infolist() if not i.is_dir()]
art = pack(items, "maimai-ngo-art-source-20261101")

with open(os.path.join(OUT, "SHA256SUMS.txt"), "w", encoding="ascii", newline="\n") as f:
    for n in skin + art:
        h = hashlib.sha256(open(os.path.join(OUT, n), "rb").read()).hexdigest()
        f.write("%s  %s\n" % (h, n))
print("done")
