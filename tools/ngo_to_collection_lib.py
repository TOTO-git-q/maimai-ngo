"""Shared steps for off-screen runs that go through a whole credit (used by ngo_collection_check.py):
start the muted test game parked off screen, log in with the card in DEVICE\\aime.txt, play every track of the credit
(started and skipped) until the end-of-credit collection, log out, stop."""
import ctypes, ctypes.wintypes as wt, subprocess, time
import phase_round_lib as lib
from phase_round_lib import screen, phase, press, get

u32 = ctypes.windll.user32
_hwnd = [None]


def _game_hwnd(pid):
    found = []
    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, _):
        p = wt.DWORD(); u32.GetWindowThreadProcessId(h, ctypes.byref(p))
        if p.value == pid and u32.IsWindowVisible(h): found.append(h)
        return True
    u32.EnumWindows(cb, 0)
    return found[0] if found else None


def park():
    h = _hwnd[0]
    if not h: return
    r = wt.RECT(); u32.GetWindowRect(h, ctypes.byref(r))
    if r.left > -3000: u32.SetWindowPos(h, 0, -4200, 40, 0, 0, 0x0001 | 0x0004 | 0x0010)


def wait_for(names, timeout):
    t = time.time()
    while time.time() - t < timeout:
        if screen() in names: return True
        time.sleep(0.5)
    return False


def start_and_login(say):
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", lib.DRIVE, "start"], check=True)
    pid = lib.game_pid(); t0 = time.time()
    while time.time() - t0 < 60 and not _hwnd[0]: _hwnd[0] = _game_hwnd(pid); time.sleep(0.05)
    park()
    while not get("status"):
        park()
        if time.time() - t0 > 180: raise SystemExit("mod API never came up")
        time.sleep(1)
    if not wait_for(("AdvertiseProcess",), 240): raise SystemExit("no attract screen")
    # The reader may come up minutes after the attract screen (MuMod loads late on a slow network), so scan again until
    # hinata reports the card. Never press any other button on the attract screen: that starts a GUEST entry.
    t = time.time(); scanned = False
    while time.time() - t < 420 and not scanned:
        time.sleep(6); before = _aime_reads(); say("scan card"); press("scan", 3000); time.sleep(4)
        scanned = _aime_reads() > before
    if not scanned: raise SystemExit("the card was never read")
    # On the entry screen press "yes" only when the game asks to play with THIS card (state ConfirmPlay). When the
    # server lookup fails it asks ConfirmGuest instead; "yes" there turns the run into a guest play, so stop.
    t = time.time(); confirmed = False
    while screen() != "MusicSelectProcess" and time.time() - t < 240:
        park()
        s = screen()
        if s == "EntryProcess":
            st = (get("test/entry") or {}).get("state")
            if st in ("ConfirmGuest", "ConfirmNewUser"):
                raise SystemExit("entry asks " + st + " (server lookup failed?); not playing as guest")
            if st == "ConfirmPlay" and not confirmed:
                press("C"); confirmed = True
        elif s not in ("AdvertiseProcess", "FadeProcess", ""):
            press("C")   # login flow screens after the entry (info windows, mode select, area, tickets)
        time.sleep(1.5)
    if screen() != "MusicSelectProcess": raise SystemExit("login did not reach music select: " + screen())
    say("logged in:", screen())


def _aime_reads():
    """number of card reads hinata has logged so far (the card number itself is never read out)"""
    import os
    p = os.path.join(os.environ.get("PARTNERLIVE2D_PACKAGE", r"D:\maimai\Package"), "hinata.log")
    try:
        with open(p, encoding="utf-8", errors="replace") as f: return sum(1 for line in f if "Detected Aime" in line)
    except OSError: return 0


def play_until_collection(say, max_tracks=6):
    track = 0
    while track < max_tracks:
        s = screen()
        if s == "CollectionProcess": return True
        if s == "MusicSelectProcess":
            track += 1; say("track", track)
            for _ in range(6):
                if phase() == "Menu": break
                press("C"); time.sleep(1.5)
            press("C", 150)
            if wait_for(("GameProcess",), 120): time.sleep(4); press("Service", 2500); say("skip")
            if wait_for(("ResultProcess",), 60): time.sleep(6); press("C")
            t = time.time()
            while time.time() - t < 90:
                s = screen()
                if s in ("MusicSelectProcess", "CollectionProcess"): break
                if s not in ("FadeProcess", "NextTrackProcess", "MusicSelectInfoProcess", "GameProcess", ""): press("C")
                time.sleep(2.5)
        elif s == "AdvertiseProcess":
            raise SystemExit("back on the attract screen (credit ended early); not starting a guest entry")
        else:
            press("C"); time.sleep(2.5)
    return screen() == "CollectionProcess"


def logout(say):
    """End the credit with a long Service press (AquaMai OneKeyEntryEnd) and only answer "no" to continue; pressing
    C on the way out can start a new credit."""
    if screen() not in ("AdvertiseProcess", "GameOverProcess", "UserDataULProcess"):
        press("Service", 2500)
    t = time.time()
    while time.time() - t < 180:
        s = screen()
        if s == "AdvertiseProcess": break
        if s == "ContinueProcess": press("X")
        time.sleep(2)
    say("end screen", screen())


def stop(say):
    try: subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", lib.DRIVE, "stop"], check=True)
    except Exception as e: say("stop failed", e)
