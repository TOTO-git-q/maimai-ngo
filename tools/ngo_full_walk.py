"""Full-flow skin check, off screen and muted: attract -> card login -> every screen to music select -> each select
phase -> difficulty (then LEFT pressed DIFF_LEFT times, with the mod's layout state after each press) -> track start
-> gameplay (a frame every PLAY_DT s; mod frame log on) -> result -> back to select -> logout (AquaMai OneKeyEntryEnd,
Service held) -> attract. Screens go to <out>/NN_<tag>.png (+ .ui.json); diff_left.json, game_frames.txt, walk.log.
usage: python tools/ngo_full_walk.py <out_dir>   (env: DIFF_LEFT=5, LEFT_KEY=Z, PLAY_DT=12, PLAY_MAX=200, TESTINPUT=1 turns
the mod's test input / autoplay API on for this run; settings.ini is restored afterwards)"""
import json, os, re, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phase_round_lib as lib
from phase_round_lib import screen, phase, press, wait_screen, say_to, DRIVE, get, game_pid

lib.K.update({"Z": 0x5A, "A": 0x41, "Q": 0x51, "W": 0x57, "E": 0x45})
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
say = say_to(os.path.join(OUT, "walk.log"))
SETTINGS = r"D:\maimai\Package\LocalAssets\PartnerLive2D\settings.ini"
LATEST = r"D:\maimai\Package\MelonLoader\Latest.log"
n = [0]

def shot(tag):
    n[0] += 1
    png = get("screenshot.png", raw=True)
    if png: open(os.path.join(OUT, "%02d_%s.png" % (n[0], tag)), "wb").write(png)
    ui = get("test/ui")
    if ui: json.dump(ui, open(os.path.join(OUT, "%02d_%s.ui.json" % (n[0], tag)), "w", encoding="utf-8"), ensure_ascii=False)

def side():
    d = get("music-select")
    try: return d["sessions"][0]["sides"][0]
    except Exception: return None

def follow(until, timeout, push="C", every=2):
    """Shoot every new screen; press `push` every `every` polls on screens that wait for input; stop at `until`."""
    t = time.time(); last = None; same = 0
    while time.time() - t < timeout:
        s = screen()
        if s != last:
            say("screen", s); last = s; same = 0
            if s.startswith("TrackStart"):   # the card screen only shows between two transitions: shoot a burst
                for k in range(int(os.environ.get("TST_FRAMES", "14"))):
                    time.sleep(float(os.environ.get("TST_DT", "0.4"))); shot("%s_%02d" % (s, k))
            else:
                time.sleep(2.5); shot(s)
        if until(s): return s
        if s == "ModeSelectProcess" and int(os.environ.get("MODE_RIGHT", "0")):   # pick a mode right of NORMAL
            time.sleep(2)
            for k in range(int(os.environ["MODE_RIGHT"])):
                press("D"); time.sleep(1.2); shot("mode_right%d" % (k + 1))
            os.environ["MODE_RIGHT"] = "0"
        if push and s not in ("AdvertiseProcess",):
            same += 1
            if same % every == 0: shot(s + "_b"); press(push)
        time.sleep(2)
    return None

ini = open(SETTINGS, encoding="utf-8").read()
if os.environ.get("TESTINPUT"):   # the mod's test input / autoplay API; read at game start, so set before it
    open(SETTINGS, "w", encoding="utf-8").write(re.sub(r"(?m)^testInput\s*=.*\n?", "", ini).rstrip("\n") + "\ntestInput = true\n")
subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
al = None
try:
    cur = open(SETTINGS, encoding="utf-8").read()
    open(SETTINGS, "w", encoding="utf-8").write(re.sub(r"(?m)^frameLog\s*=.*\n?", "", cur).rstrip("\n") + "\nframeLog = true\n")
    if os.environ.get("BOOT_SHOTS"):   # the power-on / self-check screens, a frame every second
        t0 = time.time()
        while screen() not in ("AdvertiseProcess", "WarningProcess") and time.time() - t0 < 240:
            png = get("screenshot.png", raw=True)
            if png: open(os.path.join(OUT, "boot_%03d_%s.png" % (int(time.time() - t0), screen() or "none")), "wb").write(png)
            time.sleep(1)
    if not wait_screen(("AdvertiseProcess",), 240): raise RuntimeError("no attract screen")
    time.sleep(6); shot("attract")
    for i in range(int(float(os.environ.get("ATTRACT_SECONDS", "0")) / 1.5)):   # the attract loop (title page)
        time.sleep(1.5); png = get("screenshot.png", raw=True)
        if png: open(os.path.join(OUT, "attract_%03d.png" % i), "wb").write(png)
    al = subprocess.Popen([sys.executable, r"D:\maimai\auto-login.py", str(game_pid())])
    if not follow(lambda s: s.startswith("MusicSelect"), 400): raise RuntimeError("no music select")
    al.kill(); al = None

    # select phases up to difficulty
    for i in range(6):
        time.sleep(3); p = phase(); shot("select_%s" % p)
        if p in ("Difficulty", "UtageDifficulty"): break
        if p == "Music" and int(os.environ.get("CAT_LEFT", "0")):   # category tabs left (button 7 = A), state each time
            cats = []
            for k in range(int(os.environ["CAT_LEFT"])):
                press("A"); time.sleep(1.2)
                sd = side() or {}
                cats.append({"press": k + 1, "phase": sd.get("phase"), "reason": sd.get("reason"), "side": sd})
                say("category left", k + 1, "phase", sd.get("phase"), "reason", sd.get("reason")); shot("cat_left%d" % (k + 1))
            json.dump(cats, open(os.path.join(OUT, "cat_left.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
            os.environ["CAT_LEFT"] = "0"
            if os.environ.get("CAT_BURST"):   # fast taps (100 ms down, 200 ms up) and a 2 s hold, like real play
                from vkeys import VirtualKeys
                ap = int(lib.ps("(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'amdaemon.exe' } | Select-Object -First 1).ProcessId"))
                hw = int(lib.ps("(Get-Process -Id %d).MainWindowHandle.ToInt64()" % game_pid()) or 0)
                vk = VirtualKeys(ap, hw)
                try:
                    for r in range(int(os.environ["CAT_BURST"])):
                        for k in range(4): vk.press(0x41, 100); time.sleep(0.2)
                        time.sleep(0.6); sd = side() or {}
                        say("burst", r + 1, "phase", sd.get("phase"), "reason", sd.get("reason"), "applied", (sd.get("layout") or {}).get("applied"))
                        shot("cat_burst%d" % (r + 1))
                    vk.press(0x41, 2000); time.sleep(0.8); sd = side() or {}
                    say("hold", "phase", sd.get("phase"), "reason", sd.get("reason"), "applied", (sd.get("layout") or {}).get("applied"))
                    shot("cat_hold")
                    for k in range(8): vk.press(0x45, 100); time.sleep(0.2)   # back right fast (button 2 = E)
                    time.sleep(0.6); shot("cat_back_right")
                finally:
                    vk.restore()
            if os.environ.get("SELECT_ONLY"): raise SystemExit(0)
        press("C")
    states = []
    for i in range(int(os.environ.get("DIFF_LEFT", "5"))):
        press(os.environ.get("LEFT_KEY", "Z")); time.sleep(1.5)
        sd = side() or {}
        states.append({"press": i + 1, "phase": sd.get("phase"), "reason": sd.get("reason"), "applied": sd.get("applied"), "side": sd})
        say("left", i + 1, "phase", sd.get("phase"), "reason", sd.get("reason")); shot("diff_left%d" % (i + 1))
    json.dump(states, open(os.path.join(OUT, "diff_left.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    # start the track and play
    t0 = time.strftime("%H:%M:%S")
    if not follow(lambda s: s == "GameProcess", 150): raise RuntimeError("track did not start")
    start = time.strftime("%H:%M:%S"); t = time.time()
    while screen() == "GameProcess" and time.time() - t < float(os.environ.get("PLAY_MAX", "200")):
        time.sleep(float(os.environ.get("PLAY_DT", "12"))); shot("game")
    end = time.strftime("%H:%M:%S")
    lines = [l.rstrip() for l in open(LATEST, encoding="utf-8", errors="ignore") if "[frame]" in l]
    window = [l for l in lines if start <= l[1:9] <= end]
    open(os.path.join(OUT, "game_frames.txt"), "w", encoding="utf-8").write("\n".join(window))
    say("game frames:", len(window), "lines;", *[l for l in window if "summary" in l][-3:])

    # result -> back to music select
    follow(lambda s: s.startswith("MusicSelect"), 120)
    time.sleep(4); shot("select_after_track")
    # logout: AquaMai OneKeyEntryEnd = Service held
    press("Service", 2500)
    follow(lambda s: s == "AdvertiseProcess", 180, push="C", every=3)
    time.sleep(4); shot("attract_after_logout")
finally:
    if al: al.kill()
    open(SETTINGS, "w", encoding="utf-8").write(ini)
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
