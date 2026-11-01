"""Soak test for the "gets slower the longer you play" report, off screen and muted: play many tracks in one game run
(game autoplay through the mod test API, each track skipped after PLAY seconds with AquaMai OneKeyRetrySkip), log in
again after each credit, and sample the game process (private MB, handles) every 30 s next to the mod's [mem] lines
(frameLog). Results: <out>/soak.csv and the game log copy.
usage: python ngo_soak.py <out dir> [minutes=25]   (env PLAY=25 seconds of play per track; WALK=1,5 walks the whole
managed heap (test/heapwalk) at the song select after those tracks)"""
import csv, json, os, re, shutil, subprocess, sys, threading, time, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phase_round_lib as lib
from phase_round_lib import screen, press, get, say_to, game_pid, DRIVE

OUT = sys.argv[1]; MIN = float(sys.argv[2]) if len(sys.argv) > 2 else 25; PLAY = float(os.environ.get("PLAY", "25"))
WALK = {int(x) for x in os.environ.get("WALK", "").split(",") if x.strip()}
os.makedirs(OUT, exist_ok=True)
say = say_to(os.path.join(OUT, "soak.log"))
SETTINGS = r"D:\maimai\Package\LocalAssets\PartnerLive2D\settings.ini"
LATEST = r"D:\maimai\Package\MelonLoader\Latest.log"


def post(path, body, timeout=5):
    req = urllib.request.Request("http://127.0.0.1:8791/api/" + path, json.dumps(body).encode(), {"Content-Type": "application/json", "X-PartnerLive2D": "1"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r: return json.load(r)
    except Exception as e: return {"error": str(e)}


def sampler(stop):
    with open(os.path.join(OUT, "soak.csv"), "w", newline="") as f:
        w = csv.writer(f); w.writerow(["t_s", "screen", "private_mb", "handles", "working_mb"])
        t0 = time.time()
        while not stop.is_set():
            o = lib.ps("$p = Get-Process -Id %d -ErrorAction SilentlyContinue; if ($p) { '{0} {1} {2}' -f $p.PrivateMemorySize64, $p.HandleCount, $p.WorkingSet64 }" % game_pid())
            if o:
                pm, h, ws = o.split()
                w.writerow([int(time.time() - t0), screen(), int(pm) // 1048576, h, int(ws) // 1048576]); f.flush()
            stop.wait(30)


ini = open(SETTINGS, encoding="utf-8").read()
cfg = re.sub(r"(?m)^(testInput|frameLog)\s*=.*\n?", "", ini).rstrip("\n") + "\ntestInput = true\nframeLog = true\n"
open(SETTINGS, "w", encoding="utf-8").write(cfg)
subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
stop = threading.Event(); th = None; al = None; tracks = 0
try:
    t_end = time.time() + MIN * 60
    while screen() != "AdvertiseProcess" and time.time() < t_end: time.sleep(2)
    th = threading.Thread(target=sampler, args=(stop,), daemon=True); th.start()
    while time.time() < t_end:
        s = screen()
        if s == "AdvertiseProcess":                      # a new credit: log in again
            if al is None or al.poll() is not None:
                al = subprocess.Popen([sys.executable, r"D:\maimai\auto-login.py", str(game_pid())]); say("login")
            time.sleep(5); continue
        if s == "GameProcess":
            post("test/autoplay", {"mode": "Critical"})
            time.sleep(PLAY); tracks += 1; say("track", tracks, "skip")
            press("Service", 2500); time.sleep(3); continue
        if s in ("EntryProcess", "FadeProcess", "PleaseWaitProcess", "") or s.startswith("TrackStart"):
            time.sleep(2); continue
        if s == "MusicSelectProcess" and tracks in WALK:
            WALK.discard(tracks); time.sleep(4); r = post("test/heapwalk", {}, timeout=900); say("heapwalk after", tracks, json.dumps(r)[:600])
        if s == "ContinueProcess": press("X"); time.sleep(3); continue    # no continue: back to the attract loop
        press("C"); time.sleep(2.5)                                       # every other screen: confirm / next
    say("done:", tracks, "tracks")
finally:
    stop.set()
    if al and al.poll() is None: al.kill()
    post("test/autoplay", {"mode": "None"})
    open(SETTINGS, "w", encoding="utf-8").write(ini)
    shutil.copy(LATEST, os.path.join(OUT, "game.log"))
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
