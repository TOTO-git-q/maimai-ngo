"""Compare two heap walks (PartnerLive2D test/heapwalk .tsv): which roots and which types grew between them.
usage: python ngo_heapwalk_diff.py <earlier.tsv> <later.tsv> [top=25]"""
import csv, sys


def load(p):
    d = {"root": {}, "type": {}, "meta": {}}
    with open(p, encoding="utf-8", errors="replace") as f:
        for r in csv.DictReader(f, delimiter="\t"):
            d[r["kind"]][r["name"]] = (int(r["count"]), int(r["bytes"]))
    return d


a, b = load(sys.argv[1]), load(sys.argv[2])
top = int(sys.argv[3]) if len(sys.argv) > 3 else 25
for k in ("heap_after_gc", "reached"):
    x, y = a["meta"].get(k, (0, 0)), b["meta"].get(k, (0, 0))
    print("%-14s %8.1f MB -> %8.1f MB  (%+.1f MB, objects %+d)" % (k, x[1] / 2**20, y[1] / 2**20, (y[1] - x[1]) / 2**20, y[0] - x[0]))
short = lambda s: s.replace(", Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null", "")[:120]
for kind in ("root", "type"):
    keys = set(a[kind]) | set(b[kind])
    rows = sorted(((b[kind].get(n, (0, 0))[1] - a[kind].get(n, (0, 0))[1], n) for n in keys), reverse=True)
    print("\n%s grew most:" % kind)
    for dbytes, n in rows[:top]:
        if dbytes <= 0: break
        ca, cb = a[kind].get(n, (0, 0))[0], b[kind].get(n, (0, 0))[0]
        print("  %+8.2f MB  %+8d obj  (now %7.2f MB)  %s" % (dbytes / 2**20, cb - ca, b[kind].get(n, (0, 0))[1] / 2**20, short(n)))
