"""Log the card screen's state machine through: attract -> scan -> (scan again) -> yes. usage: python tools/entry_state_probe.py"""
import os, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phase_round_lib import screen, press, wait_screen, DRIVE, get

def follow(sec):
    last = None; t = time.time()
    while time.time() - t < sec:
        s = (screen(), get("test/entry"))
        if s != last: print(time.strftime("%H:%M:%S"), s, flush=True); last = s
        time.sleep(0.3)

subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
try:
    wait_screen(("AdvertiseProcess",), 240); time.sleep(2)
    print("scan 1"); press("scan", 3000); follow(8)
    print("scan 2"); press("scan", 3000); follow(8)
    print("yes"); press("C"); follow(12)
finally:
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
