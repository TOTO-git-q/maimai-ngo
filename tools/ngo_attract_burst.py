"""Capture the attract loop of a running game (read only): a screenshot (quarter size) and the UI dump every few
seconds while the attract screen runs, to find screens that only appear in the loop.
usage: python ngo_attract_burst.py <out dir> [seconds] [interval]"""
import json, os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phase_round_lib import get, screen
from PIL import Image
import io

OUT = sys.argv[1]; SEC = float(sys.argv[2]) if len(sys.argv) > 2 else 150; DT = float(sys.argv[3]) if len(sys.argv) > 3 else 2.5
os.makedirs(OUT, exist_ok=True)
t = time.time(); n = 0
while time.time() - t < SEC:
    s = screen()
    png = get("screenshot.png", raw=True)
    if png:
        im = Image.open(io.BytesIO(png)).convert("RGB")
        im.resize((im.width // 4, im.height // 4)).save(os.path.join(OUT, "%03d_%s.png" % (n, s)))
        ui = get("test/ui")
        if ui: json.dump(ui, open(os.path.join(OUT, "%03d_%s.ui.json" % (n, s)), "w", encoding="utf-8"), ensure_ascii=False)
        n += 1
    time.sleep(DT)
print("captured", n)
