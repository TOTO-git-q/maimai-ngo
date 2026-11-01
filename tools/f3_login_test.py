"""F3 login: on the attract screen the coin key (F3) must log the user in; the game then shows its own screens up to
mode select. usage: python tools/f3_login_test.py <out_dir>   (DRIVE_KEEP_TIMEOUT=1)"""
import os, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phase_round_lib as L
from phase_round_lib import screen, press, wait_screen, say_to, DRIVE, get, game_pid

L.K["F3"] = 0x72
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
say = say_to(os.path.join(OUT, "f3.log"))

subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
al = None
try:
    if not wait_screen(("AdvertiseProcess",), 240): raise RuntimeError("no attract screen")
    al = subprocess.Popen([sys.executable, r"D:\maimai\auto-login.py", str(game_pid()), "--no-start-login"])
    time.sleep(8)
    say("attract:", screen(), "quicklogin", get("test/quicklogin"))
    press("F3", 200)
    say("pressed F3")
    t = time.time(); last = None
    while time.time() - t < 90:
        s = screen()
        if s != last: say("screen", s); last = s
        if s.startswith("ModeSelect"): break
        time.sleep(0.5)
    time.sleep(4)
    png = get("screenshot.png", raw=True)
    if png: open(os.path.join(OUT, "after_f3.png"), "wb").write(png)
finally:
    if al: al.kill()
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
    lines = [l.rstrip() for l in open(r"D:\maimai\launcher.log", encoding="utf-8", errors="ignore") if "auto-login" in l]
    say("auto-login log tail:\n  " + "\n  ".join(lines[-4:]))
