"""Calibrate the select page's partner layout against the screen (test game, off screen, muted; runs on a locked desktop).
The partner rectangle's top is cut by the circle, so a fill capture alone cannot say where the rectangle really is
(ngo_select_backdrop.py took the cut edge as the top). Here, at the Music phase, through POST music-select/layout
(not saved):
  calibA / calibB: small rectangles that sit fully inside the open dome, filled white and black -> the exact mapping
                   layout units -> screenshot pixels (x and y separately);
  cover:           one rectangle larger than the circle, cleared / black / white -> what the page shows behind the pair
                   and what covers it, everywhere (a backdrop for any layout: v2/layout_try.py, v2/render.py).
Writes <out>/<name>_{clear,black,white}.png and calib.json, restores nothing on disk (nothing was saved), stops the game.
usage: python tools/select_layout_calib.py <out dir>"""
import json, os, subprocess, sys, time, urllib.request
os.environ.setdefault('DRIVE_WINDOW', '1080x1920')
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phase_round_lib as lib
from phase_round_lib import screen, press, DRIVE

API = "http://127.0.0.1:8791/api/"
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
SETS = {'calibA': {'bottomX': 0, 'bottomY': 120, 'height': 300},
        'calibB': {'bottomX': -150, 'bottomY': 60, 'height': 200},
        'cover': {'bottomX': 0, 'bottomY': -700, 'height': 1400}}


def get(path, raw=False):
    try:
        with urllib.request.urlopen(API + path, timeout=10) as r: b = r.read()
        return b if raw else json.loads(b)
    except Exception: return None


def post(path, body):
    req = urllib.request.Request(API + path, json.dumps(body).encode(), {"Content-Type": "application/json", "X-PartnerLive2D": "1"})
    with urllib.request.urlopen(req, timeout=10) as r: return json.load(r)


def phase():
    d = get("music-select") or {}
    for x in d.get("sessions", []):
        for s in x.get("sides", []):
            if s.get("phase"): return s["phase"]
    return None


def main():
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
    pid = lib.game_pid(); print("game", pid, flush=True)
    t0 = time.time()
    while not get("test/processes") and time.time() - t0 < 240: time.sleep(2)
    al = subprocess.Popen([sys.executable, r"D:\maimai\auto-login.py", str(pid)])
    try:
        t0 = time.time()
        while time.time() - t0 < 400:
            s, p = screen(), phase()
            if s == "MusicSelectProcess" and p == "Music": break
            if s in ("AdvertiseProcess", "EntryProcess", "FadeProcess", "PleaseWaitProcess", "") or s.startswith("TrackStart") or "PowerOn" in s:
                time.sleep(2); continue
            if s == "ContinueProcess": press("X"); time.sleep(3); continue
            if "Photo" in s: press("X"); time.sleep(4); continue
            press("C"); time.sleep(3)
    finally:
        if al.poll() is None: al.kill()
    print("at", screen(), phase(), flush=True)
    if phase() != "Music": return
    time.sleep(4)
    vid = next((v['id'] for v in (get('views') or {}).get('views', []) if v.get('context') == 'musicSelect'), None)
    before = (get("music-select") or {}).get("preset", {}).get("partner")
    info = {"before": before, "sets": SETS}
    for name, part in SETS.items():
        post("music-select/layout", {"partner": part}); time.sleep(1.0)
        for fill, color in (('clear', '#00000000'), ('black', '#000000FF'), ('white', '#FFFFFFFF')):
            if name != 'cover' and fill == 'clear': continue
            post(f'views/{vid}/fill', {'color': color}); time.sleep(0.4)
            png = get('screenshot.png', raw=True)
            open(os.path.join(OUT, f'{name}_{fill}.png'), 'wb').write(png); print(name, fill, len(png), flush=True)
        post(f'views/{vid}/fill', {'color': ''})
    post("music-select/layout", {"partner": before}); time.sleep(0.5)
    json.dump(info, open(os.path.join(OUT, 'calib.json'), 'w'), indent=1)


try:
    main()
finally:
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
