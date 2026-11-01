"""Shared helpers for the muted test runs (game driven through vkeys inside amdaemon, screens read from the mod API)."""
import ctypes, json, os, subprocess, time, urllib.request
from vkeys import VirtualKeys

DRIVE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "drive.ps1")
API = "http://127.0.0.1:8791/api/"
K = {"scan": 0x0D, "C": 0x43, "X": 0x58, "D": 0x44, "Service": 0x71}
u32 = ctypes.windll.user32; k32 = ctypes.windll.kernel32

def say_to(path):
    f = open(path, "a", encoding="utf-8")
    def say(*a):
        s = time.strftime("%H:%M:%S ") + " ".join(map(str, a)); print(s, flush=True); f.write(s + "\n"); f.flush()
    return say

def get(path, raw=False):
    try:
        with urllib.request.urlopen(API + path, timeout=10) as r:
            b = r.read()
        return b if raw else json.loads(b)
    except Exception:
        return None

def screen():
    d = get("test/processes")
    return d["processes"][-1]["name"].split(".")[-1] if d and d["processes"] else ""

def phase():
    d = get("music-select")
    try: return d["sessions"][0]["sides"][0]["phase"]
    except Exception: return None

def ps(cmd):
    return subprocess.run(["powershell", "-NoProfile", "-Command", cmd], capture_output=True, text=True).stdout.strip()

def game_pid():
    return json.load(open(os.path.join(os.path.dirname(DRIVE), "drive-state.json")))["gamePid"]

def press(key, ms=180):
    ap = int(ps("(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'amdaemon.exe' } | Select-Object -First 1).ProcessId"))
    h = int(ps("(Get-Process -Id %d).MainWindowHandle.ToInt64()" % game_pid()) or 0)
    vk = VirtualKeys(ap, h)
    try:
        vk.press(K[key], ms); time.sleep(0.8)   # let the io board see the release before restoring
    finally:
        vk.restore()

def wait_screen(names, timeout):
    t = time.time()
    while time.time() - t < timeout:
        if screen() in names: return True
        time.sleep(1)
    return False
