"""Attract-screen check (off screen, muted): start the test game, record which processes run and grab a frame every
3 s for SECONDS after the attract screen appears, then stop. Used to confirm the attract screen holds the title.
usage: python tools/ngo_attract_check.py <out dir> [seconds]"""
import os, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phase_round_lib import screen, wait_screen, say_to, DRIVE, get

OUT = sys.argv[1]; SECONDS = float(sys.argv[2]) if len(sys.argv) > 2 else 90
os.makedirs(OUT, exist_ok=True)
say = say_to(os.path.join(OUT, "check.log"))
subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
try:
    if not wait_screen(("AdvertiseProcess",), 300): raise RuntimeError("no attract screen")
    t = time.time(); last = None
    while time.time() - t < SECONDS:
        s = screen()
        if s != last: say("screen", s); last = s
        png = get("screenshot.png", raw=True)
        if png: open(os.path.join(OUT, "attract_%03d.png" % int(time.time() - t)), "wb").write(png)
        time.sleep(float(os.environ.get('CHECK_DT', '3')))
finally:
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
