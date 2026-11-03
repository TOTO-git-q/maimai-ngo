"""What the mod costs while a chart plays (owner 2026-11-02 night: "确实会掉帧……可能是游玩的过程中mod占用资源导致的"):
off screen and muted, log in, start the chart under the cursor with autoplay (all critical perfect, so the score and
judgement UI change as in real play), let it play to the end and print the song's "[play]" and "[play-mod]" lines
(PlayStats.cs: late frames, and the mod's own time per frame and per part). Logs out and stops the game.
usage: python tools/measure_play.py <out dir>"""
import json, os, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phase_round_lib as lib
from phase_round_lib import screen, phase, press, get, say_to, DRIVE
import ngo_to_collection_lib as tc

OUT = os.path.abspath(sys.argv[1]); os.makedirs(OUT, exist_ok=True)
say = say_to(os.path.join(OUT, "measure.log"))
LOG = r"D:\maimai\Package\MelonLoader\Latest.log"


def post(path, body):
    import urllib.request
    req = urllib.request.Request(lib.API + path, data=json.dumps(body).encode(), method="POST",
                                 headers={"X-PartnerLive2D": "1", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=15) as r: return json.loads(r.read() or b"null")
    except Exception as e: return {"error": str(e)}


def wait_screen(name, limit):
    t = time.time()
    while screen() != name and time.time() - t < limit: time.sleep(0.5)
    return screen() == name


subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
try:
    pid = lib.game_pid(); t0 = time.time()
    while not get("test/processes") and time.time() - t0 < 180: time.sleep(2)
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
    finally:
        if al.poll() is None: al.kill()
    if phase() != "Music": raise SystemExit("no song list: %s %s" % (screen(), phase()))
    say("autoplay", post("test/autoplay", {"mode": "Critical"}))
    time.sleep(2); press("C"); time.sleep(2.5); press("C"); time.sleep(2.5); press("C")
    if not wait_screen("GameProcess", 90): raise SystemExit("no GameProcess: " + screen())
    say("playing")
    if not wait_screen("ResultProcess", 420): raise SystemExit("the chart never ended: " + screen())
    time.sleep(3)
    with open(LOG, encoding="utf-8", errors="ignore") as f: lines = f.read().splitlines()
    keep = [l for l in lines if "[play" in l or "[stall]" in l or "[clean]" in l]
    for l in keep: say(l)
    open(os.path.join(OUT, "play_lines.txt"), "w", encoding="utf-8").write("\n".join(keep))
    say("autoplay off", post("test/autoplay", {"mode": "None"}))
    tc.logout(say)
finally:
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
