"""Off-screen, muted check of the 2026-11-01 evening fixes on the music select: the NGO pair's in-between drawing
blend (partner frames grabbed back to back, with the frame-to-frame change printed so a dissolve shows as several
small steps instead of one jump) and the pair easing down under the difficulty header in the Difficulty phase and back
up in the Menu phase (full screens at each). Logs in with the card in DEVICE\\aime.txt, saves to <out_dir>, logs out,
stops the game. usage: python tools/ngo_unify_check.py <out_dir>"""
import io, json, os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from PIL import Image
import ngo_to_collection_lib as run
from phase_round_lib import screen, phase, press, get, say_to

OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
say = say_to(os.path.join(OUT, "check.log"))


def shot(tag):
    png = get("screenshot.png", raw=True)
    if png: open(os.path.join(OUT, tag + ".png"), "wb").write(png)


def select_view():
    for v in (get("views") or {}).get("views", []):
        if v.get("context") == "musicSelect": return v
    return None


try:
    run.start_and_login(say)
    t = time.time(); v = None
    while not v and time.time() - t < 30: v = select_view(); time.sleep(0.5)
    if not v: raise SystemExit("no music select partner view")
    vid = v["id"]
    t = time.time()
    while time.time() - t < 60:   # the pair's 600-odd textures load over the first ~25 s
        d = get("views/%d" % vid) or {}
        if d.get("loadedTextures") and d.get("loadedTextures") == d.get("totalTextures"): break
        run.park(); time.sleep(1)
    say("view", json.dumps({k: d.get(k) for k in ("state", "loadedTextures", "totalTextures")}))
    # blend: 40 partner frames back to back
    frames, stamps = [], []
    for k in range(40):
        png = get("views/%d/frame.png" % vid, raw=True)
        if png: frames.append(np.asarray(Image.open(io.BytesIO(png)).convert("RGBA")).astype(np.int16)); stamps.append(time.time())
    steps = [float(np.abs(frames[i] - frames[i - 1]).mean()) for i in range(1, len(frames))]
    say("grab interval ms", round(1000 * (stamps[-1] - stamps[0]) / max(1, len(stamps) - 1)))
    say("frame-to-frame change", " ".join("%.2f" % s for s in steps))
    for i, f in enumerate(frames[:16]): Image.fromarray(f.astype(np.uint8)).save(os.path.join(OUT, "blend_%02d.png" % i))
    # phases: Music (as tuned), Difficulty (eased down), Menu (back up)
    for _ in range(6):
        p = phase()
        if p == "Music": break
        press("C"); time.sleep(1.5)
    time.sleep(1); shot("phase_music"); say("phase", phase())
    press("C"); t0 = time.time()
    while phase() != "Difficulty" and time.time() - t0 < 5: time.sleep(0.05)
    say("phase", phase(), "after %.2f s" % (time.time() - t0))
    time.sleep(0.08); shot("phase_difficulty_0s1")
    time.sleep(0.8); shot("phase_difficulty_1s")
    press("C"); time.sleep(1.2); shot("phase_menu"); say("phase", phase())
    run.logout(say)
finally:
    run.stop(say)
