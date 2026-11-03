"""Record a demo of the pair in the real game (owner 2026-11-02 night: "顺便录制一遍完整的演示给我看看"), off screen and
muted, with the mod's test recorder (POST test/record: the back buffer every frame, 60 fps, by real time):
  part 1  the select page (greeting, touches, a poke, song / category / Master reactions, NEXT, NEXT, START), the skipped
          chart's result (the real reaction, then Top / Good / Clear through the view's motion route, a tap) and the
          return to the select page;
  part 2  (tracks 2-4 played through unrecorded) the end-of-credit collection: the partner room (greeting, touches, a
          poke, a present through POST test/roompresent - no presents of the owner are spent).
Every step is logged with its time in the part (captions.json) for the captions. The display is kept on meanwhile
(SetThreadExecutionState): with the desktop locked and the display off the game drops to 30 frames a second.
usage: python tools/demo_record.py <out dir> [room sensor over K] [room sensor over Ame] [--part1-only]"""
import ctypes, json, os, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phase_round_lib as lib
from phase_round_lib import screen, phase, press, get, say_to, DRIVE
import ngo_to_collection_lib as tc

OUT = os.path.abspath(sys.argv[1]); os.makedirs(OUT, exist_ok=True)
ROOM_K = sys.argv[2] if len(sys.argv) > 2 else "C1"
ROOM_AME = sys.argv[3] if len(sys.argv) > 3 else "B5"
FF = r"%USERPROFILE%\AppData\Roaming\Python\Python312\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe"
say = say_to(os.path.join(OUT, "demo.log"))
captions = {"part1": [], "part2": []}
part, t_part = [None], [0.0]


def post(path, body):
    import urllib.request
    req = urllib.request.Request(lib.API + path, data=json.dumps(body).encode(), method="POST",
                                 headers={"X-PartnerLive2D": "1", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=15) as r: return json.loads(r.read() or b"null")
    except Exception as e: return {"error": str(e)}


def record(name):
    r = post("test/record", {"path": os.path.join(OUT, name + ".mp4"), "ffmpeg": FF, "width": 720, "fps": 60, "crf": 21})
    part[0], t_part[0] = name, time.time(); say("record", name, r)


def stop():
    r = post("test/record", {"stop": True}); say("stop", part[0], r)
    t = time.time()
    while time.time() - t < 300:
        s = post("test/record", {})
        if s.get("finished"): say("finished", s); break
        time.sleep(1)
    part[0] = None


def cap(text):
    """a caption from now on (until the next one)"""
    if part[0]: captions[part[0]].append([round(time.time() - t_part[0], 2), text])
    say("caption", text)


def view(context):
    for v in (get("views") or {}).get("views", []):
        if v.get("context") == context: return v
    return None


def idle_both(vid, limit=10):
    t = time.time()
    while time.time() - t < limit:
        li = (get("views/%d" % vid) or {}).get("library") or {}
        if all((li.get(c) or {}).get("action") is None for c in ("K", "Ame")): return
        time.sleep(0.3)


def wait_screen(name, limit):
    t = time.time()
    while screen() != name and time.time() - t < limit: time.sleep(0.4)
    return screen() == name


def part1():
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "start"], check=True)
    pid = lib.game_pid(); say("game", pid)
    t0 = time.time()
    while not get("test/processes") and time.time() - t0 < 180: time.sleep(2)
    # with the laptop's display off the game waits for vertical blanks that come half as often (30 frames a second)
    say("frame rate", post("test/framerate", {"vsync": 0, "fps": 60}))
    al = subprocess.Popen([sys.executable, r"D:\maimai\auto-login.py", str(pid)])
    try:
        t0 = time.time()
        while time.time() - t0 < 300:
            s = screen()
            if s == "MusicSelectProcess": break
            if s in ("AdvertiseProcess", "EntryProcess", "FadeProcess", "PleaseWaitProcess", "") or s.startswith("TrackStart"):
                time.sleep(0.4); continue
            if s == "ContinueProcess": press("X"); time.sleep(3); continue
            if "Photo" in s: press("X"); time.sleep(4); continue
            press("C"); time.sleep(3)
    finally:
        if al.poll() is None: al.kill()
    if screen() != "MusicSelectProcess": raise SystemExit("login did not reach music select: " + screen())
    record("part1"); cap("进入选歌")
    # the page may open on the category step (2026-11-02 run 1: the level categories, "check the mission targets"); the
    # pair's select reactions start with the song list, so choose the category first
    t = time.time()
    while phase() != "Music" and time.time() - t < 4: time.sleep(0.3)
    if phase() != "Music":
        say("phase", phase(), "-> choose the category"); cap("选分类，进入选歌列表"); press("C")
        t = time.time()
        while phase() != "Music" and time.time() - t < 10: time.sleep(0.3)
    if phase() != "Music": raise SystemExit("no song list: phase " + str(phase()))
    cap("选歌列表：两人打招呼")
    v = None; t = time.time()
    while not v and time.time() - t < 10: v = view("musicSelect"); time.sleep(0.3)
    if not v: raise SystemExit("no select view")
    vid = v["id"]
    time.sleep(6); idle_both(vid); cap("待机"); time.sleep(5)
    idle_both(vid); cap("摸超天酱"); post("test/touch", {"area": "B1"}); time.sleep(4.5)
    idle_both(vid); cap("摸糖糖"); post("test/touch", {"area": "A1"}); time.sleep(4.5)
    idle_both(vid); cap("连戳糖糖 3 下")
    for _ in range(3): post("test/touch", {"area": "A1"}); time.sleep(.35)
    time.sleep(4.5)
    cap("左右换歌"); press("D"); time.sleep(2.5); press("D"); time.sleep(3)
    idle_both(vid); cap("换分类"); post("views/%d/motion" % vid, {"group": "category"}); time.sleep(4.5)
    idle_both(vid); cap("难度切到 Master"); post("views/%d/motion" % vid, {"group": "master"}); time.sleep(4.5)
    idle_both(vid); cap("倒计时剩 10 秒"); post("views/%d/motion" % vid, {"group": "countdown"}); time.sleep(4.5)
    idle_both(vid); cap("选难度"); press("C"); time.sleep(3.5)
    cap("确认选歌"); press("C"); time.sleep(4.5)
    say("select library", json.dumps((get("views/%d" % vid) or {}).get("library"))[:500])
    cap("开始游戏"); press("C")
    if not wait_screen("GameProcess", 90): say("no GameProcess:", screen()); return
    cap("（谱面跳过）"); time.sleep(3); press("Service", 2500)
    if not wait_screen("ResultProcess", 60): say("no ResultProcess:", screen()); return
    rv, t = None, time.time()
    while not rv and time.time() - t < 15: rv = view("result"); time.sleep(0.4)
    cap("结算：跳过的谱面算没过，两人对 Fail 的反应")
    if rv:
        rid = rv["id"]; time.sleep(8)
        for tier, text in (("Top", "结算：Top（AP 或 100%）时的反应"), ("Good", "结算：97% 以上时的反应"), ("Clear", "结算：过了时的反应")):
            idle_both(rid); cap(text); post("views/%d/motion" % rid, {"group": "result" + tier}); time.sleep(4.5)
        idle_both(rid); cap("结算页摸她们"); post("views/%d/tap" % rid, {}); time.sleep(5)
        say("result library", json.dumps((get("views/%d" % rid) or {}).get("library"))[:500])
    t = time.time()
    while screen() != "MusicSelectProcess" and time.time() - t < 90:
        if screen() == "ResultProcess": press("C")
        time.sleep(1.5)
    cap("回到选歌（第 2 首）：两人的反应"); time.sleep(8)
    stop()


