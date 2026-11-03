"""Smoke test of the mod's test recorder (POST test/record): start the muted off-screen test game, record 6 s of the
attract screen, finish the file, stop the game.
usage: python tools/record_smoke.py <out.mp4>"""
import json, os, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phase_round_lib as lib
from phase_round_lib import screen, get, DRIVE

OUT = os.path.abspath(sys.argv[1])
FF = r"%USERPROFILE%\AppData\Roaming\Python\Python312\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe"


def post(path, body):
    import urllib.request
    req = urllib.request.Request(lib.API + path, data=json.dumps(body).encode(), method="POST",
                                 headers={"X-PartnerLive2D": "1", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=15) as r: return json.loads(r.read() or b"null")
    except Exception as e: return {"error": str(e)}


subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
try:
    t = time.time()
    while screen() != "AdvertiseProcess" and time.time() - t < 240: time.sleep(1)
    time.sleep(3)
    if "--free" in sys.argv: print("frame rate", post("test/framerate", {"vsync": 0, "fps": 60}), flush=True)
    print("start", post("test/record", {"path": OUT, "ffmpeg": FF, "width": 720, "fps": 60}), flush=True)
    time.sleep(6)
    print("stop", post("test/record", {"stop": True}), flush=True)
    t = time.time()
    while time.time() - t < 120:
        s = post("test/record", {})
        if s.get("finished"): print("finished", s, flush=True); break
        time.sleep(1)
finally:
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
