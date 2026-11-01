"""Owner-watched demo on the real cabinet (not the muted off-screen test): start the game with the normal launcher,
wait for the attract screen, log in with the owner's card, linger on every screen, walk to the demo song (env
DEMO_MUSIC, default 11831 きゅびずむ by 超てんちゃん; located through the mod's /api/test/music), skip it (AquaMai
OneKeyRetrySkip: Service held), go through the result, log out (OneKeyEntryEnd: Service held, which also uploads the
account) and stop on the attract screen. The game is left running. A game that is already running is taken over
from its current screen instead of started.
usage: python tools/ngo_demo.py <log dir>"""
import os, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phase_round_lib as lib
from phase_round_lib import screen, phase, press, say_to, get

OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
say = say_to(os.path.join(OUT, "demo.log"))
LINGER = float(os.environ.get("LINGER", "4"))

def sinmai_pid():
    out = lib.ps("(Get-Process Sinmai -ErrorAction SilentlyContinue | Select-Object -First 1).Id")
    return int(out) if out else 0
lib.game_pid = sinmai_pid   # press() looks the window up through this

def shot(tag):
    png = get("screenshot.png", raw=True)
    if png: open(os.path.join(OUT, "%s_%s.png" % (time.strftime("%H%M%S"), tag)), "wb").write(png)

def wait_for(names, timeout):
    t = time.time()
    while time.time() - t < timeout:
        if screen() in names: return True
        time.sleep(1)
    return False

if sinmai_pid():   # started by the owner or the normal launcher already: take it over where it is
    say("game already running: attaching at", screen())
else:
    subprocess.Popen(["explorer.exe", r"D:\maimai\Play-maimai.bat"])
    say("launcher started")
    t = time.time()
    while not get("test/processes") and time.time() - t < 240: time.sleep(2)
    if not wait_for(("AdvertiseProcess",), 300): raise SystemExit("no attract screen")
    say("attract"); time.sleep(8); shot("attract")

# the launcher itself runs auto-login.py (card login as soon as the attract screen is up)
try:
    t = time.time(); last = None; since = time.time(); picked = False
    while time.time() - t < 420:
        s = screen()
        if s != last:
            say("screen", s); last = s; since = time.time()
            time.sleep(1.5); shot(s)
        if s == "MusicSelectProcess": break        # not MusicSelectInfoProcess (the freedom-mode rules card before it)
        if s == "ModeSelectProcess" and not picked:   # normal mode never starts a track here: choose FREEDOM
            time.sleep(LINGER); picked = True
            for k in range(10):
                ui = get("test/ui") or {}
                icons = [i for i in ui.get("items", []) if "ModeIcon" in i.get("texture", "") and "w" in i]
                big = max(icons, key=lambda i: abs(i["w"] * i["h"]), default=None)
                say("mode", big and big["texture"])
                if big and "Freedom" in big["texture"]: break
                press("D"); time.sleep(1.2)
            press("C"); since = time.time(); time.sleep(1); continue
        # linger so the screen can be seen, then confirm (attract/entry are handled by the auto-login helper)
        if s not in ("AdvertiseProcess", "EntryProcess", "FadeProcess", "") and time.time() - since > LINGER:
            press("C"); since = time.time()
        time.sleep(1)
finally:
    pass

# music select: walk to the demo song (default きゅびずむ by 超てんちゃん, the owner wants a KAngel song), then
# genre -> music -> difficulty -> start, lingering on each phase. Right/left are buttons 3/6 (D/Z).
TARGET = int(os.environ.get("DEMO_MUSIC", "11831"))
RIGHT, LEFT = 0x44, 0x5A

def probe():
    return get("test/music?find=%d" % TARGET) or {}

def walk(now_key, want_key, size_key, label, limit=160):
    """press right/left the short way round until probe()[now_key] == want; want is fixed at the first probe"""
    pr = probe(); want = pr.get(want_key)
    if want is None: say("demo song %d not found (%s)" % (TARGET, label)); return False
    ap = int(lib.ps("(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'amdaemon.exe' } | Select-Object -First 1).ProcessId"))
    vk = lib.VirtualKeys(ap, int(lib.ps("(Get-Process -Id %d).MainWindowHandle.ToInt64()" % sinmai_pid()) or 0))
    try:
        for step in range(limit):
            now, n = pr.get(now_key), max(1, pr.get(size_key) or 1)
            if now == want: say(label, "reached", want, pr.get("categoryName")); return True
            fwd = (want - now) % n
            vk.press(RIGHT if fwd <= n - fwd else LEFT, 120); time.sleep(0.35)
            pr = probe()
        say(label, "gave up at", pr.get(now_key), "want", want); return False
    finally:
        time.sleep(0.8); vk.restore()

time.sleep(LINGER)
t = time.time()
while phase() is None and time.time() - t < 20: time.sleep(0.5)   # the select session reports its phase a moment after the screen opens
if phase() == "Genre":
    walk("category", "findCategory", "categories", "genre"); time.sleep(1.5); shot("select_Genre_target")
    press("C"); time.sleep(2.5)
if phase() == "Music":
    walk("index", "findIndex", "count", "song"); time.sleep(LINGER)
for i in range(8):
    p, s = phase(), screen()
    say("select", s, p); shot("select_%s" % p)
    if s in ("TrackStartProcess", "GameProcess") or (s or "").startswith("TrackStart"): break
    press("C"); time.sleep(LINGER)
if not wait_for(("GameProcess",), 90): say("track did not start:", screen(), phase())
else:
    say("playing"); time.sleep(8); shot("game")
    press("Service", 2500); say("skip")                  # OneKeyRetrySkip: skip the track
if wait_for(("ResultProcess",), 60):
    time.sleep(6); shot("result"); time.sleep(LINGER); press("C")
wait_for(("MusicSelectProcess",), 60); time.sleep(LINGER); shot("select_after")
press("Service", 2500); say("logout")                    # OneKeyEntryEnd: end the credit, upload, log out
t = time.time(); last = None; since = time.time()
while time.time() - t < 180:
    s = screen()
    if s != last: say("screen", s); last = s; since = time.time(); time.sleep(1.5); shot(s)
    if s == "AdvertiseProcess": break
    if s == "ContinueProcess" and time.time() - since > LINGER: press("X"); since = time.time()   # NO
    elif s not in ("FadeProcess", "UserDataULProcess", "GameOverProcess") and time.time() - since > LINGER: press("C"); since = time.time()
    time.sleep(1)
say("done; game left running on the attract screen")
