"""Muted test run (game in front): log in, capture every music-select phase, start a chart, skip it, and capture
the Result -> music select return frame by frame.
usage: python tools/phase_round.py <out_dir>
drive.ps1 start/stop mute the PC, set a windowed test config (incl. login -> music select) and restore everything."""
import ctypes, json, os, subprocess, sys, time, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vkeys import VirtualKeys

OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
DRIVE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "drive.ps1")
API = "http://127.0.0.1:8791/api/"
K = {"scan": 0x0D, "C": 0x43, "X": 0x58, "D": 0x44, "Service": 0x71}
LOG = open(os.path.join(OUT, "run.log"), "a", encoding="utf-8")
u32 = ctypes.windll.user32; k32 = ctypes.windll.kernel32

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

def game_pid():
    return json.load(open(os.path.join(os.path.dirname(DRIVE), "drive-state.json")))["gamePid"]

def front():
    h = int(ps("(Get-Process -Id %d).MainWindowHandle.ToInt64()" % game_pid()) or 0)
    fg = u32.GetForegroundWindow()
    me = k32.GetCurrentThreadId(); other = u32.GetWindowThreadProcessId(fg, None) if fg else 0
    if other and other != me: u32.AttachThreadInput(me, other, True)
    u32.SetForegroundWindow(h)
    if other and other != me: u32.AttachThreadInput(me, other, False)
    return h

def press(key, ms=180):
    ap = int(ps("(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'amdaemon.exe' } | Select-Object -First 1).ProcessId"))
    vk = VirtualKeys(ap, front())
    try:
        vk.press(K[key], ms); time.sleep(0.8)   # let the io board see the release before restoring
    finally:
        vk.restore()

def wait_screen(names, timeout):
    t = time.time()
    while time.time() - t < timeout:
        if screen() in names: return True
        time.sleep(1)
    return False

subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
try:
    if not wait_screen(("AdvertiseProcess",), 240): raise RuntimeError("no attract screen")
    time.sleep(2)
    for i in range(3):
        press("scan", 3000); time.sleep(4)
        if screen() == "EntryProcess": break
    say("entry", screen()); time.sleep(2); press("C")
    t0 = time.time()
    while not screen().startswith("MusicSelect") and time.time() - t0 < 180:
        say("press C on", screen()); press("C"); time.sleep(2.5)
    for n in range(20): shot("first_%02d" % n)
    time.sleep(3)
    say("phase", phase()); shot("p1_" + str(phase()))
    for step in range(3):                       # Genre -> Music -> Difficulty -> Menu
        b = phase(); press("C"); time.sleep(2.5); say("C:", b, "->", phase()); shot("p%d_%s" % (step + 2, phase()))
    press("D"); time.sleep(2); press("C"); time.sleep(2.5); say("menu D,C ->", phase()); shot("p5_%s" % phase())
    # Option at several carousel positions, then back to the menu
    for i, key in enumerate(("D", "D")):
        press(key); time.sleep(1.5); shot("p5_option_%d" % i)
    for i in range(4):
        if phase() == "Menu": break
        press("X"); time.sleep(2.5)
    say("back at", phase()); shot("p6_back_%s" % phase())
    # menu cursor back to track start, then start the chart
    for i in range(6):
        if screen() == "GameProcess": break
        press("C"); time.sleep(3); say("start C ->", screen(), phase())
    if not wait_screen(("GameProcess",), 180):
        say("chart did not start; screen", screen(), "phase", phase()); shot("stuck")
    else:
        time.sleep(6); shot("game"); press("Service", 2500); say("skip ->", screen())
        t0 = time.time()
        while screen() != "MusicSelectProcess" and time.time() - t0 < 120:
            if screen() in ("ResultProcess", "NextTrackProcess"): press("C")
            time.sleep(1.5)
        # return burst: as fast as the API allows
        for n in range(30): shot("return_%02d" % n)
        say("returned:", screen(), phase())
finally:
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
    say("done")
