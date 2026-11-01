"""Whole entry flow as the user gets it: auto-login.py logs in at the attract screen, one song, end the credit with a
Service long-press, then ring button 1 on the attract screen must log in again. Screenshots every screen on the way
(to spot tutorials / popups). usage: python tools/entry_flow_test.py <out_dir>   (DRIVE_KEEP_TIMEOUT=1)"""
import os, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phase_round_lib as L
from phase_round_lib import screen, press, wait_screen, say_to, DRIVE, get, game_pid

L.K["W"] = 0x57   # ring button 1
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
say = say_to(os.path.join(OUT, "flow.log"))
n = [0]

def shot(tag):
    n[0] += 1
    png = get("screenshot.png", raw=True)
    if png: open(os.path.join(OUT, "%02d_%s.png" % (n[0], tag)), "wb").write(png)

def watch_until(target, timeout, press_key=None, every=4.0):
    """follow screens until target; screenshot each new screen (and each time before pressing)"""
    t = time.time(); last = None
    while time.time() - t < timeout:
        s = screen()
        if s != last: say("screen", s); last = s; time.sleep(2); shot(s)
        if s.startswith(target): return True
        if press_key and s not in ("GameProcess", "TrackStartProcess"):
            # CONTINUE? -> "no" (button 5, bottom left); everything else -> the given key
            shot(s + "_before_press"); press("X" if s == "ContinueProcess" else press_key)
        time.sleep(every)
    return False

subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
al = None
try:
    if not wait_screen(("AdvertiseProcess",), 240): raise RuntimeError("no attract screen")
    al = subprocess.Popen([sys.executable, r"D:\maimai\auto-login.py", str(game_pid())])
    say("auto-login started")
    ok = watch_until("MusicSelect", 120)
    say("auto login ->", screen(), ok)
    time.sleep(8); shot("select_idle")
    # one song: genre -> music -> difficulty -> start, then let it play out
    if not os.environ.get("SKIP_SONG"):
        t = time.time()
        while screen() != "GameProcess" and time.time() - t < 120: press("C"); time.sleep(2.5)
        say("playing", screen())
        wait_screen(("ResultProcess",), 400)
        watch_until("MusicSelect", 180, press_key="C")
        say("back in", screen())
    # end the credit (AquaMai OneKeyEntryEnd: long-press Service at music select)
    time.sleep(5); press("Service", 3000)
    watch_until("AdvertiseProcess", 180, press_key="C")
    say("attract:", screen())
    time.sleep(6)
    press("W", 200)
    say("pressed ring button 1")
    ok = watch_until("MusicSelect", 120)
    say("button-1 login ->", screen(), ok)
finally:
    if al: al.kill()
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
    lines = [l.rstrip() for l in open(r"D:\maimai\launcher.log", encoding="utf-8", errors="ignore") if "auto-login" in l]
    say("auto-login log tail:\n  " + "\n  ".join(lines[-8:]))
