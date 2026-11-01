"""Off-screen, muted capture of the entry screen (エントリーしました): attract -> press an entry button -> a frame
every 0.4 s for 8 s. usage: python tools/ngo_entry_shot.py <out_dir> [key=C]"""
import os, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phase_round_lib import press, wait_screen, DRIVE, get

OUT = sys.argv[1]
KEY = sys.argv[2] if len(sys.argv) > 2 else "C"
os.makedirs(OUT, exist_ok=True)
subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
try:
    if not wait_screen(("AdvertiseProcess",), 240): raise RuntimeError("no attract screen")
    time.sleep(6)
    press(KEY)
    time.sleep(7)    # the card / guest screen comes up; the entered screen follows the second press
    press(KEY)       # guest play: a confirm dialog, はい is the same button
    time.sleep(2)
    press(KEY)
    t0 = time.time()
    while time.time() - t0 < 10:
        png = get("screenshot.png", raw=True)
        if png: open(os.path.join(OUT, "entry_%04.1f.png" % (time.time() - t0)), "wb").write(png)
        time.sleep(0.4)
finally:
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
