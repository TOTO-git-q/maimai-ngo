"""In-game check of the frame-library select page (partner.ini "library"), on the muted test game, without the game
window in front (buttons through vkeys into amdaemon, touches through the mod's test/touch, moments through the view's
motion route), so it also runs on a locked desktop.
Starts the test game, logs in with D:\\maimai\\auto-login.py, walks to the Music phase, then: song changes (button 3),
touches on sensors over the pair, category / master / countdown moments, NEXT (difficulty), NEXT (options = confirm),
START (start: the page waits for the pair). Saves screenshots, the library's own record of every picture shown
(timeline.json, for v2/render.py) and the partner's select notes from the log; stops the game.
usage: python tools/library_select_test.py <out dir> [--keep]   (--keep: leave the game running)"""
import json, os, subprocess, sys, time, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phase_round_lib as lib
from phase_round_lib import screen, press, DRIVE

API = "http://127.0.0.1:8791/api/"
LOG = r"D:\maimai\Package\MelonLoader\Latest.log"
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
notes = open(os.path.join(OUT, "run.log"), "w", encoding="utf-8")


def say(*a):
    line = time.strftime("%H:%M:%S ") + " ".join(str(x) for x in a)
    print(line, flush=True); notes.write(line + "\n"); notes.flush()


def get(path, raw=False):
    try:
        with urllib.request.urlopen(API + path, timeout=10) as r: b = r.read()
        return b if raw else json.loads(b)
    except Exception: return None


def post(path, body):
    req = urllib.request.Request(API + path, json.dumps(body).encode(), {"Content-Type": "application/json", "X-PartnerLive2D": "1"})
    try:
        with urllib.request.urlopen(req, timeout=10) as r: return json.load(r)
    except Exception as e: return {"error": str(e)}


def phase():
    d = get("music-select") or {}
    for x in d.get("sessions", []):
        for s in x.get("sides", []):
            if s.get("phase"): return s["phase"]
    return None


def view():
    for v in (get("views") or {}).get("views", []):
        if v.get("context") == "musicSelect": return v
    return None


def shot(name):
    b = get("screenshot.png", raw=True)
    if b: open(os.path.join(OUT, name + ".png"), "wb").write(b)


def main():
    log0 = os.path.getsize(LOG) if os.path.exists(LOG) else 0
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
    pid = lib.game_pid(); say("game", pid)
    t0 = time.time()
    while not get("test/processes") and time.time() - t0 < 180: time.sleep(2)
    log0 = 0                                                    # a new game writes a new Latest.log
    al = subprocess.Popen([sys.executable, r"D:\maimai\auto-login.py", str(pid)])
    try:
        t0 = time.time()
        while time.time() - t0 < 300:
            s, p = screen(), phase()
            if s == "MusicSelectProcess" and p == "Music": break
            if s in ("AdvertiseProcess", "EntryProcess", "FadeProcess", "PleaseWaitProcess", "") or s.startswith("TrackStart"):
                time.sleep(2); continue
            if s == "ContinueProcess": press("X"); time.sleep(3); continue
            if "Photo" in s: press("X"); time.sleep(4); continue
            press("C"); time.sleep(3)
        say("at", screen(), phase())
        if phase() != "Music": shot("stuck"); return
    finally:
        if al.poll() is None: al.kill()
    time.sleep(2)
    v = view(); say("view", json.dumps(v)[:300] if v else None)
    if not v: return
    vid = v["id"]
    def lib_info(): return (get("views/%d" % vid) or {}).get("library")
    say("library", json.dumps(lib_info()))
    def idle_both(limit=12):
        t = time.time()
        while time.time() - t < limit:
            li = lib_info() or {}
            if all((li.get(c) or {}).get("action") is None for c in ("K", "Ame")): return True
            time.sleep(0.3)
        return False
    time.sleep(1); shot("01_layout")
    time.sleep(20); say("idle 20 s, library", json.dumps(lib_info()))                 # random moves on their own
    idle_both(); say("touch B1 (K)", post("test/touch", {"area": "B1"})); time.sleep(2.5); shot("02_touch_K"); time.sleep(4)
    idle_both(); say("touch A1 (Ame)", post("test/touch", {"area": "A1"})); time.sleep(2.5); shot("03_touch_Ame"); time.sleep(4)
    idle_both()
    for k in range(3): say("poke A1 (Ame)", k + 1, post("test/touch", {"area": "A1"})); time.sleep(.35)   # three in 1.5 s: she curls up
    time.sleep(2); shot("03b_poke_Ame"); time.sleep(3)
    press("D"); say("song right"); time.sleep(3); press("D"); say("song right"); time.sleep(3)
    say("category", post("views/%d/motion" % vid, {"group": "category"})); time.sleep(6)
    idle_both(); say("master", post("views/%d/motion" % vid, {"group": "master"})); time.sleep(2.5); shot("04_master"); time.sleep(4)
    press("C"); say("NEXT ->", phase()); time.sleep(2); shot("05_difficulty"); time.sleep(3)
    press("C"); say("NEXT ->", phase()); time.sleep(2.5); shot("06_confirm"); time.sleep(3)
    say("library", json.dumps(lib_info()))
    tl = get("views/%d/timeline" % vid)
    if tl: json.dump(tl, open(os.path.join(OUT, "timeline.json"), "w")); say("timeline", len(tl.get("ticks", [])), "pictures")
    if "--no-start" not in sys.argv:
        t_start = time.time(); press("C"); say("START ->", screen(), phase(), "press took", round(time.time() - t_start, 2))
        time.sleep(1.5); shot("07_start")
        for _ in range(40):
            if screen() != "MusicSelectProcess": break
            time.sleep(0.25)
        say("left select", round(time.time() - t_start, 2), "s after the START press began ->", screen())
        if "--result" in sys.argv: result_round()
    with open(LOG, encoding="utf-8", errors="ignore") as f: lines = f.read().splitlines()
    keep = [l for l in lines if " select: " in l or " result: " in l or "library" in l or "Exception" in l or "fallback" in l or "countdown" in l]
    open(os.path.join(OUT, "mod.log"), "w", encoding="utf-8").write("\n".join(keep))
    say(len(keep), "mod log lines")


