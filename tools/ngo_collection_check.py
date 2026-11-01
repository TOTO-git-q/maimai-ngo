"""Off-screen, muted check of the custom partner in the end-of-credit collection: play the credit (each track started
and skipped) until CollectionProcess, step the genre carousel (button 3) until the partner icon (texture
"<partner folder>/icon.png", named by Mod.LoadImage) sits on the centre card, open it (button 4), then grab the
partner tab and the partner room and save the room view's state. The timer of that screen is short, so every step is
automatic. Logs out and stops the game at the end.
usage: python tools/ngo_collection_check.py <out> [partner folder name, default ngo_pair]"""
import json, os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phase_round_lib as lib
from phase_round_lib import screen, press, get, say_to
import ngo_to_collection_lib as tc

OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
ICON = (sys.argv[2] if len(sys.argv) > 2 else "ngo_pair") + "/icon.png"
say = say_to(os.path.join(OUT, "check.log"))


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


_dumps = [0]
def icon_items():
    d = get("test/ui") or {}
    _dumps[0] += 1
    json.dump(d, open(os.path.join(OUT, "ui_%02d.json" % _dumps[0]), "w", encoding="utf-8"), ensure_ascii=False)
    return [i for i in d.get("items", []) if i.get("texture") == ICON]


try:
    tc.start_and_login(say)
    tc.play_until_collection(say)
    say("collection reached"); grab("c00_start")
    # Genre carousel: button 6 (Z) = left, button 3 (D) = right, no wrap. Every genre card carries a title sprite;
    # UI_CLC_ContentsTitle_05 is the partner genre (order left->right: 07 exit-left, 03 title, 01 icon, 02 plate,
    # 06 frame, 05 partner, _Shop, 07 exit). The centre card has the widest title. The partner card itself shows no
    # partner picture: the icons and the partner room appear only after entering the genre (button 4 = C).
    lib.K.setdefault("Z", 0x5A)
    found = False
    for step in range(9):
        items = (get("test/ui") or {}).get("items", [])
        titles = sorted([i for i in items if "ContentsTitle" in i.get("sprite", "")], key=lambda i: -i["w"])
        if not titles: time.sleep(1); continue
        centre = titles[0]; part = [i for i in titles if i["sprite"].endswith("_05")]
        say("step", step, "centre", centre["sprite"], "partner card", [(i["x"], i["w"]) for i in part])
        if centre["sprite"].endswith("_05"):
            found = True; break
        if part: key = "Z" if part[0]["x"] < centre["x"] else "D"
        else: key = "Z" if centre["sprite"].endswith(("_Shop", "_07")) and any(i["sprite"].endswith("_Shop") for i in titles) else "D"
        press(key); time.sleep(0.9)
    grab("c01_partner_genre")
    if found:
        press("C"); time.sleep(2.0); grab("c02_partner_tab")
        say("our icon on screen:", [(i["x"], i["w"]) for i in icon_items()])
        # the partner room (present screen) opens on a touch of the centre card's button area (B4/B5)
        say("touch B4:", post("test/touch", {"area": "B4"}))
        time.sleep(3.0); grab("c03_partner_room")
        time.sleep(3.0); grab("c04_partner_room_later")
        json.dump(get("views"), open(os.path.join(OUT, "views.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        json.dump(get("test/ui"), open(os.path.join(OUT, "ui_partner_tab.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        press("X"); time.sleep(1.0)   # back to the carousel (never C again here: C on a list entry equips it)
    else:
        say("partner icon never reached the centre card")
    tc.logout(say)
finally:
    tc.stop(say)
