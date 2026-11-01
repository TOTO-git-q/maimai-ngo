"""Shared helpers for the PartnerLive2D MCP extension tools (ngo_api.py, ngo_jobs.py).

server.py calls bind(<server module>) before the extension tools are registered; every helper reads the
configuration at call time (environment variables, see README.md), so tests can point it at a fake package.
"""
from __future__ import annotations

import json
import os
import tempfile
import time
from pathlib import Path
from typing import Any, Optional

core: Any = None   # the server.py module (its _api/_err/_package/_project/_sinmai_running helpers)

# the tools\*.py test scripts have these built in (they do not read the environment)
SCRIPTS_PACKAGE = r"D:\maimai\Package"
SCRIPTS_PORT = 8791


def bind(server_module: Any) -> None:
    global core
    core = server_module


# --------------------------------------------------------------------------------------
# paths (environment variable, else derived from the package / project folder)
# --------------------------------------------------------------------------------------

def tools_dir() -> Path:
    return core._project() / "tools"


def game_root() -> Path:
    """Folder that holds Package (launcher.ps1, auto-login.py, playlogs\\ live here)."""
    return Path(os.environ.get("PARTNERLIVE2D_GAME_ROOT") or core._package().parent)


def playlogs_dir() -> Path:
    return Path(os.environ.get("PARTNERLIVE2D_PLAYLOGS") or (game_root() / "playlogs"))


def state_dir() -> Path:
    """Job logs, settings snapshots, moved heap walks, backups. Never inside the game or the repo."""
    return Path(os.environ.get("PARTNERLIVE2D_STATE") or (Path(tempfile.gettempdir()) / "partnerlive2d-mcp"))


def assets_root() -> Path:
    return core._partners_root()


def settings_path() -> Path:
    return assets_root() / "settings.ini"


def drive_state_path() -> Path:
    return tools_dir() / "drive-state.json"


def python_exe() -> str:
    import sys
    return os.environ.get("PARTNERLIVE2D_PYTHON") or sys.executable


def stamp() -> str:
    return time.strftime("%Y%m%d-%H%M%S")


# --------------------------------------------------------------------------------------
# which game is running: the drive.ps1 test instance or someone else's game
# --------------------------------------------------------------------------------------

def read_drive_state() -> Optional[dict]:
    p = drive_state_path()
    try:
        d = json.loads(p.read_bytes().decode("utf-8-sig"))
        return d if isinstance(d, dict) else None
    except (OSError, ValueError):
        return None


def classify_games() -> dict:
    """Package Sinmai.exe processes, each marked testInstance when it is the game drive.ps1 started
    (pid in tools\\drive-state.json). Raises RuntimeError when the process list cannot be read."""
    running = core._sinmai_running()
    st = read_drive_state() or {}
    test_pid = st.get("gamePid")
    procs = [dict(p, testInstance=(p.get("pid") == test_pid)) for p in running]
    return {
        "processes": procs,
        "test": [p for p in procs if p["testInstance"]],
        "other": [p for p in procs if not p["testInstance"]],
        "driveState": st or None,
    }


def guard_no_game(action: str) -> Optional[dict]:
    """Error dict when any Sinmai.exe of the package runs (same rule as test_game_start / build_and_install)."""
    try:
        g = classify_games()
    except RuntimeError as e:
        return core._err(f"{action}: cannot verify that no game is running: {e}")
    if g["processes"]:
        return core._err(f"{action}: a game from this package is already running; nothing was started. "
                         "Wait until it has exited (never stop a game you did not start).", running=g["processes"])
    return None


def guard_test_instance(action: str, force: bool = False) -> Optional[dict]:
    """Error dict unless the only package game running is the off-screen test instance started by drive.ps1.
    force=True skips the check (only for a game you started yourself some other way)."""
    if force:
        return None
    try:
        g = classify_games()
    except RuntimeError as e:
        return core._err(f"{action}: cannot tell which game is running: {e}")
    if g["other"]:
        return core._err(f"{action}: refused - a game that drive.ps1 did not start is running (pid "
                         + ", ".join(str(p["pid"]) for p in g["other"]) + "). Test helpers only touch the "
                         "off-screen test instance.", running=g["processes"])
    if not g["test"]:
        return core._err(f"{action}: no off-screen test game is running (start one with test_game_start or a test job)",
                         game_running=bool(g["processes"]))
    return None


def scripts_compatible(action: str) -> Optional[dict]:
    """The tools\\*.py game scripts have the package path and the API port built in; refuse to run them
    against a different setup instead of driving the wrong folder."""
    pkg = str(core._package()).rstrip("\\/").lower().replace("/", "\\")
    if pkg != SCRIPTS_PACKAGE.lower():
        return core._err(f"{action}: the tools\\ scripts are written for {SCRIPTS_PACKAGE}, but PARTNERLIVE2D_PACKAGE is "
                         f"{core._package()}. Install the game there or adapt the scripts' constants first.")
    if core._port() != SCRIPTS_PORT:
        return core._err(f"{action}: the tools\\ scripts call the mod API on port {SCRIPTS_PORT}, but the configured port "
                         f"is {core._port()}.")
    return None


# --------------------------------------------------------------------------------------
# results
# --------------------------------------------------------------------------------------

def save_or_return(data: Any, save_path: Optional[str], preview_chars: int = 2000) -> Any:
    """Big JSON answers: with save_path the full JSON goes to that file and only a preview comes back."""
    if not save_path or not isinstance(data, dict) or data.get("ok") is False:
        return data
    p = Path(save_path)
    p.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(data, ensure_ascii=False, indent=1)
    p.write_text(text, encoding="utf-8")
    return {"ok": True, "saved": str(p), "bytes": len(text.encode("utf-8")), "preview": text[:preview_chars]}


# --------------------------------------------------------------------------------------
# ini lines (settings.ini as the scripts and the mod write it)
# --------------------------------------------------------------------------------------

def ini_values(text: str) -> dict[str, str]:
    out: dict[str, str] = {}
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line[0] in "#;" or "=" not in line:
            continue
        k, v = line.split("=", 1)
        out[k.strip()] = v.strip()
    return out


def ini_set_keys(text: str, wanted: dict[str, Optional[str]]) -> str:
    """Set (value) or remove (None) top-level keys; other lines, comments and line endings are kept."""
    nl = "\r\n" if "\r\n" in text else "\n"
    lines = text.splitlines()
    out, done = [], set()
    for raw in lines:
        t = raw.strip()
        key = t.split("=", 1)[0].strip() if ("=" in t and t[:1] not in ("#", ";")) else None
        if key in wanted:
            if wanted[key] is not None and key not in done:
                out.append(f"{key} = {wanted[key]}")
                done.add(key)
            continue
        out.append(raw)
    for k, v in wanted.items():
        if v is not None and k not in done:
            out.append(f"{k} = {v}")
    return nl.join(out) + nl