def result_round():
    """--result: skip the chart (test button held), then the result screen from the library: the automatic reaction to
    the rank (a skipped chart fails), the other tiers through the view's motion route, a tap (the other one notices),
    back to the select page (the return moment) and a proper logout"""
    def wait_screen(name, limit):
        t = time.time()
        while screen() != name and time.time() - t < limit: time.sleep(0.5)
        return screen() == name
    if not wait_screen("GameProcess", 90): say("no GameProcess:", screen()); return
    time.sleep(4); press("Service", 2500); say("skip")
    if not wait_screen("ResultProcess", 60): say("no ResultProcess:", screen()); return
    rv, t = None, time.time()
    while not rv and time.time() - t < 15:
        rv = next((x for x in (get("views") or {}).get("views", []) if x.get("context") == "result"), None); time.sleep(0.5)
    say("result view", json.dumps(rv)[:300] if rv else None)
    if rv:
        rid = rv["id"]
        def info(): return (get("views/%d" % rid) or {}).get("library") or {}
        def idle(limit=12):
            t = time.time()
            while time.time() - t < limit:
                li = info()
                if all((li.get(c) or {}).get("action") is None for c in ("K", "Ame")): return
                time.sleep(0.3)
        time.sleep(7); shot("08_result_auto"); say("result library", json.dumps(info()))
        for tier in ("Top", "Good", "Clear"):
            idle(); say("result" + tier, post("views/%d/motion" % rid, {"group": "result" + tier})); time.sleep(2.5); shot("09_result_" + tier)
        idle(); say("result tap", post("views/%d/tap" % rid, {})); time.sleep(2.5); shot("10_result_tap"); time.sleep(3)
        say("result library", json.dumps(info()))
        tl = get("views/%d/timeline" % rid)
        if tl: json.dump(tl, open(os.path.join(OUT, "timeline_result.json"), "w")); say("result timeline", len(tl.get("ticks", [])), "pictures")
    t = time.time()
    while screen() != "MusicSelectProcess" and time.time() - t < 90:
        if screen() == "ResultProcess": press("C")
        time.sleep(2)
    time.sleep(6); shot("11_select_return"); press("Service", 2500); say("logout")
    t = time.time()
    while time.time() - t < 150:
        s = screen()
        if s == "AdvertiseProcess": break
        if s == "ContinueProcess": press("X")
        elif s not in ("FadeProcess", "UserDataULProcess", "GameOverProcess", ""): press("C")
        time.sleep(2)
    say("end screen", screen())


try:
    main()
finally:
    if "--keep" not in sys.argv:
        subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
    notes.close()
