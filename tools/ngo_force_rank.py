"""Test helper: make the result screen play the clear-rank animation of a given rank (needs settings.ini testInput =
true, e.g. the walk with TESTINPUT=1). The score, record and upload are untouched; only the animation changes.
Waits for the game's API, sets the rank, and clears it again after the given seconds.
usage: python ngo_force_rank.py <rank, e.g. Rank_SSSP> <seconds>"""
import json, sys, time, urllib.request

RANK, SEC = sys.argv[1], float(sys.argv[2])


def post(rank):
    req = urllib.request.Request("http://127.0.0.1:8791/api/test/rank", json.dumps({"rank": rank}).encode(),
                                 {"Content-Type": "application/json", "X-PartnerLive2D": "1"})
    with urllib.request.urlopen(req, timeout=5) as r: return json.load(r)


t = time.time()
while time.time() - t < 240:
    try: print(time.strftime("%H:%M:%S"), post(RANK), flush=True); break
    except Exception: time.sleep(3)
time.sleep(SEC)
try: print(time.strftime("%H:%M:%S"), post("None"), flush=True)
except Exception as e: print("clear failed:", e)
