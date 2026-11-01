"""Skin check: attract -> login -> every screen to music select (and its phases), screenshot each, record theme status.
usage: python tools/ngo_skin_walk.py <out_dir>   (run with DRIVE_FULL_ENTRY=1; BURST=<n> also grabs n frames 0.7 s apart
on the first mode- or area-select screen (or the screens named in BURST_AT, comma separated), plus a renderer dump,
for checking background motion; BURST_DT sets the interval; ATTRACT_SECONDS=<s> grabs a frame every 1.5 s of the
attract loop before logging in, e.g. to catch the title page; SELECTPERF=<s> idles s seconds at the end of music
select and saves the layout cost from /api/test/selectperf to selectperf.json)"""
import json, os, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phase_round_lib import screen, phase, press, wait_screen, say_to, DRIVE, get, game_pid

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
say = say_to(os.path.join(OUT, "walk.log"))
n = [0]

def shot(tag):
    n[0] += 1
    png = get("screenshot.png", raw=True)
    if png: open(os.path.join(OUT, "%02d_%s.png" % (n[0], tag)), "wb").write(png)
    ui = get("test/ui")
    if ui: json.dump(ui, open(os.path.join(OUT, "%02d_%s.ui.json" % (n[0], tag)), "w", encoding="utf-8"), ensure_ascii=False)

subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
al = None
try:
    if not wait_screen(("AdvertiseProcess",), 240): raise RuntimeError("no attract screen")
    time.sleep(6); shot("attract")
    for i in range(int(float(os.environ.get("ATTRACT_SECONDS", "0")) / 1.5)):   # watch the attract loop before login
        time.sleep(1.5); png = get("screenshot.png", raw=True)
        if png: open(os.path.join(OUT, "attract_%03d.png" % i), "wb").write(png)
    al = subprocess.Popen([sys.executable, r"D:\maimai\auto-login.py", str(game_pid())])
    t = time.time(); last = None; same = 0
    while time.time() - t < 400:
        s = screen()
        if s != last:
            say("screen", s); last = s; same = 0
            at = tuple(os.environ.get("BURST_AT", "ModeSelect,RegionalSelect").split(","))
            if not (s.startswith(at) and os.environ.get("BURST_NOW")):   # BURST_NOW: burst before settling (transitions)
                time.sleep(2.5); shot(s)
            if s.startswith(at) and int(os.environ.get("BURST", "0")) and not os.path.exists(os.path.join(OUT, "renderers.json")):
                rd = get("test/renderers")
                if rd: json.dump(rd, open(os.path.join(OUT, "renderers.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
                for i in range(int(os.environ["BURST"])):
                    time.sleep(float(os.environ.get("BURST_DT", "0.7"))); png = get("screenshot.png", raw=True)
                    if png: open(os.path.join(OUT, "burst_%02d.png" % i), "wb").write(png)
        if s.startswith("MusicSelect"): break
        if s not in ("AdvertiseProcess", "EntryProcess") or time.time() - t > 30:
            same += 1
            if same % 2 == 0: shot(s + "_b"); press("C")
        time.sleep(2)
    time.sleep(5); shot("select_" + str(phase()))
    for i in range(3):
        press("C"); time.sleep(3); shot("select_" + str(phase()))
    if os.environ.get("SELECTPERF"):   # cost of the music-select layout: idle on the music list, then dump
        get("test/selectperf?reset=1"); time.sleep(float(os.environ["SELECTPERF"]))
        json.dump(get("test/selectperf"), open(os.path.join(OUT, "selectperf.json"), "w"), indent=1)
    status = get("theme")
    json.dump(status, open(os.path.join(OUT, "theme_status.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    if status: say("theme enabled", status.get("enabled"), "loaded", len(status.get("loaded", [])), "errors", len(status.get("errors", {})))
finally:
    if al: al.kill()
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
