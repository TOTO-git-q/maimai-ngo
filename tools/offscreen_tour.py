"""Play one full credit with the game window off screen and record every screen.
usage: python tools/offscreen_tour.py <out_dir>
Needs the mod built with TestHarness (process log). Starts the muted test game via drive.ps1, moves its
window off screen and presses keys through vkeys.py (a virtual keyboard patched into amdaemon.exe, so the
game never needs focus and the real keyboard never reaches it). Logs in with the user's own Aime (hold
Return), plays through (each chart is skipped with a long Service press, AquaMai OneKeyRetrySkip), and
stops when the session returns to the attract screen. Screenshots go to <out_dir> named by process.
AquaMai.toml, mute and the game process are restored at the end."""
import ctypes, ctypes.wintypes as wt, json, os, subprocess, sys, time, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vkeys import VirtualKeys

PKG = r"D:\maimai\Package"
DRIVE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "drive.ps1")
SETTINGS = os.path.join(PKG, r"LocalAssets\PartnerLive2D\settings.ini")
API = "http://127.0.0.1:8791/api/"
VK = {"Return": 0x0D, "C": 0x43, "X": 0x58, "Service": 0x71}
user32 = ctypes.windll.user32
out = sys.argv[1]
os.makedirs(out, exist_ok=True)
log = open(os.path.join(out, "tour.log"), "a", encoding="utf-8")
vk = None

def say(*a):
    s = time.strftime("%H:%M:%S ") + " ".join(str(x) for x in a)
    print(s, flush=True); log.write(s + "\n"); log.flush()

def get(path, raw=False):
    with urllib.request.urlopen(API + path, timeout=15) as r:
        b = r.read()
    return b if raw else json.loads(b)

def key(name, ms=150):
    vk.press(VK[name], ms)
    time.sleep(0.25)

def amdaemon_pid():
    q = ("(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'amdaemon.exe' -and "
         "$_.ExecutablePath -like '" + PKG + "\\*' } | Select-Object -First 1).ProcessId")
    r = subprocess.run(["powershell", "-NoProfile", "-Command", q], capture_output=True, text=True)
    return int(r.stdout.strip()) if r.stdout.strip() else None

def drive(*a):
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, *a], check=True)

def game_hwnd(pid):
    found = []
    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, _):
        p = wt.DWORD(); user32.GetWindowThreadProcessId(h, ctypes.byref(p))
        if p.value == pid and user32.IsWindowVisible(h): found.append(h)
        return True
    user32.EnumWindows(cb, 0)
    return found[0] if found else None

def park(h):
    r = wt.RECT(); user32.GetWindowRect(h, ctypes.byref(r))
    if r.left > -3000:
        user32.SetWindowPos(h, 0, -4200, 40, 0, 0, 0x0001 | 0x0004 | 0x0010)  # NOSIZE|NOZORDER|NOACTIVATE

# ---- setup -------------------------------------------------------------------------------
user32.GetForegroundWindow.restype = wt.HWND
user_fg = user32.GetForegroundWindow()
shots, last_proc_i, procs, active = 0, 0, [], []
try:
    drive("start")
    pid = json.load(open(os.path.join(os.path.dirname(DRIVE), "drive-state.json")))["gamePid"]
    t0 = time.time(); h = None
    while time.time() - t0 < 60 and not h:
        h = game_hwnd(pid); time.sleep(0.05)
    if h:
        park(h)
        # the new game window may have taken focus at launch; hand it back so stray real keys (Esc =
        # AquaMai InstantQuit) cannot reach the game
        user32.GetForegroundWindow.restype = wt.HWND
        if user32.GetForegroundWindow() == h and user_fg:
            me = ctypes.windll.kernel32.GetCurrentThreadId(); other = user32.GetWindowThreadProcessId(h, None)
            user32.AttachThreadInput(me, other, True); user32.SetForegroundWindow(user_fg); user32.AttachThreadInput(me, other, False)
    say("game pid", pid, "hwnd", h)
    ap = None
    while time.time() - t0 < 90 and not ap:
        ap = amdaemon_pid(); time.sleep(1)
    time.sleep(3)
    vk = VirtualKeys(ap, h)
    say("virtual keys in amdaemon", ap, ":", ", ".join(vk.patched))
    while True:  # wait for the mod's API
        try: get("status"); break
        except Exception: time.sleep(1)
        if time.time() - t0 > 180: raise RuntimeError("mod API never came up")

    def poll():
        global last_proc_i
        d = get("test/processes?since=%d" % last_proc_i)
        for p in d["processes"]:
            procs.append(p); last_proc_i = p["i"] + 1
            say("proc +", p["name"], "@%.1fs" % p["t"])

    def shot(tag):
        global shots
        try:
            png = get("screenshot.png", raw=True)
        except Exception as e:
            say("shot failed", e); return
        recent = procs[-1]["name"].split(".")[-1] if procs else "boot"
        name = "%03d_%s_%s.png" % (shots, recent, tag)
        open(os.path.join(out, name), "wb").write(png); shots += 1


    state = "boot"; logged_in = False; session_done = False; last_action = time.time(); t_start = time.time()
    while time.time() - t_start < 40 * 60:
        if h: park(h)
        poll()
        shot(state)
        names = [p["name"].split(".")[-1] for p in procs]
        cur = names[-1] if names else ""
        now = time.time()
        if not logged_in:
            if "AdvertiseProcess" in names and now - last_action > 8:
                say("scan card"); key("Return", 3000); last_action = time.time(); state = "login"
            if "EntryProcess" in names: logged_in = True; state = "entry"
        elif cur == "GameProcess":
            session_done = True
            if now - last_action > 10:
                say("skip chart"); key("Service", 2500); last_action = time.time()
        elif cur == "AdvertiseProcess":
            # never press anything here: a button on the attract screen starts a guest entry
            say("back to attract: session finished" if session_done else "back to attract before any chart (timeout?)")
            shot("end"); break
        else:
            if now - last_action > 2.5:
                # C confirms / OK on every screen; X declines the icon photo
                k = "X" if cur == "PhotoAgreeProcess" else "C"
                say("press", k, "on", cur); key(k); last_action = time.time()
                time.sleep(0.6); shot("after" + k)
        state = cur or state
        time.sleep(1)
finally:
    try: drive("stop")
    except Exception as e: say("stop failed", e)
    json.dump(procs, open(os.path.join(out, "processes.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    say("done,", shots, "screenshots")
