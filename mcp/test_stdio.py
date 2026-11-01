"""Spawn server.py over stdio, do initialize + tools/list, check every expected tool is listed."""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
EXPECTED = {
    "game_status", "list_views", "view_info", "set_partner_values", "save_partner", "reload_partner",
    "rescan_partners", "preview_partner", "play_state", "play_motion", "tap_partner", "set_expression",
    "look_at", "close_view", "screenshot", "view_frame",
    "list_partners", "get_partner", "model_info", "create_partner", "delete_partner", "build_and_install",
    "read_mod_log",
    "test_game_start", "test_game_keys", "test_game_hold", "test_game_shot", "test_game_stop",
    # ngo_api.py
    "theme", "music_select_info", "music_select_layout", "music_select_node", "music_select_image",
    "music_select_reset", "music_select_save", "music_select_tree", "music_select_sprite",
    "get_live_partner", "set_partner_placement",
    "test_screen_state", "test_music_probe", "test_ui_dump", "test_tree", "test_renderers", "test_select_perf",
    "test_autoplay", "test_force_rank", "test_heapwalk",
    # ngo_jobs.py
    "game_processes", "play_report", "heapwalk_diff", "soak_test", "select_tune", "to_collection",
    "run_offscreen_test", "test_game_vkeys", "job_status", "job_stop", "list_jobs",
}


def main() -> int:
    p = subprocess.Popen([sys.executable, str(HERE / "server.py")], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                         stderr=subprocess.PIPE, text=True, encoding="utf-8")

    def send(obj: dict) -> None:
        p.stdin.write(json.dumps(obj) + "\n")
        p.stdin.flush()

    def recv() -> dict:
        while True:
            line = p.stdout.readline()
            if not line:
                raise RuntimeError("server closed stdout; stderr: " + p.stderr.read())
            if line.strip():
                return json.loads(line)

    try:
        send({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
            "protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "test", "version": "0"}}})
        init = recv()
        print("server:", init["result"]["serverInfo"])
        send({"jsonrpc": "2.0", "method": "notifications/initialized"})
        send({"jsonrpc": "2.0", "id": 2, "method": "tools/list"})
        tools = recv()["result"]["tools"]
        names = {t["name"] for t in tools}
        print(f"{len(names)} tools listed")
        missing = EXPECTED - names
        extra = names - EXPECTED
        empty_doc = [t["name"] for t in tools if not (t.get("description") or "").strip()]
        print("missing:", sorted(missing), "extra:", sorted(extra), "no description:", empty_doc)
        ok = init["result"]["serverInfo"]["name"] == "partnerlive2d" and not missing and not extra and not empty_doc
        # one real call through the protocol (offline + harmless): model_info on a nonexistent file
        send({"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": "model_info", "arguments": {"model3_json_path": "Z:\\nope.model3.json"}}})
        r = recv()["result"]
        print("tools/call model_info ->", r["content"][0]["text"][:120])
        ok = ok and not r.get("isError", False)
        print("STDIO OK" if ok else "STDIO FAILED")
        return 0 if ok else 1
    finally:
        p.stdin.close()
        p.terminate()
        try:
            p.wait(5)
        except subprocess.TimeoutExpired:
            p.kill()


if __name__ == "__main__":
    sys.exit(main())
