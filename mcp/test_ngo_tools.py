"""Offline tests for the extension tools (ngo_api.py, ngo_jobs.py) against a FAKE game folder and a FAKE project
(mcp\\_testngo). Never touches the real game folder, the real tools\\drive-state.json or the real settings.ini, and
never starts the game: the "game scripts" here are stand-ins written by this test.

Run:  python test_ngo_tools.py
"""
from __future__ import annotations

import json
import os
import shutil
import socket
import sys
import threading
import time
import urllib.parse
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parent
ROOT = HERE / "_testngo"
GAME = ROOT / "game"
PKG = GAME / "Package"
FAKEPROJ = ROOT / "project"
STATE = ROOT / "state"
ASSETS = PKG / "LocalAssets" / "PartnerLive2D"
PNG = b"\x89PNG\r\n\x1a\n" + b"fakepng"

FAILS: list[str] = []


def check(cond: bool, msg: str) -> None:
    print(("PASS  " if cond else "FAIL  ") + msg)
    if not cond:
        FAILS.append(msg)


def free_port() -> int:
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    p = s.getsockname()[1]
    s.close()
    return p


# --------------------------------------------------------------------------------------
# fake game folder + fake project (tools\ with stand-in scripts)
# --------------------------------------------------------------------------------------

FAKE_SCRIPT = r'''
import os, sys, time, json
out = sys.argv[1]
os.makedirs(out, exist_ok=True)
settings = os.environ["FAKE_SETTINGS"]
orig = open(settings, "rb").read()
open(settings, "ab").write(b"testInput = true\r\nframeLog = true\r\n")
open(os.path.join(out, "args.json"), "w").write(json.dumps({"argv": sys.argv, "PLAY": os.environ.get("PLAY"),
                                                            "WALK": os.environ.get("WALK"), "CHECK_DT": os.environ.get("CHECK_DT")}))
print("fake script running", flush=True)
stubborn = os.environ.get("FAKE_STUBBORN") == "1"
seconds = float(os.environ.get("FAKE_SECONDS", "60"))
t = time.time()
try:
    while time.time() - t < seconds:
        try:
            time.sleep(0.2)
        except KeyboardInterrupt:
            if not stubborn:
                raise
            print("ignoring the stop request", flush=True)
finally:
    if not stubborn:
        open(settings, "wb").write(orig)
        print("settings restored by the script", flush=True)
'''

DRIVE_STUB = r'''# stand-in for tools\drive.ps1 in the MCP tests. ASCII only.
param([Parameter(Position = 0)][string]$Cmd, [string]$Package = '')
$state = Join-Path $PSScriptRoot 'drive-state.json'
if ($Cmd -eq 'stop') {
    Set-Content -Path (Join-Path $PSScriptRoot 'drive-stop-called.txt') -Value 'stop'
    if (Test-Path $state) { Remove-Item $state }
    Write-Host 'stub stopped'
}
'''


