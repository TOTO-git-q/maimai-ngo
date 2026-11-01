"""Find every copy of the named textures across the game's asset files (Sinmai_Data/*.assets, sharedassets, level
files and StreamingAssets AssetBundles), so a redraw can patch all copies. usage: python ngo_find_tex.py name [...]"""
import glob, os, sys
import UnityPy

names = set(sys.argv[1:])
D = r"D:\maimai\Package\Sinmai_Data"
files = [f for f in glob.glob(os.path.join(D, "*")) if os.path.isfile(f) and not f.endswith((".resS", ".resource", ".new", ".txt", ".json"))]
files += glob.glob(os.path.join(D, "StreamingAssets", "**", "*"), recursive=True)
for f in files:
    if not os.path.isfile(f) or os.path.getsize(f) < 1024 or f.endswith((".xml", ".txt", ".json", ".acb", ".awb", ".dat", ".usm", ".png", ".jpg", ".manifest")): continue
    try:
        env = UnityPy.load(f)
    except Exception:
        continue
    hits = []
    for o in env.objects:
        if o.type.name in ("Texture2D", "Sprite"):
            try:
                n = o.peek_name()
            except Exception:
                continue
            if n in names: hits.append("%s:%s" % (o.type.name, n))
    if hits: print(os.path.relpath(f, D), len(hits), sorted(set(hits))[:6])
