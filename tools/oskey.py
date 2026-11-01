"""Press one key in the off-screen test game without leaving it in front.
usage: python tools/oskey.py <Key> [holdMs] [--shot out.png] [--park]
Key = Return | C | X | D | Z | W | E | A | Q | Service | Test ; game started with drive.ps1 start.
Brings the game window forward only for the press, then hands focus back; --shot saves a mod screenshot
~1.2 s later; --park moves the game window off screen first."""
import ctypes, ctypes.wintypes as wt, json, os, sys, time, urllib.request

VK = {"Return": 0x0D, "Service": 0x71, "Test": 0x70}
user32 = ctypes.windll.user32; kernel32 = ctypes.windll.kernel32
user32.GetForegroundWindow.restype = wt.HWND
STATE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "drive-state.json")

def game_hwnd(pid):
    found = []
    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, _):
        p = wt.DWORD(); user32.GetWindowThreadProcessId(h, ctypes.byref(p))
        if p.value == pid and user32.IsWindowVisible(h): found.append(h)
        return True
    user32.EnumWindows(cb, 0)
    return found[0] if found else None

def front(h):
    fg = user32.GetForegroundWindow()
    if fg == h: return True
    me = kernel32.GetCurrentThreadId()
    other = user32.GetWindowThreadProcessId(fg, None) if fg else 0
    if other and other != me: user32.AttachThreadInput(me, other, True)
    user32.BringWindowToTop(h); user32.SetForegroundWindow(h)
    if other and other != me: user32.AttachThreadInput(me, other, False)
    for _ in range(30):
        if user32.GetForegroundWindow() == h: return True
        time.sleep(0.01)
    return False

def park(h):
    user32.SetWindowPos(h, 0, -4200, 40, 0, 0, 0x0001 | 0x0004 | 0x0010)

def press(name, ms=150):
    pid = json.load(open(STATE))["gamePid"]
    h = game_hwnd(pid)
    if not h: raise SystemExit("game window not found")
    prev = user32.GetForegroundWindow()
    vk = VK.get(name) or ord(name.upper()); sc = user32.MapVirtualKeyW(vk, 0)
    ok = True
    try:
        if not front(h): raise SystemExit("could not focus the game")
        time.sleep(0.03)
        user32.keybd_event(vk, sc, 0, 0)
        end = time.time() + ms / 1000
        while time.time() < end:
            if user32.GetForegroundWindow() != h: ok = False; break
            time.sleep(0.01)
        user32.keybd_event(vk, sc, 2, 0)
        time.sleep(0.05)
    finally:
        if prev and prev != h: front(prev)
    return ok, h

def shot(path):
    with urllib.request.urlopen("http://127.0.0.1:8791/api/screenshot.png", timeout=15) as r:
        open(path, "wb").write(r.read())

if __name__ == "__main__":
    args = sys.argv[1:]
    out = args[args.index("--shot") + 1] if "--shot" in args else None
    if "--park" in args:
        h = game_hwnd(json.load(open(STATE))["gamePid"]); park(h)
    pos = [a for a in args if not a.startswith("--") and a != out]
    if pos:
        ok, _ = press(pos[0], int(pos[1]) if len(pos) > 1 else 150)
        print("pressed", pos[0], "" if ok else "(focus lost)")
    if out:
        time.sleep(1.2); shot(out); print("shot", out)
