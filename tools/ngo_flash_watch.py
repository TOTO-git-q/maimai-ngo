"""Catch a screen that only flashes by (read only): poll the UI dump fast and log every texture the moment it first
appears, with the screen it appeared on; a screenshot is taken once when a new screen starts.
usage: python ngo_flash_watch.py <out dir> [seconds]"""
import io, json, os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phase_round_lib import get, screen
from PIL import Image

OUT = sys.argv[1]; SEC = float(sys.argv[2]) if len(sys.argv) > 2 else 90
os.makedirs(OUT, exist_ok=True)
log = open(os.path.join(OUT, "flash.log"), "a", encoding="utf-8")
seen, last = set(), None
first = get("test/ui") or {}
for it in first.get("items", []): seen.add(it.get("texture"))
t = time.time()
while time.time() - t < SEC:
    s = screen()
    if s != last:
        last = s; log.write("%s screen %s\n" % (time.strftime("%H:%M:%S"), s)); log.flush()
        png = get("screenshot.png", raw=True)
        if png:
            im = Image.open(io.BytesIO(png)).convert("RGB"); im.resize((im.width // 4, im.height // 4)).save(os.path.join(OUT, "%s_%s.png" % (time.strftime("%H%M%S"), s)))
    ui = get("test/ui") or {}
    new = [it for it in ui.get("items", []) if it.get("texture") not in seen]
    for it in new:
        seen.add(it.get("texture"))
        log.write("%s %s new %s\n" % (time.strftime("%H:%M:%S"), s, json.dumps({k: it.get(k) for k in ("texture", "x", "y", "w", "h", "path", "renderer")}, ensure_ascii=False)))
    if new:
        log.flush(); json.dump(ui, open(os.path.join(OUT, "%s_%s.ui.json" % (time.strftime("%H%M%S"), s)), "w", encoding="utf-8"), ensure_ascii=False)
    time.sleep(0.25)
