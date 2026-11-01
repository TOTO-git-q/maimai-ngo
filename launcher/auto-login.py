"""Log in with the card in Package\\DEVICE\\aime.txt. Started by launcher.ps1 and stays alive with the game:
- once at startup, as soon as the attract screen is up;
- again whenever the coin key (Maimoller side button F3) is pressed on the attract / card screen: read here as a key
  (works whichever window is in front), or seen as a coin by the PartnerLive2D mod (only while the game is in front).
After login the game runs its own screens (mode select etc.); nothing is chosen for the player.
The card reader (hinata.dll) and the io board run inside amdaemon.exe and only accept the scan key while the game
window is in front. vkeys.py briefly repoints their GetAsyncKeyState / GetForegroundWindow imports, holds the scan
key, presses "yes" once on the login prompt, and puts the original imports back, so the real keyboard works again
and nothing is typed into other programs. The Maimoller buttons and touch do not go through this path.
usage: python auto-login.py <Sinmai pid> [--no-start-login]   (the flag is for tests: only the F3 login)"""
import json, os, subprocess, sys, time, urllib.request

sys.path.insert(0, r"E:\claude workspace\maimai-partner-live2d\tools")
from vkeys import VirtualKeys

PKG = r"D:\maimai\Package"
API = "http://127.0.0.1:8791/api/test/"
LOG = os.path.join(os.path.dirname(os.path.abspath(__file__)), "launcher.log")
SCAN, YES = 0x0D, 0x43   # segatools [aime] scan=0x0D; C = yes / OK on the login prompt

def log(msg):
    with open(LOG, "a", encoding="utf-8") as f:
        f.write(time.strftime("%H:%M:%S ") + "auto-login: " + msg + "\n")

def get(path):
    try:
        with urllib.request.urlopen(API + path, timeout=3) as r:
            return json.load(r)
    except Exception:
        return None

def screen():
    d = get("processes")
    return d["processes"][-1]["name"].split(".")[-1] if d and d["processes"] else ""

def entry_state():
    d = get("entry")
    return d.get("state") if d else None

def wait(cond, sec):
    t = time.time() + sec
    while time.time() < t:
        if cond(): return True
        time.sleep(0.3)
    return cond()

def quick_count():
    d = get("quicklogin")
    return d["count"] if d else None

def ps(cmd):
    r = subprocess.run(["powershell", "-NoProfile", "-Command", cmd], capture_output=True, text=True,
                       creationflags=0x08000000)
    return r.stdout.strip()

def alive(pid):
    # cheap check (no child process): this runs every half second while the game is played
    import ctypes
    k32 = ctypes.windll.kernel32
    h = k32.OpenProcess(0x1000, False, pid)   # PROCESS_QUERY_LIMITED_INFORMATION
    if not h: return False
    code = ctypes.c_ulong()
    ok = k32.GetExitCodeProcess(h, ctypes.byref(code))
    k32.CloseHandle(h)
    return bool(ok) and code.value == 259    # STILL_ACTIVE

def login(game_pid):
    ap = ps("(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'amdaemon.exe' -and "
            "$_.ExecutablePath -like '" + PKG + "\\*' } | Select-Object -First 1).ProcessId")
    if not ap: log("amdaemon not found, skipped"); return
    hwnd = int(ps("(Get-Process -Id %d).MainWindowHandle.ToInt64()" % game_pid) or 0)
    vk = VirtualKeys(int(ap), hwnd)   # the io board only takes keys while this window counts as foreground
    try:
        # on the attract screen the first scan only opens the card screen; the card is read by a scan made
        # while that screen waits for one (entry state ConfirmEntry -> ConfirmPlay "play with this Aime?")
        if screen() != "EntryProcess":
            for attempt in range(3):
                vk.press(SCAN, 3000)
                if wait(lambda: screen() == "EntryProcess", 12): break
            else:
                log("card screen did not open"); return
        for attempt in range(5):
            if entry_state() != "ConfirmPlay":
                time.sleep(1.5)
                vk.press(SCAN, 3000)
                if not wait(lambda: entry_state() == "ConfirmPlay", 10): continue
            log("card read")
            # the prompt ignores "yes" while it is still opening; it falls back to waiting for a card if left alone
            time.sleep(2.5)
            if entry_state() != "ConfirmPlay": continue
            vk.press(YES, 200)   # "play with this Aime?" -> yes (only ever pressed on this prompt, never on guest play)
            if wait(lambda: screen() != "EntryProcess", 8):
                log("login confirmed"); return
        log("login failed (entry state %s)" % entry_state())
    finally:
        # the io board only polls the keyboard while the game counts as foreground: let it see every key
        # released before the real imports (and the real foreground window) come back, or a key stays latched
        time.sleep(1.0)
        vk.restore()

def main(game_pid, start_login=True):
    deadline = time.time() + 300
    while time.time() < deadline:
        if not alive(game_pid): return
        if screen() in ("AdvertiseProcess", "EntryProcess"): break
        time.sleep(2)
    else:
        log("attract screen never came up, skipped"); return
    time.sleep(2)
    if start_login: login(game_pid)

    # stay with the game: the side F3 key on the attract screen logs in again. Two ways in: the coin the game counts
    # (only while the game window is in front, segatools reads the keyboard only then), and the F3 key read here
    # directly (GetAsyncKeyState sees it whichever window is in front, e.g. after Claude or a browser took focus)
    import ctypes
    u32 = ctypes.windll.user32
    F3 = 0x72
    seen = quick_count(); was_down = False; next_coin = 0.0
    while alive(game_pid):
        time.sleep(0.1)
        down = bool(u32.GetAsyncKeyState(F3) & 0x8000)
        pressed, was_down = down and not was_down, down
        coin = False
        if time.time() >= next_coin:
            next_coin = time.time() + 0.5
            n = quick_count()
            if n is not None and seen is not None and n > seen: coin = True
            if n is not None: seen = n
        if not (pressed or coin): continue
        # a coin on the attract screen may make the game open the card screen right away
        if wait(lambda: screen() in ("AdvertiseProcess", "EntryProcess"), 5):
            log("F3 (%s) on the attract screen" % ("key" if pressed else "coin"))
            login(game_pid)
            n = quick_count()                     # the same press may also have counted as a coin: don't log in twice
            if n is not None: seen = n
            was_down = bool(u32.GetAsyncKeyState(F3) & 0x8000)

if __name__ == "__main__":
    try:
        main(int(sys.argv[1]), "--no-start-login" not in sys.argv)
    except Exception as e:
        log("error: %r" % (e,))
