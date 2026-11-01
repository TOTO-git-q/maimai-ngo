"""Off-screen, muted size/position check of the select-page partner: log in with the owner's card, wait in the Music
list until the partner's sprites are loaded, apply each candidate partner rect live (POST music-select/layout), grab
the screen for each, put the original rect back, log out and stop. Nothing is saved to the preset.
Writes <out>/cand_<label>.png and <out>/sheet.png (lower screen of every candidate side by side).
usage: python tools/ngo_select_tune.py <out> <candidates.json | json text> [result_candidates.json]
       (a list of {"label","height","bottomY"[,"bottomX"]}; pass a file from PowerShell, which strips the quotes)
       result_candidates: a list of {"label", <partner.ini key>: value, ...}; with it one chart is started and skipped,
       the result hierarchy is dumped (tree_near.json / tree_far.json) and each candidate is applied live and grabbed
       (result_sheet.png); the partner's values are put back afterwards and nothing is saved."""
import ctypes, ctypes.wintypes as wt, json, os, subprocess, sys, time, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phase_round_lib as lib
from phase_round_lib import screen, phase, press, get, say_to

OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
CANDS = json.load(open(sys.argv[2], encoding="utf-8")) if os.path.isfile(sys.argv[2]) else json.loads(sys.argv[2])
say = say_to(os.path.join(OUT, "tune.log"))
LATEST = r"D:\maimai\Package\MelonLoader\Latest.log"
u32 = ctypes.windll.user32


def post(path, body=None):
    req = urllib.request.Request(lib.API + path, data=json.dumps(body or {}).encode(), method="POST",
                                 headers={"X-PartnerLive2D": "1", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=10) as r: return json.loads(r.read() or b"null") or {}
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


def drive(*a):
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", lib.DRIVE, *a], check=True)


def wait_for(names, timeout):
    t = time.time()
    while time.time() - t < timeout:
        if screen() in names: return True
        time.sleep(0.5)
    return False


def sheet(files, labels, out):
    from PIL import Image, ImageDraw, ImageFont
    crops = [Image.open(f).convert("RGB") for f in files]
    w, h = crops[0].size
    top = int(h * 0.42)   # the lower (circle) screen of the 9:16 window
    crops = [c.crop((0, top, w, h)) for c in crops]
    pad, head = 12, 46
    cw, ch = crops[0].size
    img = Image.new("RGB", (len(crops) * (cw + pad) + pad, ch + head + pad), "white")
    try: font = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 22)
    except OSError: font = ImageFont.load_default()
    d = ImageDraw.Draw(img)
    for i, (c, label) in enumerate(zip(crops, labels)):
        x = pad + i * (cw + pad)
        img.paste(c, (x, head))
        d.text((x + 4, 10), label, fill="black", font=font)
    img.save(out)


try:
    drive("start")
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
        if s not in ("AdvertiseProcess", "FadeProcess", ""): press("C")   # EntryProcess needs C (yes) after the scan
        time.sleep(2.5)
    acct = [l for l in open(LATEST, encoding="utf-8", errors="replace") if "[account]" in l]
    say("account line:", acct[-1].strip() if acct else None)
    t = time.time(); v = None
    while time.time() - t < 60:
        v = view("musicSelect")
        if v and v.get("loadedTextures") == v.get("totalTextures"): break
        time.sleep(1)
    say("select view", json.dumps(v, ensure_ascii=False)[:200])
    for i in range(2):   # Genre -> Music list
        if phase() == "Music": break
        press("C"); time.sleep(1.5)
    orig = (get("music-select") or {}).get("preset", {}).get("partner")
    say("original rect", orig)
    files, labels = [], []
    for c in CANDS:
        rect = {k: c[k] for k in ("bottomX", "bottomY", "height") if k in c}
        say(c["label"], post("music-select/layout", {"partner": rect}).get("error"))
        time.sleep(1.2)
        png = get("screenshot.png", raw=True)
        f = os.path.join(OUT, "cand_%s.png" % c["label"].split()[0])
        open(f, "wb").write(png); files.append(f); labels.append(c["label"])
    if orig: post("music-select/layout", {"partner": orig})
    sheet(files, labels, os.path.join(OUT, "sheet.png")); say("sheet written")
    if len(sys.argv) > 3:
        # result screen: play one chart (skipped), dump the result hierarchy, then the partner.ini candidates
        rcands = json.load(open(sys.argv[3], encoding="utf-8"))
        for i in range(6):
            if phase() == "Menu": break
            press("C"); time.sleep(1.5)
        press("C", 150); say("START")
        if wait_for(("GameProcess",), 120):
            time.sleep(4); press("Service", 2500); say("skip")
        if wait_for(("ResultProcess",), 60):
            time.sleep(6)
            for up, depth, name in ((3, 8, "tree_near.json"), (8, 4, "tree_far.json")):
                d = get("test/tree?of=PartnerLive2D_ngo_pair&up=%d&depth=%d" % (up, depth))
                json.dump(d, open(os.path.join(OUT, name), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
            before = ((get("partners/ngo_pair") or {}).get("values") or {})
            keep = {k: before.get(k, "") for c in rcands for k in c if k != "label"}
            say("result values before", keep)
            files, labels = [], []
            for c in rcands:
                vals = {k: str(v) for k, v in c.items() if k != "label"}
                say(c["label"], post("partners/ngo_pair/config", {"values": vals}).get("error"))
                time.sleep(1.5)
                f = os.path.join(OUT, "result_%s.png" % c["label"].split()[0])
                open(f, "wb").write(get("screenshot.png", raw=True)); files.append(f); labels.append(c["label"])
            post("partners/ngo_pair/config", {"values": keep})
            sheet(files, labels, os.path.join(OUT, "result_sheet.png")); say("result sheet written")
            time.sleep(1); press("C")
        wait_for(("MusicSelectProcess",), 60); time.sleep(3)
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
