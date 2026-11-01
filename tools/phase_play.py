"""Hitch probe in gameplay: log in, start the first chart from music select, let it play, collect [frame] lines.
usage: python tools/phase_play.py <out_dir> <label> [play_seconds]"""
import os, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phase_round_lib import screen, phase, press, wait_screen, say_to, DRIVE

OUT, LABEL = sys.argv[1], sys.argv[2]
PLAY = int(sys.argv[3]) if len(sys.argv) > 3 else 70
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
    for i in range(10):
        s, p = screen(), phase()
        say("start step", i, s, p)
        if s in ("GameProcess", "TrackStartProcess", "MovieTrackStartProcess") or s.startswith("TrackStart"): break
        press("C"); time.sleep(3)
    if not wait_screen(("GameProcess",), 150):
        say("chart did not start:", screen(), phase()); sys.exit(0)
    say("playing for", PLAY, "s")
    start = time.strftime("%H:%M:%S"); time.sleep(PLAY); end = time.strftime("%H:%M:%S")
    lines = [l.rstrip() for l in open(LATEST, encoding="utf-8", errors="ignore") if "[frame]" in l]
    window = [l for l in lines if start <= l[1:9] <= end]
    open(os.path.join(OUT, LABEL + "_frames.txt"), "w", encoding="utf-8").write("\n".join(window))
    spikes = [l for l in window if "summary" not in l]
    say(LABEL, "spikes:", len(spikes)); [say(l) for l in spikes]
    [say(l) for l in window if "summary" in l]
finally:
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