def build() -> None:
    if ROOT.exists():
        shutil.rmtree(ROOT)
    ASSETS.mkdir(parents=True)
    (ASSETS / "settings.ini").write_bytes(b"selectView = true\r\nselectPartner = equipped\r\n")
    (ASSETS / "select_layout.ini").write_text("# preset\r\nlayout = {}\r\n", encoding="utf-8")
    # a partner folder for set_partner_placement (offline path) and select_tune result checks
    shutil.copytree(PROJECT / "deploy" / "LocalAssets" / "PartnerLive2D" / "hiyori", ASSETS / "hiyori")
    (ASSETS / "ngo_pair").mkdir()
    (ASSETS / "ngo_pair" / "partner.ini").write_text("model = model/x.model3.json\nnaviCharaId = 91\npartnerId = 502\nscale = 0.9\n", encoding="utf-8")
    # MelonLoader log of one "session" + the music title + the launcher's monitor CSV
    logs = PKG / "MelonLoader" / "Logs"
    logs.mkdir(parents=True)
    lines = [
        "[10:00:00.000] [PartnerLive2D] [proc] + Process.MusicSelectProcess",
        "[10:00:05.000] [PartnerLive2D] [proc] + Process.FadeProcess",
        "[10:00:06.500] [PartnerLive2D] [proc] + Process.GameProcess",
        "[10:02:06.000] [PartnerLive2D] [play] music 11831 diff 3: 120.0 s, 7200 frames (60.0 fps), 3 over 25 ms, 1 over 50 ms, worst 61.0 ms, load 480 ms, gc 0",
        "[10:02:07.000] [PartnerLive2D] [proc] + Process.FadeProcess",
        "[10:02:08.000] [PartnerLive2D] [proc] + Process.ResultProcess",
        "[10:02:09.000] [PartnerLive2D] [clean] unload+GC 320 ms, heap 330 MB",
        "[10:02:30.000] [PartnerLive2D] [proc] + Process.NextTrackProcess",
        "[10:02:31.600] [PartnerLive2D] [proc] + Process.MusicSelectProcess",
    ]
    (logs / "26-11-1_10-0-0.log").write_text("\n".join(lines) + "\n", encoding="utf-8")
    music = PKG / "Sinmai_Data" / "StreamingAssets" / "A000" / "music" / "music011831"
    music.mkdir(parents=True)
    (music / "Music.xml").write_text("<?xml version=\"1.0\"?><MusicData><name><id>11831</id><str>FakeSong</str></name></MusicData>", encoding="utf-8")
    pl = GAME / "playlogs"
    pl.mkdir()
    (pl / ("monitor_" + time.strftime("%Y%m%d") + "_095959.csv")).write_text(
        "time,private_mb,working_set_mb,cpu_pct,gpu_mhz,gpu_util_pct,gpu_temp_c,gpu_pstate,gpu_slowdown\n"
        "10:00:04,3500,3000,20.0,1800,40,55,P0,0\n10:00:09,3550,3020,22.0,1800,45,56,P0,0\n", encoding="ascii")
    (GAME / "auto-login.py").write_text("# stand-in\n", encoding="ascii")
    # fake project: the real report scripts + stand-ins for the game scripts and drive.ps1
    tools = FAKEPROJ / "tools"
    tools.mkdir(parents=True)
    for f in ("ngo_play_report.py", "ngo_heapwalk_diff.py"):
        shutil.copy(PROJECT / "tools" / f, tools / f)
    for f in ("ngo_soak.py", "ngo_select_tune.py", "ngo_to_collection.py", "ngo_attract_check.py"):
        (tools / f).write_text(FAKE_SCRIPT, encoding="utf-8")
    (tools / "drive.ps1").write_text(DRIVE_STUB, encoding="ascii")
    # two heap walks in the state folder
    hw = STATE / "heapwalk"
    hw.mkdir(parents=True)
    (hw / "walk_20261101_100000.tsv").write_text("kind\tname\tcount\tbytes\nmeta\theap_after_gc\t0\t300000000\nmeta\treached\t10\t200000000\n"
                                                 "type\tSystem.String\t100\t1000\nroot\tstatic A.B\t5\t1000\n", encoding="utf-8")
    time.sleep(0.05)
    (hw / "walk_20261101_101000.tsv").write_text("kind\tname\tcount\tbytes\nmeta\theap_after_gc\t0\t350000000\nmeta\treached\t20\t260000000\n"
                                                 "type\tSystem.String\t200\t60000000\nroot\tstatic A.B\t9\t52428800\n", encoding="utf-8")


# --------------------------------------------------------------------------------------
# stub of the mod's control API
# --------------------------------------------------------------------------------------

