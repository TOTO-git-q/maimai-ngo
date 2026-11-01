"""Off-screen, muted check of the NGO pair sprite backend: log in with the owner's card, watch the music select
(Idle + Random), tap the partner through the API, walk to the Menu phase (Confirm), press START (Enter clip and the
held ReleaseWait), skip the chart, watch the result (tier Loop) and tap it (Reaction), then log out.
The partner render target (views/<id>/frame.png) and the full screen are saved to <out_dir>; times go to sprite.log.
settings.ini selectPartner is set to ngo_pair for the run and restored afterwards.
usage: python tools/ngo_sprite_test.py <out_dir>"""
import ctypes, ctypes.wintypes as wt, json, os, sys, time, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phase_round_lib as lib
from phase_round_lib import screen, phase, press, get, say_to

OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
say = say_to(os.path.join(OUT, "sprite.log"))
SETTINGS = r"D:\maimai\Package\LocalAssets\PartnerLive2D\settings.ini"
u32 = ctypes.windll.user32

def post(path, body=None):
    req = urllib.request.Request(lib.API + path, data=json.dumps(body or {}).encode(), method="POST",
                                 headers={"X-PartnerLive2D": "1", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=10) as r: return json.loads(r.read() or b"null")
    except Exception as e: return {"error": str(e)}

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

def view(context):
    for v in (get("views") or {}).get("views", []):
        if v.get("context") == context: return v
    return None

n = [0]
def grab(tag, vid=None, full=False):
    n[0] += 1
    if vid is not None:
        png = get("views/%d/frame.png" % vid, raw=True)
        if png: open(os.path.join(OUT, "%03d_%s_partner.png" % (n[0], tag)), "wb").write(png)
    if full:
        png = get("screenshot.png", raw=True)
        if png: open(os.path.join(OUT, "%03d_%s_screen.png" % (n[0], tag)), "wb").write(png)

def info(vid):
    d = get("views/%d" % vid) or {}
    return {k: d.get(k) for k in ("state", "loadedTextures", "totalTextures", "estimatedVramBytes", "motionGroups", "width", "height") if k in d} or d

def drive(*a):
    import subprocess
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", lib.DRIVE, *a], check=True)

def wait_for(names, timeout):
    t = time.time()
    while time.time() - t < timeout:
        if screen() in names: return True
        time.sleep(0.5)
    return False

orig = open(SETTINGS, "rb").read()
h = None
try:
    txt = orig.decode("utf-8-sig")
    open(SETTINGS, "w", encoding="utf-8").write(txt.replace("selectPartner = equipped", "selectPartner = ngo_pair"))
    drive("start")
    pid = lib.game_pid(); t0 = time.time()
    while time.time() - t0 < 60 and not h: h = game_hwnd(pid); time.sleep(0.05)
    if h: park(h)
    say("game", pid, "hwnd", h)
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
        if s not in ("AdvertiseProcess", "FadeProcess", ""): press("C")   # EntryProcess needs C (yes) after the scan
        time.sleep(2.5)
    say("music select", phase())
    t = time.time(); v = None
    while not v and time.time() - t < 30: v = view("musicSelect"); time.sleep(0.5)
    if not v: raise SystemExit("no music select partner view: %s" % get("views"))
    vid = v["id"]; say("select view", json.dumps(v, ensure_ascii=False)[:300])
    # Idle + Random: 25 s, partner frame every 0.5 s, full screen every 5 s
    t = time.time()
    while time.time() - t < 25:
        k = int((time.time() - t) / 0.5)
        grab("select%02d" % k, vid, full=(k % 10 == 0)); time.sleep(0.35)
        if k % 6 == 0: say("info", json.dumps(info(vid), ensure_ascii=False)[:400])
    say("tap via API", post("views/%d/tap" % vid))
    for k in range(10): grab("tap%02d" % k, vid); time.sleep(0.3)
    # walk Genre -> Music -> Difficulty -> Menu
    for i in range(6):
        p = phase(); say("phase", p)
        if p == "Menu": break
        press("C"); time.sleep(1.5)
    say("menu reached:", phase())
    for k in range(12): grab("confirm%02d" % k, vid, full=(k == 4)); time.sleep(0.25)
    say("info", json.dumps(info(vid), ensure_ascii=False)[:400])
    # START: Enter clip while ReleaseWait is held
    t_start = time.time(); press("C", 150); say("START pressed")
    k = 0
    while time.time() - t_start < 7:
        s = screen(); grab("enter%02d_%s" % (k, s), vid, full=(k % 4 == 0)); k += 1
        if s not in ("MusicSelectProcess", ""): say("left music select after %.2f s ->" % (time.time() - t_start), s); break
        time.sleep(0.15)
    if not wait_for(("GameProcess",), 90): say("no GameProcess:", screen())
    else:
        time.sleep(4); press("Service", 2500); say("skip")
    if wait_for(("ResultProcess",), 60):
        time.sleep(5); rv = None; t = time.time()
        while not rv and time.time() - t < 10: rv = view("result"); time.sleep(0.5)
        say("result view", json.dumps(rv, ensure_ascii=False)[:300] if rv else None)
        grab("result_full", None, full=True)
        if rv:
            for k in range(8): grab("result%02d" % k, rv["id"]); time.sleep(0.5)
            say("result info", json.dumps(info(rv["id"]), ensure_ascii=False)[:400])
            say("result tap", post("views/%d/tap" % rv["id"]))
            for k in range(12): grab("rtap%02d" % k, rv["id"], full=(k == 4)); time.sleep(0.3)
        time.sleep(2); press("C")
    wait_for(("MusicSelectProcess",), 60); time.sleep(4); grab("select_after", None, full=True)
    press("Service", 2500); say("logout")
    t = time.time()
    while time.time() - t < 150:
        s = screen()
        if s == "AdvertiseProcess": break
        if s == "ContinueProcess": press("X")
        elif s not in ("FadeProcess", "UserDataULProcess", "GameOverProcess", ""): press("C")
        time.sleep(2)
    say("end screen", screen())
finally:
    try: drive("stop")
    except Exception as e: say("stop failed", e)
    open(SETTINGS, "wb").write(orig); say("settings.ini restored")
