"""Off-screen, muted: three full-size screenshots of the music select screen for compositing previews of the partner
exactly as the game layers it - the partner rectangle cleared (what is behind the pair), filled black and filled white
(what covers it, and how much).  Uses the test-only view action POST /api/views/<id>/fill.
Logs in with the card in DEVICE\\aime.txt, logs out, stops the game.
usage: python tools/ngo_select_backdrop.py <out dir> [window=1080x1920]"""
import json, os, sys, time, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
os.environ['DRIVE_WINDOW'] = sys.argv[2] if len(sys.argv) > 2 else '1080x1920'
import ngo_to_collection_lib as run
from phase_round_lib import phase, press, get, say_to, API

OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
say = say_to(os.path.join(OUT, 'backdrop.log'))


def post(path, body):
    req = urllib.request.Request(API + path, json.dumps(body).encode(), {'Content-Type': 'application/json', 'X-PartnerLive2D': '1'})
    with urllib.request.urlopen(req, timeout=10) as r: return json.load(r)


try:
    run.start_and_login(say)
    for _ in range(6):
        if phase() == 'Music': break
        press('C'); time.sleep(1.5)
    t = time.time(); vid = None
    while vid is None and time.time() - t < 30:
        vid = next((v['id'] for v in (get('views') or {}).get('views', []) if v.get('context') == 'musicSelect'), None)
        time.sleep(0.5)
    if vid is None: raise SystemExit('no music select partner view')
    time.sleep(4)                                            # let the screen's own entry animations finish
    for name, color in (('clear', '#00000000'), ('black', '#000000FF'), ('white', '#FFFFFFFF')):
        post(f'views/{vid}/fill', {'color': color}); time.sleep(0.25)
        png = get('screenshot.png', raw=True)
        open(os.path.join(OUT, name + '.png'), 'wb').write(png); say(name, len(png))
    post(f'views/{vid}/fill', {'color': ''})
    run.logout(say)
finally:
    run.stop(say)
