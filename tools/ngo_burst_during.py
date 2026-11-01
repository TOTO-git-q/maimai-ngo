"""While another script drives the game, capture a burst of screenshots (quarter size) during the named screens.
usage: python ngo_burst_during.py <out dir> <seconds total> <interval> <screen> [screen ...]"""
import io, os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phase_round_lib import get, screen
from PIL import Image

OUT, SEC, DT, NAMES = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), set(sys.argv[4:])
os.makedirs(OUT, exist_ok=True)
t = time.time(); n = 0
while time.time() - t < SEC:
    s = screen()
    if s in NAMES:
        png = get("screenshot.png", raw=True)
        if png:
            im = Image.open(io.BytesIO(png)).convert("RGB")
            im.resize((im.width // 4, im.height // 4)).save(os.path.join(OUT, "%s_%03d_%s.png" % (time.strftime("%H%M%S"), n, s))); n += 1
        time.sleep(DT)
    else:
        time.sleep(0.2)
print("captured", n)
