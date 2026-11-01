"""Drive the muted test game from the title screen to the normal Music song list (logs in with the user's card).
usage: python tools/goto_select.py      (game must be started with test_game_start)
Each step: send a key via drive.ps1, wait for the screen to change (in-game screenshot differs), repeat.
Stops when /api/music-select reports phase Music."""
import subprocess, time, json, urllib.request, hashlib, sys
DRIVE = r"E:\claude workspace\maimai-partner-live2d\tools\drive.ps1"
def drive(*a): subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, *a], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
def shot():
    try: return hashlib.md5(urllib.request.urlopen("http://127.0.0.1:8791/api/screenshot.png", timeout=5).read()).hexdigest()
    except Exception: return None
def phase():
    try:
        d = json.load(urllib.request.urlopen("http://127.0.0.1:8791/api/music-select", timeout=5))
        return [s["phase"] for x in d.get("sessions", []) for s in x["sides"]]
    except Exception: return []
def log_has(t):
    try: return t in open(r"D:\maimai\Package\MelonLoader\Latest.log", encoding="utf-8", errors="ignore").read()[-20000:]
    except Exception: return False
t0 = time.time()
while not log_has("NewTouchPanel Start 1P") and time.time() - t0 < 180: time.sleep(2)
time.sleep(3)
# login with the user's own Aime card (segatools scan key = Return, DEVICEime.txt): hold Return, then
# C = yes / promo OK / decide, X = "no" to the icon photo, then C through area/ticket/news pages until Genre.
plan = ["HOLD:Return:3000", "C", "C", "C", "X"] + ["C"] * 16
for step in plan:
    p = phase()
    if "Music" in p: break
    if "Genre" in p: drive("key", "C"); time.sleep(4); continue
    if step.startswith("HOLD"):
        _, k, ms = step.split(":"); drive("key", k, ms)
    else:
        drive("key", step)
    time.sleep(6)
for _ in range(6):
    p = phase()
    if "Music" in p: break
    if "Genre" in p: drive("key", "C")
    time.sleep(4)
print("phase:", phase())
