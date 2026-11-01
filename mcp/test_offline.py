"""Offline tests for server.py against a FAKE package dir (mcp\\_testpkg). Never touches D:\\maimai.

Run:  python test_offline.py
"""
from __future__ import annotations

import json
import os
import shutil
import socket
import sys
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parent
PKG = HERE / "_testpkg"
HARU = Path(os.environ.get("CUBISM_SDK_NATIVE") or r"E:\tools\live2d\CubismSdkForNative-5-r.5") / "Samples" / "Resources" / "Haru"
PNG = b"\x89PNG\r\n\x1a\n" + b"fakepngdata"

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


def build_fake_package(port: int) -> None:
    if PKG.exists():
        shutil.rmtree(PKG)
    root = PKG / "LocalAssets" / "PartnerLive2D"
    shutil.copytree(PROJECT / "deploy" / "LocalAssets" / "PartnerLive2D" / "hiyori", root / "hiyori")
    xml_dir = PKG / "Sinmai_Data" / "StreamingAssets" / "A500" / "partner" / "partner000501"
    xml_dir.mkdir(parents=True)
    shutil.copy(PROJECT / "deploy" / "Sinmai_Data" / "StreamingAssets" / "A500" / "partner" / "partner000501" / "Partner.xml", xml_dir / "Partner.xml")
    (root / "settings.ini").write_text(f"http = true\nport = {port}\n", encoding="utf-8")
    (PKG / "MelonLoader").mkdir()
    (PKG / "MelonLoader" / "Latest.log").write_text("line one\n[PartnerLive2D] started\nother\n[PartnerLive2D] second\n", encoding="utf-8")


class Stub(BaseHTTPRequestHandler):
    seen: list[tuple[str, str, str | None, bytes]] = []

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
        Stub.seen.append(("GET", self.path, self.headers.get("X-PartnerLive2D"), b""))
        if self.path.endswith(".png"):
            return self._reply(200, png=True)
        if self.path == "/api/status":
            return self._reply(200, {"ok": True, "version": "stub", "views": []})
        self._reply(404, {"ok": False, "error": "not found"})

    def do_POST(self):  # noqa: N802
        n = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(n)
        Stub.seen.append(("POST", self.path, self.headers.get("X-PartnerLive2D"), raw))
        if self.headers.get("X-PartnerLive2D") != "1":
            return self._reply(403, {"ok": False, "error": "missing header"})
        if self.path == "/api/partners/hiyori/config":
            return self._reply(200, {"ok": True, "name": "hiyori", "echo": json.loads(raw)})
        if self.path == "/api/partners/nope/config":
            return self._reply(404, {"ok": False, "error": "no partner named 'nope'"})
        self._reply(200, {"ok": True, "path": self.path, "body": json.loads(raw or b"{}")})


