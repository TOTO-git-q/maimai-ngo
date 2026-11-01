"""Alongside a walk: switch the game's own autoplay on for each track (from track start) and off again once the result
screen is over, so a test reaches the high-rank result effects. Needs settings.ini testInput = true. AquaMai
UX.DontRuinMyAccount keeps the account's scores untouched for autoplayed tracks.
usage: python ngo_autoplay_during.py <seconds total> [mode=Critical]"""
import json, os, sys, time, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phase_round_lib import screen

SEC = float(sys.argv[1]); MODE = sys.argv[2] if len(sys.argv) > 2 else "Critical"


def autoplay(mode):
    req = urllib.request.Request("http://127.0.0.1:8791/api/test/autoplay", json.dumps({"mode": mode}).encode(), {"Content-Type": "application/json", "X-PartnerLive2D": "1"})
    try:
        with urllib.request.urlopen(req, timeout=5) as r: print(time.strftime("%H:%M:%S"), json.load(r), flush=True)
    except Exception as e: print(time.strftime("%H:%M:%S"), "autoplay", mode, "failed:", e, flush=True)


t = time.time(); on = False; seen_result = False
while time.time() - t < SEC:
    s = screen()
    if s in ("TrackStartProcess", "GameProcess") and not on: autoplay(MODE); on = True; seen_result = False
    if s == "ResultProcess": seen_result = True
    if on and seen_result and s != "ResultProcess": autoplay("None"); on = False
    time.sleep(0.5)
if on: autoplay("None")
