"""Press keys in the off-screen test game (vkeys inside amdaemon, no focus needed) and grab the screen afterwards.
usage: python tools/ngo_key.py <out.png> [key[:ms] ...]   keys: scan C X D Service (phase_round_lib.K)"""
import os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phase_round_lib import press, get, screen

for k in sys.argv[2:]:
    name, _, ms = k.partition(":")
    press(name, int(ms) if ms else 180); time.sleep(1.2)
time.sleep(0.8)
png = get("screenshot.png", raw=True)
if png: open(sys.argv[1], "wb").write(png)
print(screen())
