"""Off-screen run that logs in with the user's card, walks music select through its phases and captures each one.
usage: python tools/phase_shots.py <out_dir>
Uses drive.ps1 (muted, windowed, restores settings) and vkeys.py (keys inside amdaemon, no focus needed, restored
after every press). Screenshots come from the mod's /api/screenshot.png. Never presses anything on the attract
screen except the card scan."""
import json, os, subprocess, sys, time, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vkeys import VirtualKeys

OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
DRIVE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "drive.ps1")
API = "http://127.0.0.1:8791/api/"
K = {"scan": 0x0D, "C": 0x43, "X": 0x58, "D": 0x44, "E": 0x45, "Z": 0x5A, "A": 0x41, "Q": 0x51, "W": 0x57}
LOG = open(os.path.join(OUT, "run.log"), "a", encoding="utf-8")

def say(*a):
    s = time.strftime("%H:%M:%S ") + " ".join(map(str, a)); print(s, flush=True); LOG.write(s + "\n"); LOG.flush()

def get(path, raw=False):
    try:
        with urllib.request.urlopen(API + path, timeout=10) as r:
            b = r.read()
        return b if raw else json.loads(b)
    except Exception:
        return None

def screen():
    d = get("test/processes")
    return d["processes"][-1]["name"].split(".")[-1] if d and d["processes"] else ""

def phase():
    d = get("music-select")
    try: return d["sessions"][0]["sides"][0]["phase"]
    except Exception: return None

def shot(name):
    png = get("screenshot.png", raw=True)
    if png: open(os.path.join(OUT, name + ".png"), "wb").write(png)

def ps(cmd):
    return subprocess.run(["powershell", "-NoProfile", "-Command", cmd], capture_output=True, text=True).stdout.strip()

def press(key, ms=180):
    ap = int(ps("(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'amdaemon.exe' } | Select-Object -First 1).ProcessId"))
    gp = json.load(open(os.path.join(os.path.dirname(DRIVE), "drive-state.json")))["gamePid"]
    hwnd = int(ps("(Get-Process -Id %d).MainWindowHandle.ToInt64()" % gp) or 0)
    vk = VirtualKeys(ap, hwnd)
    try:
        vk.press(K[key], ms); time.sleep(0.8)   # let the io board see the release before restoring
    finally:
        vk.restore()

subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
try:
    t0 = time.time()
    while screen() != "AdvertiseProcess":
        if time.time() - t0 > 240: raise RuntimeError("no attract screen")
        time.sleep(2)
    time.sleep(2)
    for i in range(3):
        press("scan", 3000); time.sleep(4)
        if screen() == "EntryProcess": break
    say("entry", screen()); time.sleep(2); press("C")   # yes, play with this Aime

    # pre-select screens: C confirms; photo screen needs E; tutorial select: try the skip tab keys
    burst_done = False; t0 = time.time(); tut = ["Z", "A", "Q", "X"]
    while time.time() - t0 < 600:
        s = screen()
        if s == "MusicSelectProcess" or (s.startswith("MusicSelect") and s != "MusicSelectInfoProcess"):
            break
        if s == "MusicSelectInfoProcess":
            # first-entry burst: capture as fast as the API allows across the switch into music select
            for n in range(25):
                shot("first_%02d" % n)
            burst_done = True
            press("C"); time.sleep(1.5); continue
        key = {"PhotoShootProcess": "E", "PhotoAgreeProcess": "X"}.get(s, "C")
        if s == "TutorialSelectProces":
            key = tut[0]; tut = tut[1:] + tut[:1]
        say("press", key, "on", s); press(key); time.sleep(2.2)
    if not burst_done:
        for n in range(15): shot("first_%02d" % n)
    time.sleep(3)
    say("music select phase", phase()); shot("p1_" + str(phase()))
    # walk: (Genre ->) Music -> Difficulty -> Menu ; then Option via the menu cursor; then back out
    for step in range(3):
        before = phase(); press("C"); time.sleep(2.5)
        say("C:", before, "->", phase()); shot("p%d_%s" % (step + 2, phase()))
    for key in ("D", "C"):   # move menu cursor right, open
        press(key); time.sleep(2.5); say(key, "->", phase()); shot("p_menu_%s_%s" % (key, phase()))
    for key in ("X", "X", "X"):  # back out step by step
        press(key); time.sleep(2.5); say(key, "->", phase()); shot("back_%s" % phase())
finally:
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
    say("done")