def main() -> int:
    dead_port = free_port()
    build_fake_package(dead_port)
    os.environ["PARTNERLIVE2D_PACKAGE"] = str(PKG)
    os.environ["PARTNERLIVE2D_PROJECT"] = str(PROJECT)
    os.environ.pop("PARTNERLIVE2D_PORT", None)
    sys.path.insert(0, str(HERE))
    import server as S  # noqa: E402

    try:
        # ---- tool registration: FastMCP keeps the plain functions ----
        check(callable(S.list_partners), "tool functions are plain callables")

        # ---- list_partners / get_partner / model_info ----
        r = S.list_partners()
        names = [p["name"] for p in r["partners"]]
        check(r["ok"] and names == ["hiyori"], f"list_partners finds hiyori: {names}")
        h = r["partners"][0]
        check(h["naviCharaId"] == 90 and h["partnerId"] == 501, "hiyori ids parsed")
        check(h["model"]["motionGroups"] == {"Idle": 3, "TapBody": 4} or "Idle" in h["model"]["motionGroups"],
              f"hiyori motion groups: {h['model']['motionGroups']}")
        check(h["modelExists"] and h["createdByMcp"] is False, "hiyori model exists, not created by mcp")

        g = S.get_partner("hiyori")
        check(g["ok"] and "motion.Default = Idle" in g["iniText"] and "groups" not in g, "get_partner returns ini text")
        check("Idle" in g["model"]["motionGroups"] and not g["model"]["missingFiles"], "get_partner model parsed, no missing files")
        check(not S.get_partner("../x")["ok"] and not S.get_partner("zzz")["ok"], "get_partner rejects bad / unknown names")

        mi = S.model_info(str(HARU / "Haru.model3.json"))
        check(mi["ok"] and set(mi["motionGroups"]) >= {"Idle", "TapBody"} and len(mi["expressions"]) == 8, f"model_info Haru: groups={list(mi['motionGroups'])}")
        check(S.model_info(str(HARU))["ok"], "model_info accepts a folder")
        check(not S.model_info(str(PKG / "nothing.model3.json"))["ok"], "model_info missing file -> error")

        # ---- set_partner_values: offline path (nothing listens on dead_port) ----
        ini = PKG / "LocalAssets" / "PartnerLive2D" / "hiyori" / "partner.ini"
        before = ini.read_text(encoding="utf-8")
        r = S.set_partner_values("hiyori", {"scale": 1.25, "flipY": True, "motion.Sad01": "TapBody", "offsetY": "-0.1", "debugDump": False})
        check(r["ok"] and r["mode"] == "offline", f"set_partner_values fell back to offline: {r.get('mode')}")
        after = ini.read_text(encoding="utf-8")
        check(all(c in after for c in ["# Live2D partner: Hiyori", "# Paths are relative", "#   Sad01        failed the song (loops)", "# expression.<state> = <expression name> (model has none)"]),
              "comments preserved")
        check("scale = 1.25" in after and "flipY = true" in after and "motion.Sad01 = TapBody" in after and "offsetY = -0.1" in after, "values replaced in place")
        check("debugDump = false" in after, "new key appended")
        check(after.index("scale = 1.25") < after.index("offsetX = 0") < after.index("renderSize = 1024"), "key order kept")
        check(before.count("\n") + 1 == after.count("\n") or after.count("\n") == before.count("\n") + 1, "line count only grew by the appended key")
        rawb = ini.read_bytes()
        check(rawb.count(b"\r\n") == rawb.count(b"\n") and not rawb.startswith(b"\xef\xbb\xbf"), "CRLF, no BOM like File.WriteAllLines")
        bad = S.set_partner_values("hiyori", {"nonsense": 1, "scale": "abc"})
        check(not bad["ok"] and "nonsense" in bad["errors"] and "scale" in bad["errors"], "invalid keys/values rejected")
        check(ini.read_text(encoding="utf-8") == after, "rejected edit left file untouched")
        check(not S.set_partner_values("ghost", {"scale": 1})["ok"], "offline edit of unknown partner errors")

        # ---- live tools: game not running ----
        live_calls = [
            S.game_status(), S.list_views(), S.view_info(1), S.save_partner("hiyori"), S.reload_partner("hiyori"),
            S.rescan_partners(), S.preview_partner("hiyori"), S.play_state(1, "Default"), S.play_motion(1, "Idle"),
            S.tap_partner(1), S.set_expression(1, "F01"), S.look_at(1, 0, 0), S.close_view(1), S.screenshot(), S.view_frame(1),
        ]
        ok_all = all(isinstance(c, dict) and c.get("ok") is False and c.get("game_running") is False and "not running" in c["error"] for c in live_calls)
        check(ok_all, "all 15 live tools return a clear 'game not running' error")
        check(not S.play_state(1, "Bogus")["ok"], "play_state validates state")

        # ---- live tools against a stub server (header, JSON, PNG, fallback logic) ----
        port = free_port()
        (PKG / "LocalAssets" / "PartnerLive2D" / "settings.ini").write_text(f"port = {port}\n", encoding="utf-8")
        srv = HTTPServer(("127.0.0.1", port), Stub)
        threading.Thread(target=srv.serve_forever, daemon=True).start()
        try:
            check(S.game_status()["version"] == "stub", "live: game_status via stub")
            r = S.set_partner_values("hiyori", {"scale": 2}, save=True)
            check(r["mode"] == "live" and r["echo"] == {"values": {"scale": "2"}, "save": True}, f"live: set_partner_values -> {r.get('mode')}")
            check(Stub.seen[-1][2] == "1", "live: X-PartnerLive2D header sent")
            r = S.set_partner_values("nope", {"scale": 2})
            check(r["ok"] is False and r["mode"] == "live" and "rescan" in r["hint"], "live: 404 not turned into an offline edit")
            r = S.play_motion(7, "TapBody", 2)
            check(r["path"] == "/api/views/7/motion" and r["body"] == {"group": "TapBody", "index": 2}, "live: play_motion body")
            r = S.look_at(3, 0.5, -0.5, 2)
            check(r["body"] == {"x": 0.5, "y": -0.5, "seconds": 2.0}, "live: look_at body")
            sp = HERE / "_testpkg" / "shots" / "s.png"
            img = S.screenshot(str(sp))
            check(isinstance(img, list) and img[0].data == PNG and sp.read_bytes() == PNG, "live: screenshot returns Image and saves file")
            img = S.view_frame(3)
            check(img.data == PNG and img._format == "png", "live: view_frame returns Image")
            check(S.preview_partner("hiyori", False)["body"] == {"show": False}, "live: preview_partner")
        finally:
            srv.shutdown()
            srv.server_close()
        (PKG / "LocalAssets" / "PartnerLive2D" / "settings.ini").write_text(f"port = {dead_port}\n", encoding="utf-8")

        # ---- create_partner ----
        partners_root = PKG / "LocalAssets" / "PartnerLive2D"
        xml_root = PKG / "Sinmai_Data" / "StreamingAssets" / "A500" / "partner"
        check(not S.create_partner("bad name", str(HARU), 90, "x")["ok"], "create: invalid name refused")
        check(not S.create_partner("haru", str(HARU), 5, "Haru")["ok"], "create: stock navi id refused")
        check(not S.create_partner("haru", str(HARU), 90, "Haru")["ok"], "create: navi id used by hiyori refused")
        check(not S.create_partner("haru", str(HARU), 91, "Haru", partner_id=12)["ok"], "create: stock partner id refused")
        check(not S.create_partner("haru", str(HARU), 91, "Haru", partner_id=501)["ok"], "create: existing Partner.xml id refused")
        check(not S.create_partner("haru", str(HARU / "nonexistent"), 91, "Haru")["ok"], "create: missing model dir refused")
        check(not S.create_partner("haru", str(HARU), 91, "Haru", motions={"Default": "NoSuchGroup"})["ok"], "create: unknown motion group refused")
        check(not partners_root.joinpath("haru").exists(), "refusals wrote nothing")
        check(not S.create_partner("hiyori", str(HARU), 91, "Haru")["ok"], "create: existing folder refused without overwrite")

        r = S.create_partner("haru", str(HARU), 91, "Haru & <Friends>", scale=0.9)
        check(r["ok"] and r["partnerId"] == 502, f"create: auto partner id = {r.get('partnerId')}")
        hini = (partners_root / "haru" / "partner.ini").read_text(encoding="utf-8")
        for needle in ["model = model/Haru.model3.json", "naviCharaId = 91", "partnerId = 502", "motion.Default = Idle",
                       "motion.Welcom = TapBody", "motion.FunStart = TapBody", "motion.Sad01 = Idle", "scale = 0.9", "# created_by = mcp"]:
            check(needle in hini, f"create: ini has '{needle}'")
        check((partners_root / "haru" / "model" / "Haru.moc3").exists() and (partners_root / "haru" / "model" / "motions").is_dir(), "create: model folder copied")
        xml_path = xml_root / "partner000502" / "Partner.xml"
        raw = xml_path.read_bytes()
        txt = raw.decode("utf-8-sig")
        check(raw.startswith(b"\xef\xbb\xbf"), "create: Partner.xml has UTF-8 BOM")
        check("<dataName>partner000502</dataName>" in txt, "create: dataName replaced")
        check("<name>\r\n    <id>502</id>\r\n    <str>Haru &amp; &lt;Friends&gt;</str>" in txt, "create: <name> block replaced")
        check("<naviChara>\r\n    <id>91</id>\r\n    <str>Haru &amp; &lt;Friends&gt;</str>" in txt, "create: <naviChara> block replaced")
        tpl = (PROJECT / "deploy/Sinmai_Data/StreamingAssets/A500/partner/partner000501/Partner.xml").read_bytes().decode("utf-8-sig")
        check(txt.count("\r\n") == tpl.count("\r\n") and "<releaseTagName>" in txt and "<netOpenName>" in txt, "create: rest of template kept")
        import xml.dom.minidom as md
        md.parseString(raw)  # well-formed
        check(True, "create: Partner.xml is well-formed XML")

        lp = S.list_partners()
        hp = [p for p in lp["partners"] if p["name"] == "haru"][0]
        check(hp["createdByMcp"] and hp["partnerXmlExists"] and "TapBody" in hp["model"]["motionGroups"], "list_partners sees the new partner (mcp-made, xml exists)")

        # replace_existing: no Partner.xml
        r = S.create_partner("haru2", str(HARU / "Haru.model3.json"), 7, "Haru Two", replace_existing=True, motions={"motion.Sad01": "TapBody"})
        check(r["ok"] and r["partnerXml"] is None and r["partnerId"] is None, "create(replace_existing): no Partner.xml")
        check(not (xml_root / "partner000503").exists(), "create(replace_existing): nothing in game data")
        h2 = (partners_root / "haru2" / "partner.ini").read_text(encoding="utf-8")
        check("naviCharaId = 7" in h2 and "partnerId" not in h2.replace("# ", "") and "motion.Sad01 = TapBody" in h2, "create(replace_existing): ini ok")
        check(not S.create_partner("x1", str(HARU), 91, "x", replace_existing=True)["ok"], "create: replace_existing needs a stock navi id")

        # overwrite keeps working and re-uses its own partner id
        r = S.create_partner("haru", str(HARU), 91, "Haru Again", partner_id=502, overwrite=True)
        check(r["ok"] and r["partnerId"] == 502, "create(overwrite): same folder & id accepted")
        check("Haru Again" in (xml_root / "partner000502" / "Partner.xml").read_text("utf-8-sig"), "create(overwrite): xml refreshed")

        # ---- delete_partner ----
        check(not S.delete_partner("haru")["ok"] and (partners_root / "haru").exists(), "delete: refused without confirm")
        r = S.delete_partner("haru", confirm=True)
        check(r["ok"] and r["createdByMcp"] and not (partners_root / "haru").exists() and not (xml_root / "partner000502").exists(),
              f"delete: folder + Partner.xml removed ({r.get('notes')})")
        r = S.delete_partner("haru2", confirm=True)
        check(r["ok"] and not (partners_root / "haru2").exists(), "delete: replace_existing partner removed")
        r = S.delete_partner("hiyori", confirm=True)
        check(r["ok"] and not r["createdByMcp"] and (xml_root / "partner000501" / "Partner.xml").exists(),
              "delete: hand-made partner removed, its Partner.xml kept")
        check(not S.delete_partner("../x", confirm=True)["ok"], "delete: traversal refused")

        # ---- read_mod_log ----
        r = S.read_mod_log(2)
        check(r["ok"] and r["text"].splitlines() == ["other", "[PartnerLive2D] second"], "read_mod_log tail")
        r = S.read_mod_log(10, "partnerlive2d")
        check(r["total_matching"] == 2 and "one" not in r["text"], "read_mod_log filter (case-insensitive)")

        # ---- build_and_install / test_game safety (no real build, no game start) ----
        running = S._sinmai_running()
        check(running == [], f"no package Sinmai.exe running (fake package): {running}")
        orig = S._sinmai_running
        S._sinmai_running = lambda: [{"pid": 1, "path": "x"}]
        try:
            r = S.build_and_install()
            check(r["ok"] is False and "running" in r["error"], "build_and_install refuses while game runs")
            check(S.test_game_start()["ok"] is False, "test_game_start refuses while game runs")
        finally:
            S._sinmai_running = orig
        check(not S.test_game_keys("C;rm -rf")["ok"] and not S.test_game_hold("En ter", 100)["ok"] and not S.test_game_hold("Enter", 99999999)["ok"], "test_game_* validate input")
    finally:
        sys.path.remove(str(HERE))
        if PKG.exists():
            shutil.rmtree(PKG, ignore_errors=True)
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
