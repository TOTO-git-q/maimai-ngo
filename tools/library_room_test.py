"""In-game check of the partner room on the frame library (owner 2026-11-02 night: "搭档房间也换"), off screen and muted:
play the credit (each track started and skipped) until the end-of-credit collection, open the partner genre and the
partner room (as ngo_collection_check.py), then the greeting as the room opens, touches on sensors over the pair (who
reacted is in the mod log: "room: touch B4 at (x, y) on the pair: K"), three quick touches on Ame (poke) and a present
(POST test/roompresent: what the room does after a present, without spending the owner's presents). Saves screenshots,
the room view's library numbers and the room's lines of the mod log; logs out and stops the game.
usage: python tools/library_room_test.py <out>"""
import json, os, re, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phase_round_lib as lib
from phase_round_lib import screen, press, get, say_to
import ngo_to_collection_lib as tc

OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
say = say_to(os.path.join(OUT, "room.log"))
LOG = r"D:\maimai\Package\MelonLoader\Latest.log"
ICON = "ngo_pair/icon.png"
TOUCHES = ["C1", "B4", "B5", "E1", "B3", "B6"]      # over the pair in the room; the log says who each one hit


def post(path, body):
    import urllib.request
    req = urllib.request.Request(lib.API + path, data=json.dumps(body).encode(), method="POST",
                                 headers={"X-PartnerLive2D": "1", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=10) as r: return json.loads(r.read() or b"null")
    except Exception as e: return {"error": str(e)}


def grab(tag):
    png = get("screenshot.png", raw=True)
    if png: open(os.path.join(OUT, tag + ".png"), "wb").write(png)


def room_view():
    for v in (get("views") or {}).get("views", []):
        if v.get("context") == "room": return v
    return None


def room_lines():
    try:
        with open(LOG, encoding="utf-8", errors="ignore") as f: return [l for l in f.read().splitlines() if " room: " in l]
    except OSError: return []


def hits():
    """sensor -> who, from the mod log's touch lines"""
    found = {}
    for l in room_lines():
        m = re.search(r"touch (\w\d) at \(.*\) on the pair: (\w+)", l)
        if m: found[m.group(1)] = m.group(2)
    return found


def partner_genre():
    # as ngo_collection_check.py: step the genre carousel (Z left, D right) until the partner card (title _05) is centred
    lib.K.setdefault("Z", 0x5A)
    for step in range(9):
        items = (get("test/ui") or {}).get("items", [])
        titles = sorted([i for i in items if "ContentsTitle" in i.get("sprite", "")], key=lambda i: -i["w"])
        if not titles: time.sleep(1); continue
        centre = titles[0]; part = [i for i in titles if i["sprite"].endswith("_05")]
        if centre["sprite"].endswith("_05"): return True
        if part: key = "Z" if part[0]["x"] < centre["x"] else "D"
        else: key = "Z" if centre["sprite"].endswith(("_Shop", "_07")) and any(i["sprite"].endswith("_Shop") for i in titles) else "D"
        press(key); time.sleep(0.9)
    return False


try:
    tc.start_and_login(say)
    tc.play_until_collection(say)
    say("collection reached")
    if not partner_genre(): raise SystemExit("partner card never reached the centre")
    press("C"); time.sleep(2.0); grab("r00_partner_tab")
    say("touch B4 (opens the room):", post("test/touch", {"area": "B4"}))
    t = time.time()
    while time.time() - t < 8 and room_view() is None: time.sleep(0.25)
    v = room_view(); say("room view:", json.dumps(v)[:400] if v else None)
    time.sleep(1.2); grab("r01_greeting"); time.sleep(3.5); grab("r02_after_greeting")
    for name in TOUCHES:
        say("touch", name, post("test/touch", {"area": name})); time.sleep(1.0); grab("r03_touch_" + name); time.sleep(2.6)
    who = hits(); say("who each sensor hit:", who)
    ame = [s for s, w in who.items() if w == "Ame"]
    if ame:
        time.sleep(1.5)
        for k in range(3): say("poke", ame[0], k + 1, post("test/touch", {"area": ame[0]})); time.sleep(.35)
        time.sleep(1.5); grab("r04_poke_Ame"); time.sleep(3)
    else: say("no sensor hit Ame: poke skipped")
    say("present:", post("test/roompresent", {})); time.sleep(1.5); grab("r05_present"); time.sleep(3)
    v = room_view(); json.dump(v, open(os.path.join(OUT, "room_view.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    say("room library:", json.dumps((v or {}).get("library"))[:600])
    press("X"); time.sleep(1.5)   # leave the room (never C here: C on a list entry equips it)
    open(os.path.join(OUT, "mod.log"), "w", encoding="utf-8").write("\n".join(room_lines()))
    tc.logout(say)
finally:
    tc.stop(say)