class Stub(BaseHTTPRequestHandler):
    seen: list[tuple[str, str, dict, object]] = []

    def log_message(self, *a):  # noqa: ANN002
        pass

    def _reply(self, code: int, obj=None, png=False):
        body = PNG if png else json.dumps(obj).encode()
        self.send_response(code)
        self.send_header("Content-Type", "image/png" if png else "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):  # noqa: N802
        u = urllib.parse.urlparse(self.path)
        q = dict(urllib.parse.parse_qsl(u.query))
        Stub.seen.append(("GET", u.path, q, None))
        p = u.path
        if p == "/api/theme":
            return self._reply(200, {"ok": True, "enabled": False, "folder": None, "loaded": [], "missed": [], "errors": {}})
        if p == "/api/music-select":
            return self._reply(200, {"ok": True, "preset": {"enabled": True}, "failure": None, "sessions": []})
        if p == "/api/music-select/tree":
            return self._reply(200, {"ok": True, "player": 0, "depth": int(q.get("depth", 3)), "tree": {"name": "Main", "q": q}})
        if p == "/api/music-select/sprite.png":
            return self._reply(200, png=True) if q.get("path") else self._reply(400, {"ok": False, "error": "path"})
        if p == "/api/screenshot.png":
            return self._reply(200, png=True)
        if p == "/api/partners/ngo_pair":
            return self._reply(200, {"ok": True, "name": "ngo_pair", "scale": 0.9, "roomScale": 1.0})
        if p == "/api/test/processes":
            procs = [{"i": 0, "name": "Process.AdvertiseProcess", "t": 1.0}, {"i": 1, "name": "Process.FadeProcess", "t": 2.0},
                     {"i": 2, "name": "Process.EntryProcess", "t": 3.0}, {"i": 3, "name": "Process.FadeProcess", "t": 4.0}]
            return self._reply(200, {"ok": True, "count": 4, "processes": procs, "testInput": False})
        if p == "/api/test/entry":
            return self._reply(200, {"ok": True, "state": "ConfirmPlay", "mode": None})
        if p == "/api/test/quicklogin":
            return self._reply(200, {"ok": True, "count": 2})
        if p == "/api/test/music":
            return self._reply(200, {"ok": True, "find": q.get("find")})
        if p == "/api/test/ui":
            items = [{"sprite": "UI_A", "texture": "T_A", "path": "X/UI_A"}, {"sprite": "UI_B", "texture": "T_B", "path": "X/UI_B"},
                     {"sprite": "Board", "texture": "UI_A_tex", "path": "Y/Board"}]
            return self._reply(200, {"ok": True, "screen": "1080x1920", "items": items})
        if p == "/api/test/tree":
            return self._reply(200, {"ok": True, "name": q.get("of"), "q": q})
        if p == "/api/test/renderers":
            return self._reply(200, {"ok": True, "renderers": [{"path": "UI_CMN_BG/x"}], "cameras": []})
        if p == "/api/test/selectperf":
            return self._reply(200, {"ok": True, "apply": {"calls": 3}, "q": q})
        self._reply(404, {"ok": False, "error": "not found: " + p})

    def do_POST(self):  # noqa: N802
        n = int(self.headers.get("Content-Length") or 0)
        body = json.loads(self.rfile.read(n) or b"{}")
        u = urllib.parse.urlparse(self.path)
        Stub.seen.append(("POST", u.path, {"hdr": self.headers.get("X-PartnerLive2D")}, body))
        if self.headers.get("X-PartnerLive2D") != "1":
            return self._reply(403, {"ok": False, "error": "missing header"})
        p = u.path
        if p == "/api/test/heapwalk":
            hw = ASSETS / "heapwalk"
            hw.mkdir(exist_ok=True)
            f = hw / "walk_101500.tsv"
            f.write_text("kind\tname\tcount\tbytes\nmeta\theap_after_gc\t0\t1\n", encoding="utf-8")
            return self._reply(200, {"ok": True, "file": str(f), "ms": 5, "topRoots": []})
        if p == "/api/test/autoplay":
            return self._reply(409, {"ok": False, "error": "testInput is off (settings.ini testInput = true)"})
        self._reply(200, {"ok": True, "path": p, "body": body})


# --------------------------------------------------------------------------------------

def main() -> int:
    dead_port = free_port()
    build()
    (ASSETS / "settings.ini").write_bytes(b"selectView = true\r\nselectPartner = equipped\r\nport = %d\r\n" % dead_port)
    os.environ.update({"PARTNERLIVE2D_PACKAGE": str(PKG), "PARTNERLIVE2D_PROJECT": str(FAKEPROJ),
                       "PARTNERLIVE2D_STATE": str(STATE), "FAKE_SETTINGS": str(ASSETS / "settings.ini")})
    os.environ.pop("PARTNERLIVE2D_PORT", None)
    sys.path.insert(0, str(HERE))
    import server as S  # noqa: E402
    import ngo_api as A  # noqa: E402
    import ngo_common as C  # noqa: E402
    import ngo_jobs as J  # noqa: E402

    try:
        check(C.core is S, "extensions bound to the server module")
        check(callable(A.theme) and callable(J.job_status), "extension tools are plain callables")

        # ---- game not running: every live tool answers with a clear error ----
        live = [A.theme(), A.theme(True), A.music_select_info(), A.music_select_layout({"partner": {"height": 800}}),
                A.music_select_node("Null_X/Y", {"hidden": True}), A.music_select_image("board", {"x": 1}),
                A.music_select_reset(confirm=True), A.music_select_tree(), A.music_select_sprite("A/B"),
                A.get_live_partner("ngo_pair"), A.test_screen_state(), A.test_music_probe(11831), A.test_ui_dump(),
                A.test_tree("Main"), A.test_renderers(), A.test_select_perf()]
        check(all(isinstance(r, dict) and r.get("ok") is False and r.get("game_running") is False for r in live),
              f"{len(live)} live extension tools report 'game not running'")
        r = A.music_select_save()
        check(r.get("ok") is False and r.get("game_running") is False and Path(r.get("backup", "")).is_dir(),
              "music_select_save backs up first, then reports game not running")
        check((Path(r["backup"]) / "select_layout.ini").exists() and (Path(r["backup"]) / "settings.ini").exists(),
              "backup holds select_layout.ini + settings.ini")
        check(str(STATE) in r["backup"] and str(PKG) not in r["backup"], "backup is outside the game folder")

        # ---- validation (no request made) ----
        check(not A.music_select_layout({})["ok"] and not A.music_select_layout({"save": True})["ok"], "layout: empty / inline save refused")
        check(not A.music_select_node("", {"hidden": True})["ok"] and not A.music_select_node("A", {})["ok"], "node: path / values required")
        check(not A.music_select_image("bad id!", {"x": 1})["ok"] and not A.music_select_image("ok", {"x": 1}, delete=True)["ok"],
              "image: id checked, delete takes no values")
        check(not A.music_select_reset()["ok"], "reset needs confirm=True")
        check(not A.music_select_tree(depth=99)["ok"] and not A.music_select_tree(player=3)["ok"], "tree: depth / player checked")
        check(not A.test_tree("")["ok"] and not A.test_tree("X", depth=31)["ok"] and not A.test_tree("X", up=-1)["ok"], "test_tree validates")
        check(not A.test_autoplay("Fast")["ok"] and not A.test_force_rank("SSS+")["ok"], "autoplay mode / rank name validated")
        check(not A.set_partner_placement("hiyori", "select", scale=1)["ok"], "placement: select screen redirected to the select preset")
        check(not A.set_partner_placement("hiyori", "room", result_under="X")["ok"], "placement: result_under only for result")
        check(not A.set_partner_placement("hiyori", "result")["ok"], "placement: nothing to change refused")

        # ---- set_partner_placement + new partner.ini keys (offline: game not running) ----
        ini = ASSETS / "hiyori" / "partner.ini"
        r = A.set_partner_placement("hiyori", "room", scale=0.8, offset_x=-0.1, offset_y=0.05)
        txt = ini.read_text(encoding="utf-8")
        check(r["ok"] and r["mode"] == "offline" and "roomScale = 0.8" in txt and "roomOffsetX = -0.1" in txt and "roomOffsetY = 0.05" in txt,
              "placement room -> roomScale/roomOffsetX/roomOffsetY written offline")
        r = A.set_partner_placement("hiyori", "result", scale=0.9, result_under="UI_DXRating/Base, UI_MusicData")
        txt = ini.read_text(encoding="utf-8")
        check(r["ok"] and "scale = 0.9" in txt and "resultUnder = UI_DXRating/Base, UI_MusicData" in txt, "placement result + resultUnder written")
        r = S.set_partner_values("hiyori", {"sprites": "sprites/manifest.json"})
        check(r["ok"] and "sprites = sprites/manifest.json" in ini.read_text(encoding="utf-8"), "set_partner_values accepts sprites")
        check(not S.set_partner_values("hiyori", {"roomScale": "big"})["ok"], "roomScale must be a number")

        # ---- guards: which game is running ----
        r = J.soak_test()
        check(not r["ok"] and "written for" in r["error"], "game jobs refuse a package other than the scripts' built-in one")
        C.SCRIPTS_PACKAGE, C.SCRIPTS_PORT = str(PKG), dead_port   # from here on the fake package counts as the scripts' one
        dstate = FAKEPROJ / "tools" / "drive-state.json"
        orig = S._sinmai_running
        try:
            S._sinmai_running = lambda: []
            check("no off-screen test game" in A.test_heapwalk()["error"], "heapwalk without any game refused")
            S._sinmai_running = lambda: [{"pid": 4242, "path": "x"}]
            g = A.test_autoplay("Critical")
            check(not g["ok"] and "did not start" in g["error"], "other game (no drive-state) -> test_autoplay refused")
            check(not A.test_force_rank("Rank_SSSP")["ok"] and not A.test_heapwalk()["ok"], "other game -> rank / heapwalk refused")
            check(not J.test_game_vkeys(["C"])["ok"], "other game -> vkeys refused")
            r = J.soak_test()
            check(not r["ok"], "any game running -> soak refused")
            dstate.write_text(json.dumps({"gamePid": 4242, "injectPid": 1}), encoding="ascii")
            check(C.guard_test_instance("x") is None, "drive-state pid == running pid -> test instance accepted")
            S._sinmai_running = lambda: [{"pid": 4242, "path": "x"}, {"pid": 5151, "path": "x"}]
            check(C.guard_test_instance("x") is not None, "test instance + another game -> refused")
            gp = J.game_processes()
            check(gp["ok"] and gp["testInstanceRunning"] and gp["otherGameRunning"], "game_processes marks test / other")
            r = J._stop_test_game()
            check("not stopped" in r["testGame"] and not (FAKEPROJ / "tools" / "drive-stop-called.txt").exists(),
                  "job cleanup never runs drive.ps1 stop while another game runs")
            S._sinmai_running = lambda: []
            r = J._stop_test_game()
            check("left over" in r["testGame"] and not (FAKEPROJ / "tools" / "drive-stop-called.txt").exists(),
                  "stale drive-state: no drive.ps1 stop on dead PIDs")
            check("earlier test run" in J.to_collection()["error"], "stale drive-state.json blocks a new game job")
        finally:
            S._sinmai_running = orig
            if dstate.exists():
                dstate.unlink()
        check(not J.test_game_vkeys([])["ok"] and not J.test_game_vkeys(["Enter"])["ok"] and not J.test_game_vkeys(["C:5"])["ok"],
              "vkeys validates names / hold")

        # ---- live paths against the stub API ----
        port = free_port()
        (ASSETS / "settings.ini").write_bytes(b"selectView = true\r\nselectPartner = equipped\r\nport = %d\r\n" % port)
        srv = HTTPServer(("127.0.0.1", port), Stub)
        threading.Thread(target=srv.serve_forever, daemon=True).start()
        try:
            check(A.theme()["enabled"] is False, "live: theme status")
            A.theme(False)
            check(Stub.seen[-1][1] == "/api/theme" and Stub.seen[-1][3] == {"enabled": False} and Stub.seen[-1][2]["hdr"] == "1",
                  "live: theme switch body + header")
            r = A.music_select_layout({"partner": {"height": 800, "bottomY": -200}}, save=True)
            check(r["path"] == "/api/music-select/layout" and r["body"] == {"partner": {"height": 800, "bottomY": -200}, "save": True},
                  "live: layout body")
            r = A.music_select_node("Null_GenreControll/SelectorTab(Clone)/Null_Btn_R", {"offsetX": 290, "sprite": None})
            check(r["body"] == {"path": "Null_GenreControll/SelectorTab(Clone)/Null_Btn_R", "offsetX": 290, "sprite": None}, "live: node body (null sprite kept)")
            r = A.music_select_image("board", {"y": -249.2, "siblingIndex": "first"})
            check(r["body"] == {"id": "board", "y": -249.2, "siblingIndex": "first"}, "live: image body")
            check(A.music_select_image("board", delete=True)["body"] == {"id": "board", "delete": True}, "live: image delete body")
            check(A.music_select_reset(confirm=True)["path"] == "/api/music-select/reset", "live: reset")
            r = A.music_select_save()
            check(r["path"] == "/api/music-select/save" and "backup" in r, "live: save + backup")
            r = A.music_select_tree("Null_GenreControll", 2, 0)
            check(r["tree"]["q"] == {"root": "Null_GenreControll", "depth": "2", "player": "0"}, "live: tree query")
            sp = ROOT / "out" / "tree.json"
            r = A.music_select_tree(save_path=str(sp))
            check(r["ok"] and Path(r["saved"]).exists() and "preview" in r and json.loads(sp.read_text("utf-8"))["tree"]["name"] == "Main",
                  "live: tree saved to file")
            img = A.music_select_sprite("A/B", 1, str(ROOT / "out" / "s.png"))
            check(isinstance(img, list) and img[0].data == PNG and (ROOT / "out" / "s.png").read_bytes() == PNG, "live: sprite PNG")
            check(Stub.seen[-1][2] == {"path": "A/B", "player": "1"}, "live: sprite query")
            check(A.get_live_partner("ngo_pair")["roomScale"] == 1.0, "live: get_live_partner")
            r = A.set_partner_placement("ngo_pair", "room", scale=1.1, save=True)
            check(r["mode"] == "live" and r["body"] == {"values": {"roomScale": "1.1"}, "save": True}, "live: placement -> partner config")
            st = A.test_screen_state(2)
            check(st["screen"] == "FadeProcess" and st["lastNonFade"] == "EntryProcess" and st["entry"]["state"] == "ConfirmPlay"
                  and st["quickLoginCount"] == 2 and len(st["recent"]) == 2 and st["testInput"] is False, "live: test_screen_state")
            check(A.test_music_probe(11831)["find"] == "11831" and A.test_music_probe()["find"] is None, "live: music probe query")
            u = A.test_ui_dump("ui_a", 1, str(ROOT / "out" / "ui.json"))
            check(u["total"] == 3 and u["matching"] == 2 and len(u["items"]) == 1 and Path(u["saved"]).exists(), "live: ui dump filter/limit/save")
            t = A.test_tree("PartnerLive2D_ngo_pair", 3, 8)
            check(t["q"] == {"of": "PartnerLive2D_ngo_pair", "up": "3", "depth": "8"}, "live: test tree query")
            check(A.test_renderers()["renderers"][0]["path"] == "UI_CMN_BG/x", "live: renderers")
            check(A.test_select_perf(True)["q"] == {"reset": "1"} and A.test_select_perf()["q"] == {}, "live: selectperf reset flag")
            r = A.test_autoplay("Critical", force=True)
            check(r["ok"] is False and r["http_status"] == 409 and "testInput" in r["error"], "live: autoplay 409 passed through")
            check(A.test_force_rank("Rank_SSSP", force=True)["body"] == {"rank": "Rank_SSSP"}, "live: rank body")
            hw_out = ROOT / "out" / "hw"
            r = A.test_heapwalk(str(hw_out), force=True)
            moved = Path(r.get("movedTo", ""))
            check(r["ok"] and moved.exists() and moved.parent == hw_out and not (ASSETS / "heapwalk").exists(),
                  "live: heap walk moved out of LocalAssets (folder removed)")
            check(moved.name.startswith("walk_2") and moved.name.endswith("_101500.tsv"), f"live: heap walk renamed with date ({moved.name})")
        finally:
            srv.shutdown()
            srv.server_close()
        (ASSETS / "settings.ini").write_bytes(b"selectView = true\r\nselectPartner = equipped\r\nport = %d\r\n" % dead_port)

        # ---- reports ----
        r = J.play_report()
        rep = r.get("report", "")
        check(r["ok"] and "FakeSong" in rep and "Master" in rep and "3500" in rep and "26-11-1_10-0-0.log" in rep,
              "play_report: newest log, song title from the fake package, monitor CSV matched")
        check("1.60" in rep or "1.6" in rep, "play_report: result -> select load time")
        check(not J.play_report(str(ROOT / "nope.log"))["ok"], "play_report: missing log refused")
        r = J.heapwalk_diff()
        check(r["ok"] and "static A.B" in r["diff"] and "+50.00 MB" in r["diff"], "heapwalk_diff: newest two walks, growth listed")
        check(not J.heapwalk_diff(str(ROOT / "a.tsv"), str(ROOT / "b.tsv"))["ok"], "heapwalk_diff: missing files refused")

        # ---- jobs (stand-in scripts; nothing touches a real game) ----
        check(not J.soak_test(minutes=0)["ok"] and not J.soak_test(play_seconds=1)["ok"] and not J.soak_test(heapwalk_after_tracks=[0])["ok"],
              "soak_test validates")
        check(not J.select_tune([])["ok"] and not J.select_tune([{"label": "a", "height": 5}])["ok"]
              and not J.select_tune([{"label": "a b", "height": 800}, {"label": "a c", "height": 900}])["ok"]
              and not J.select_tune([{"label": "x", "width": 1}])["ok"], "select_tune validates candidates")
        check(not J.select_tune([{"label": "x", "height": 800}], [{"label": "r", "nonsense": 1}])["ok"], "select_tune validates result keys")
        check(not J.run_offscreen_test("rm.py")["ok"] and not J.run_offscreen_test("ngo_attract_check.py", env={"PATH": "x"})["ok"],
              "run_offscreen_test: script / env whitelists")

        settings_before = (ASSETS / "settings.ini").read_bytes()
        os.environ["FAKE_SECONDS"] = "60"
        r = J.soak_test(str(ROOT / "out" / "soak"), minutes=2, play_seconds=10, heapwalk_after_tracks=[1, 3])
        check(r["ok"] and r["job"].endswith("-soak"), f"soak job started: {r.get('job')}")
        job = r["job"]
        check("another game job" in J.to_collection()["error"], "second game job refused while one runs")
        dstate.write_text(json.dumps({"gamePid": 999, "injectPid": 1}), encoding="ascii")
        orig = S._sinmai_running
        S._sinmai_running = lambda: [{"pid": 999, "path": "x"}]
        try:
            check("job is driving" in J.test_game_vkeys(["C"])["error"], "vkeys refused while a job drives the game")
        finally:
            S._sinmai_running = orig
            dstate.unlink()
        time.sleep(3)
        s = J.job_status(job)
        a = json.loads((ROOT / "out" / "soak" / "args.json").read_text())
        check(s["state"] == "running" and "fake script running" in s["logTail"], "job_status: running + log tail")
        check(a["PLAY"] == "10" and a["WALK"] == "1,3" and a["argv"][1].endswith("soak") and a["argv"][2] == "2", "soak args / env passed")
        check(b"testInput = true" in (ASSETS / "settings.ini").read_bytes(), "the stand-in switched testInput on")
        r = J.job_stop(job, wait_seconds=20)
        check(r["ok"] and r["status"]["state"] == "stopped" and r["status"]["exit"]["exit"] == 130, "job_stop: cooperative stop (exit 130)")
        check((ASSETS / "settings.ini").read_bytes() == settings_before, "the script's own finally restored settings.ini")
        check("drive-state" in r.get("testGame", ""), "job_stop: no drive-state -> no drive.ps1 stop")

        os.environ["FAKE_STUBBORN"] = "1"
        r = J.select_tune([{"label": "h800 y-200", "height": 800, "bottomY": -200}], [{"label": "s0.9", "scale": 0.9}],
                          str(ROOT / "out" / "tune"))
        check(r["ok"], "select_tune job started")
        job = r["job"]
        time.sleep(3)
        a = json.loads((ROOT / "out" / "tune" / "args.json").read_text())
        check(json.loads(Path(a["argv"][2]).read_text())[0]["height"] == 800 and json.loads(Path(a["argv"][3]).read_text())[0]["scale"] == 0.9,
              "select_tune wrote both candidate files")
        r = J.job_stop(job, wait_seconds=4)
        check(r["ok"] is False and "force=True" in r["error"], "stubborn job: stop without force reports it is still running")
        dstate.write_text(json.dumps({"gamePid": 777, "injectPid": 1}), encoding="ascii")
        orig = S._sinmai_running
        S._sinmai_running = lambda: [{"pid": 777, "path": "x"}]
        try:
            r = J.job_stop(job, wait_seconds=2, force=True)
        finally:
            S._sinmai_running = orig
        check(r["killed"] is True and r["status"]["state"] == "died", "force: exact PID terminated")
        check(r.get("settingsRestored") == ["frameLog", "testInput"] and (ASSETS / "settings.ini").read_bytes() == settings_before,
              "force: test-only keys put back from the snapshot")
        check(r.get("testGame") == "stopped" and (FAKEPROJ / "tools" / "drive-stop-called.txt").exists() and not dstate.exists(),
              "force: drive.ps1 stop for the test instance")
        os.environ.pop("FAKE_STUBBORN")

        os.environ["FAKE_SECONDS"] = "1"
        r = J.run_offscreen_test("ngo_attract_check.py", str(ROOT / "out" / "attract"), ["5"], {"CHECK_DT": "2"})
        check(r["ok"] and r["job"].endswith("-ngo_attract_check"), "run_offscreen_test job started")
        for _ in range(30):
            if J.job_status(r["job"])["state"] != "running":
                break
            time.sleep(0.5)
        s = J.job_status(r["job"])
        a = json.loads((ROOT / "out" / "attract" / "args.json").read_text())
        check(s["state"] == "finished" and s["exit"]["exit"] == 0 and a["CHECK_DT"] == "2" and a["argv"][2] == "5",
              "finished job: exit 0, args + env passed")
        check(any(f["name"] == "args.json" for f in s["files"]), "job_status lists the out folder")
        lj = J.list_jobs()
        check(lj["ok"] and len(lj["jobs"]) == 3 and lj["jobs"][0]["job"] == r["job"], "list_jobs newest first")
        check(not J.job_status("../../x")["ok"], "job ids are validated")
    finally:
        sys.path.remove(str(HERE))
        for k in ("FAKE_SECONDS", "FAKE_STUBBORN", "FAKE_SETTINGS", "PARTNERLIVE2D_STATE"):
            os.environ.pop(k, None)
        time.sleep(0.5)
        if ROOT.exists():
            shutil.rmtree(ROOT, ignore_errors=True)
        pyc = HERE / "__pycache__"
        if pyc.exists():
            shutil.rmtree(pyc, ignore_errors=True)

    print()
    if FAILS:
        print(f"{len(FAILS)} FAILED:")
        for f in FAILS:
            print("  - " + f)
        return 1
    print("ALL PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
