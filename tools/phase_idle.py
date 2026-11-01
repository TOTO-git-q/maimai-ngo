"""Hitch probe: log in, enter music select, sit idle in the Music phase and collect the mod's [frame] lines.
usage: python tools/phase_idle.py <out_dir> <label> [idle_seconds]
The mod must run with settings.ini frameLog = true. drive.ps1 mutes and restores everything."""
import json, os, re, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phase_round_lib import screen, phase, press, wait_screen, say_to, DRIVE, get

OUT, LABEL = sys.argv[1], sys.argv[2]
IDLE = int(sys.argv[3]) if len(sys.argv) > 3 else 60
os.makedirs(OUT, exist_ok=True)
say = say_to(os.path.join(OUT, LABEL + ".log"))
LATEST = r"D:\maimai\Package\MelonLoader\Latest.log"

subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
try:
    if not wait_screen(("AdvertiseProcess",), 240): raise RuntimeError("no attract screen")
    time.sleep(2)
    for i in range(3):
        press("scan", 3000); time.sleep(4)
        if screen() == "EntryProcess": break
    time.sleep(2); press("C")
    t0 = time.time()
    while not screen().startswith("MusicSelect") and time.time() - t0 < 180:
        press("C"); time.sleep(2.5)
    time.sleep(4)
    if phase() == "Genre": press("C"); time.sleep(3)
    say("idle in", phase(), "for", IDLE, "s")
    png = get("screenshot.png", raw=True)
    if png: open(os.path.join(OUT, LABEL + "_screen.png"), "wb").write(png)
    info = get("music-select")
    if info: open(os.path.join(OUT, LABEL + "_select.json"), "w", encoding="utf-8").write(json.dumps(info, ensure_ascii=False, indent=1))
    tree = get("music-select/tree?depth=6")
    if tree: open(os.path.join(OUT, LABEL + "_tree.json"), "w", encoding="utf-8").write(json.dumps(tree, ensure_ascii=False, indent=1))
    start = time.strftime("%H:%M:%S")
    time.sleep(IDLE)
    end = time.strftime("%H:%M:%S")
    lines = [l.rstrip() for l in open(LATEST, encoding="utf-8", errors="ignore") if "[frame]" in l]
    window = [l for l in lines if start <= l[1:9] <= end]
    open(os.path.join(OUT, LABEL + "_frames.txt"), "w", encoding="utf-8").write("\n".join(window))
    spikes = [l for l in window if "summary" not in l]
    say(LABEL, "spikes:", len(spikes)); [say(l) for l in spikes]
finally:
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
