"""Check freedomEntry: log in, land in music select as a freedom play, start one song.
usage: python tools/freedom_entry_test.py <out_dir>   (run with DRIVE_KEEP_TIMEOUT=1 to keep the user's timer config)"""
import os, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phase_round_lib import screen, phase, press, wait_screen, say_to, DRIVE, get

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
say = say_to(os.path.join(OUT, "freedom_entry.log"))
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
    while not screen().startswith("MusicSelect") and time.time() - t0 < 120:
        time.sleep(1)
    say("reached", screen(), "after", round(time.time() - t0), "s without extra presses")
    while not screen().startswith("MusicSelect") and time.time() - t0 < 240:
        press("C"); time.sleep(2.5)
    time.sleep(6)
    png = get("screenshot.png", raw=True)
    if png: open(os.path.join(OUT, "select.png"), "wb").write(png)
    log = [l.rstrip() for l in open(LATEST, encoding="utf-8", errors="ignore") if "freedomEntry" in l]
    say("mod log:", log[-1] if log else "NO freedomEntry line")
    # start a song: genre -> music -> difficulty -> start
    t1 = time.time()
    while screen() not in ("GameProcess",) and time.time() - t1 < 120:
        press("C"); time.sleep(2.5)
    say("song start:", screen(), "after", round(time.time() - t1), "s")
    time.sleep(8)
    png = get("screenshot.png", raw=True)
    if png: open(os.path.join(OUT, "game.png"), "wb").write(png)
finally:
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
