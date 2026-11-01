"""Off-screen, muted: log in with the owner's card, play the credit's tracks (each started and skipped) until the
game reaches the end-of-credit collection screen (CollectionProcess), grab it and leave the game running there for
hand-driven checks (tools/ngo_key.py). Stop it afterwards with drive.ps1 stop / test_game_stop.
usage: python tools/ngo_to_collection.py <out>"""
import ctypes, ctypes.wintypes as wt, os, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phase_round_lib as lib
from phase_round_lib import screen, phase, press, get, say_to

OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
say = say_to(os.path.join(OUT, "collection.log"))
u32 = ctypes.windll.user32


def game_hwnd(pid):
    found = []
    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, _):
        p = wt.DWORD(); u32.GetWindowThreadProcessId(h, ctypes.byref(p))
        if p.value == pid and u32.IsWindowVisible(h): found.append(h)
        return True
    u32.EnumWindows(cb, 0)
    return found[0] if found else None


def park(h):
    r = wt.RECT(); u32.GetWindowRect(h, ctypes.byref(r))
    if r.left > -3000: u32.SetWindowPos(h, 0, -4200, 40, 0, 0, 0x0001 | 0x0004 | 0x0010)


def wait_for(names, timeout):
    t = time.time()
    while time.time() - t < timeout:
        if screen() in names: return True
        time.sleep(0.5)
    return False


def grab(tag):
    png = get("screenshot.png", raw=True)
    if png: open(os.path.join(OUT, tag + ".png"), "wb").write(png)


subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", lib.DRIVE, "start"], check=True)
pid = lib.game_pid(); t0 = time.time(); h = None
while time.time() - t0 < 60 and not h: h = game_hwnd(pid); time.sleep(0.05)
if h: park(h)
while not get("status"):
    if h: park(h)
    if time.time() - t0 > 180: raise SystemExit("mod API never came up")
    time.sleep(1)
if not wait_for(("AdvertiseProcess",), 240): raise SystemExit("no attract screen")
time.sleep(6); say("scan card"); press("scan", 3000)
t = time.time()
while screen() != "MusicSelectProcess" and time.time() - t < 240:
    if h: park(h)
    s = screen()
    if s not in ("AdvertiseProcess", "FadeProcess", ""): press("C")
    time.sleep(2.5)
track = 0
while track < 6:
    s = screen()
    if s == "CollectionProcess": break
    if s == "MusicSelectProcess":
        track += 1; say("track", track)
        for i in range(6):
            if phase() == "Menu": break
            press("C"); time.sleep(1.5)
        press("C", 150)
        if wait_for(("GameProcess",), 120): time.sleep(4); press("Service", 2500); say("skip")
        if wait_for(("ResultProcess",), 60): time.sleep(6); press("C")
        t = time.time()
        while time.time() - t < 90:
            s = screen()
            if s in ("MusicSelectProcess", "CollectionProcess"): break
            if s not in ("FadeProcess", "NextTrackProcess", "MusicSelectInfoProcess", "GameProcess", ""): press("C")
            time.sleep(2.5)
        say("after track", track, "->", screen())
    else:
        press("C"); time.sleep(2.5)
time.sleep(4)
say("stopped at", screen()); grab("collection_0")