def part2():
    tc.play_until_collection(say)
    if screen() != "CollectionProcess": raise SystemExit("no collection: " + screen())
    lib.K.setdefault("Z", 0x5A)
    for step in range(9):
        items = (get("test/ui") or {}).get("items", [])
        titles = sorted([i for i in items if "ContentsTitle" in i.get("sprite", "")], key=lambda i: -i["w"])
        if not titles: time.sleep(1); continue
        centre = titles[0]; part_ = [i for i in titles if i["sprite"].endswith("_05")]
        if centre["sprite"].endswith("_05"): break
        if part_: key = "Z" if part_[0]["x"] < centre["x"] else "D"
        else: key = "Z" if centre["sprite"].endswith(("_Shop", "_07")) and any(i["sprite"].endswith("_Shop") for i in titles) else "D"
        press(key); time.sleep(0.9)
    press("C"); time.sleep(2.0)
    record("part2"); cap("收藏：打开搭档房间")
    post("test/touch", {"area": "B4"})
    t = time.time()
    while not view("room") and time.time() - t < 8: time.sleep(0.25)
    v = view("room"); rid = v["id"] if v else None
    time.sleep(0.8); cap("搭档房间：进门打招呼"); time.sleep(6)
    if rid: idle_both(rid)
    cap("搭档房间：摸超天酱"); post("test/touch", {"area": ROOM_K}); time.sleep(5)
    if rid: idle_both(rid)
    cap("搭档房间：摸糖糖"); post("test/touch", {"area": ROOM_AME}); time.sleep(5)
    if rid: idle_both(rid)
    cap("搭档房间：连戳糖糖 3 下")
    for _ in range(3): post("test/touch", {"area": ROOM_AME}); time.sleep(.35)
    time.sleep(5)
    if rid: idle_both(rid)
    cap("搭档房间：送礼物"); post("test/roompresent", {}); time.sleep(6)
    cap("待机"); time.sleep(4)
    if rid: say("room library", json.dumps((get("views/%d" % rid) or {}).get("library"))[:500])
    stop()
    press("X"); time.sleep(1.5)
    tc.logout(say)


ES = ctypes.windll.kernel32.SetThreadExecutionState
ES(0x80000000 | 0x00000002 | 0x00000001)          # ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED
try:
    part1()
    if "--part1-only" in sys.argv: tc.logout(say)
    else: part2()
finally:
    if part[0]: stop()
    json.dump(captions, open(os.path.join(OUT, "captions.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    ES(0x80000000)
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "stop"])
