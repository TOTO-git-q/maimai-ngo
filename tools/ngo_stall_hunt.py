"""Off-screen, muted hunt for the ~0.4 s in-song freezes: play whole songs with the game's autoplay (no skip, so a
song runs to its natural end) for <minutes>, logging in again after every credit, with only testInput on. NOT frameLog:
its per-minute object census makes hitches of its own. The mod's PlayStats writes "[stall] <ms> at <s> into music <id>
(<wall clock>)" for every frame over 100 ms; tools\\ngo_proc_sampler.ps1 (started here too) records which processes were
busy each second, so the two can be lined up afterwards. Copies the game log to <out>\\game.log.
usage: python tools/ngo_stall_hunt.py <out dir> [minutes=18]"""
import os, re, shutil, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phase_round_lib as lib
from phase_round_lib import screen, press, get, say_to, game_pid, DRIVE
import json, urllib.request

OUT = sys.argv[1]; MIN = float(sys.argv[2]) if len(sys.argv) > 2 else 18
os.makedirs(OUT, exist_ok=True)
say = say_to(os.path.join(OUT, "hunt.log"))
SETTINGS = r"D:\maimai\Package\LocalAssets\PartnerLive2D\settings.ini"
LATEST = r"D:\maimai\Package\MelonLoader\Latest.log"
HERE = os.path.dirname(os.path.abspath(__file__))


def post(path, body, timeout=5):
    req = urllib.request.Request(lib.API + path, json.dumps(body).encode(), {"Content-Type": "application/json", "X-PartnerLive2D": "1"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r: return json.load(r)
    except Exception as e: return {"error": str(e)}


ini = open(SETTINGS, encoding="utf-8").read()
cfg = re.sub(r"(?m)^(testInput|frameLog)\s*=.*\n?", "", ini).rstrip("\n") + "\ntestInput = true\n"
open(SETTINGS, "w", encoding="utf-8").write(cfg)
sampler = subprocess.Popen(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", os.path.join(HERE, "ngo_proc_sampler.ps1"),
                            os.path.join(OUT, "procs.csv"), str(MIN + 4)])
subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
al = None; songs = 0
try:
    t_end = time.time() + MIN * 60
    while screen() != "AdvertiseProcess" and time.time() < t_end: time.sleep(2)
    while time.time() < t_end:
        s = screen()
        if s == "AdvertiseProcess":                      # a new credit: log in again (auto-login presses yes only on ConfirmPlay)
            if al is None or al.poll() is not None:
                al = subprocess.Popen([sys.executable, r"D:\maimai\auto-login.py", str(game_pid())]); say("login")
            time.sleep(5); continue
        if s == "GameProcess":
            post("test/autoplay", {"mode": "Critical"}); songs += 1; say("song", songs)
            while screen() == "GameProcess" and time.time() < t_end + 300: time.sleep(2)   # let it end by itself
            continue
        if s in ("EntryProcess", "FadeProcess", "PleaseWaitProcess", "") or s.startswith("TrackStart"):
            time.sleep(2); continue
        if s == "ContinueProcess": press("X"); time.sleep(3); continue
        press("C"); time.sleep(2.5)
    say("done:", songs, "songs")
finally:
    if al and al.poll() is None: al.kill()
    post("test/autoplay", {"mode": "None"})
    open(SETTINGS, "w", encoding="utf-8").write(ini)
    try: shutil.copy(LATEST, os.path.join(OUT, "game.log"))
    except OSError as e: say("log copy failed", e)
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
    try: sampler.terminate()
    except Exception: pass
