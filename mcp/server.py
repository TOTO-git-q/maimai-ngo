"""PartnerLive2D MCP server (stdio).

Gives an AI agent full control of the PartnerLive2D maimai mod:
  * live control of a running game through the mod's local HTTP API (ControlServer.cs),
  * offline management of partners on disk (partner.ini, model copy, Partner.xml),
  * building / installing the mod, reading the MelonLoader log,
  * driving a test instance of the game (tools/drive.ps1).

Standard library + the `mcp` package only.
"""
from __future__ import annotations

import base64
import json
import os
import re
import shutil
import stat
import subprocess
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any, Optional

from mcp.server.fastmcp import FastMCP, Image

mcp = FastMCP("partnerlive2d")

# --------------------------------------------------------------------------------------
# configuration (read at call time so tests / users can change env vars)
# --------------------------------------------------------------------------------------

DEFAULT_PACKAGE = r"D:\maimai\Package"
# the project folder this file lives in (mcp\..); on the original machine E:\claude workspace\maimai-partner-live2d
DEFAULT_PROJECT = str(Path(__file__).resolve().parent.parent)
STATES = ["Default", "Welcom", "FunStart", "FunStartLoop", "Sad01"]
STOCK_PARTNER_IDS = range(1, 39)      # 1..38 are taken by the game
STOCK_NAVI_IDS = range(0, 29)         # 0..28 are taken by the game
FIRST_CUSTOM_PARTNER_ID = 501
NAME_RE = re.compile(r"^[A-Za-z0-9_-]{1,64}$")
MARKER_RE = re.compile(r"^\s*#?\s*created_by\s*=\s*mcp\s*$", re.I)

INT_KEYS = {"naviCharaId", "partnerId", "renderSize"}
FLOAT_KEYS = {"scale", "offsetX", "offsetY", "lookStrength", "roomScale", "roomOffsetX", "roomOffsetY"}
BOOL_KEYS = {"flipY", "debugDump", "debugOverlay"}
STR_KEYS = {"model", "icon", "resultImage", "tap", "idle", "sprites", "resultUnder"}


def _package() -> Path:
    return Path(os.environ.get("PARTNERLIVE2D_PACKAGE") or DEFAULT_PACKAGE)


def _project() -> Path:
    return Path(os.environ.get("PARTNERLIVE2D_PROJECT") or DEFAULT_PROJECT)


def _partners_root() -> Path:
    return _package() / "LocalAssets" / "PartnerLive2D"


def _partner_xml_dir(partner_id: int) -> Path:
    return _package() / "Sinmai_Data" / "StreamingAssets" / "A500" / "partner" / f"partner{partner_id:06d}"


def _template_xml() -> Path:
    return _project() / "deploy" / "Sinmai_Data" / "StreamingAssets" / "A500" / "partner" / "partner000501" / "Partner.xml"


def _port() -> int:
    env = os.environ.get("PARTNERLIVE2D_PORT")
    if env:
        return int(env)
    ini = _partners_root() / "settings.ini"
    try:
        for k, v in _read_ini_pairs(ini):
            if k == "port":
                return int(v)
    except (OSError, ValueError):
        pass
    return 8791


def _err(msg: str, **extra: Any) -> dict:
    d = {"ok": False, "error": msg}
    d.update(extra)
    return d


# --------------------------------------------------------------------------------------
# ini handling (mirrors PartnerConfig.cs: ReadIni / Set / Save)
# --------------------------------------------------------------------------------------

def _read_lines(path: Path) -> list[str]:
    return path.read_bytes().decode("utf-8-sig").splitlines()


def _read_ini_pairs(path: Path) -> list[tuple[str, str]]:
    pairs = []
    for raw in _read_lines(path):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if "=" not in line:
            continue
        k, v = line.split("=", 1)
        pairs.append((k.strip(), v.strip()))
    return pairs


def _write_lines(path: Path, lines: list[str]) -> None:
    # File.WriteAllLines on Windows: CRLF after every line, UTF-8 without BOM
    path.write_bytes(("".join(l + "\r\n" for l in lines)).encode("utf-8"))


def _ini_save(path: Path, updates: dict[str, str]) -> None:
    """Same behaviour as PartnerConfig.Save(): every existing 'key = ...' line is rewritten in
    place as 'key = value' (comments and order kept), keys that are not in the file are appended."""
    lines = _read_lines(path) if path.exists() else []
    values: dict[str, str] = dict(_read_ini_pairs(path)) if path.exists() else {}
    values.update(updates)
    written: set[str] = set()
    for i, raw in enumerate(lines):
        t = raw.strip()
        if not t or t.startswith("#") or "=" not in t:
            continue
        key = t.split("=", 1)[0].strip()
        if key in values:
            lines[i] = f"{key} = {values[key]}"
            written.add(key)
    for k, v in values.items():
        if k not in written:
            lines.append(f"{k} = {v}")
    _write_lines(path, lines)


def _normalize_value(key: str, value: Any) -> str:
    """Validate one partner.ini key/value and return the string stored in the file."""
    if value is None:
        s = ""
    elif isinstance(value, bool):
        s = "true" if value else "false"
    else:
        s = str(value).strip()
    if "\n" in s or "\r" in s:
        raise ValueError("value must be a single line")
    if key in INT_KEYS:
        try:
            f = float(s)
        except ValueError:
            raise ValueError(f"'{key}' needs an integer, got {value!r}")
        if f != int(f):
            raise ValueError(f"'{key}' needs an integer, got {value!r}")
        return str(int(f))
    if key in FLOAT_KEYS:
        try:
            f = float(s)
        except ValueError:
            raise ValueError(f"'{key}' needs a number, got {value!r}")
        if f != f or f in (float("inf"), float("-inf")):
            raise ValueError(f"'{key}' needs a finite number")
        return s
    if key in BOOL_KEYS:
        low = s.lower()
        if low in ("true", "1", "yes", "on"):
            return "true"
        if low in ("false", "0", "no", "off", ""):
            return "false"
        raise ValueError(f"'{key}' needs true/false, got {value!r}")
    if key in STR_KEYS:
        return s
    for prefix in ("motion.", "expression."):
        if key.startswith(prefix):
            state = key[len(prefix):]
            if state not in STATES:
                raise ValueError(f"'{key}': state must be one of {', '.join(STATES)}")
            return s
    raise ValueError(
        f"unknown key '{key}'. Valid: {', '.join(sorted(INT_KEYS | FLOAT_KEYS | BOOL_KEYS | STR_KEYS))}, "
        f"motion.<State>, expression.<State> with State in {', '.join(STATES)}"
    )


# --------------------------------------------------------------------------------------
# model3.json parsing
# --------------------------------------------------------------------------------------

def _parse_model3(path: Path) -> dict:
    info: dict[str, Any] = {"path": str(path), "exists": path.exists()}
    if not path.exists():
        info["error"] = "file not found"
        return info
    try:
        root = json.loads(path.read_bytes().decode("utf-8-sig"))
    except Exception as e:  # noqa: BLE001
        info["error"] = f"cannot parse: {e}"
        return info
    refs = root.get("FileReferences", {}) or {}
    base = path.parent
    groups = {}
    for g, items in (refs.get("Motions") or {}).items():
        groups[g] = {"count": len(items), "files": [i.get("File") for i in items]}
    exprs = [e.get("Name") for e in (refs.get("Expressions") or [])]
    hits = [h.get("Name") for h in (root.get("HitAreas") or [])]
    missing = []
    files = [refs.get("Moc"), refs.get("Physics"), refs.get("Pose"), refs.get("DisplayInfo"), refs.get("UserData")]
    files += list(refs.get("Textures") or [])
    for items in (refs.get("Motions") or {}).values():
        for i in items:
            files += [i.get("File")]
    for e in refs.get("Expressions") or []:
        files.append(e.get("File"))
    for f in files:
        if isinstance(f, dict):
            f = f.get("File")
        if f and not (base / f).exists():
            missing.append(f)
    info.update(
        motionGroups=groups,
        expressions=exprs,
        hitAreas=hits,
        textures=len(refs.get("Textures") or []),
        missingFiles=missing,
    )
    return info


def _find_model3(model_dir: Path) -> tuple[Optional[Path], list[str]]:
    warnings: list[str] = []
    if model_dir.is_file():
        return (model_dir, warnings) if model_dir.name.lower().endswith(".model3.json") else (None, warnings)
    cands = sorted(model_dir.glob("*.model3.json"))
    if not cands:
        cands = sorted(model_dir.rglob("*.model3.json"))
    if not cands:
        return None, warnings
    if len(cands) > 1:
        warnings.append("several model3.json found, using " + cands[0].name + "; others: " + ", ".join(c.name for c in cands[1:]))
    return cands[0], warnings


# --------------------------------------------------------------------------------------
# partners on disk
# --------------------------------------------------------------------------------------

def _existing_partner_dir(name: str) -> Path:
    if not name or name in (".", "..") or any(c in name for c in '/\\:*?"<>|'):
        raise ValueError(f"bad partner name {name!r}")
    d = _partners_root() / name
    if not d.is_dir():
        raise FileNotFoundError(f"no partner folder '{name}' under {_partners_root()}")
    return d


def _partner_summary(d: Path, with_model: bool = True) -> dict:
    ini = d / "partner.ini"
    out: dict[str, Any] = {"name": d.name, "folder": str(d), "ini": str(ini), "hasIni": ini.exists()}
    if not ini.exists():
        return out
    text_lines = _read_lines(ini)
    values = dict(_read_ini_pairs(ini))
    out["values"] = values
    out["createdByMcp"] = any(MARKER_RE.match(l) for l in text_lines)

    def num(k: str) -> Optional[int]:
        try:
            return int(values[k])
        except (KeyError, ValueError):
            return None

    out["naviCharaId"] = num("naviCharaId")
    out["partnerId"] = num("partnerId")
    out["motions"] = {s: values.get("motion." + s) for s in STATES if "motion." + s in values}
    out["expressions"] = {s: values.get("expression." + s) for s in STATES if "expression." + s in values}
    model_rel = values.get("model", "")
    model_path = (d / model_rel).resolve() if model_rel else None
    out["modelPath"] = str(model_path) if model_path else None
    out["modelExists"] = bool(model_path and model_path.exists())
    if out["partnerId"] is not None:
        xml = _partner_xml_dir(out["partnerId"]) / "Partner.xml"
        out["partnerXml"] = str(xml)
        out["partnerXmlExists"] = xml.exists()
    if with_model and model_path:
        mi = _parse_model3(model_path)
        out["model"] = {
            "motionGroups": {g: v["count"] for g, v in mi.get("motionGroups", {}).items()},
            "expressions": mi.get("expressions", []),
            "hitAreas": mi.get("hitAreas", []),
            **({"error": mi["error"]} if "error" in mi else {}),
        }
    return out


def _all_partner_dirs() -> list[Path]:
    root = _partners_root()
    if not root.is_dir():
        return []
    return sorted(p for p in root.iterdir() if p.is_dir() and (p / "partner.ini").exists())


def _rmtree(path: Path) -> None:
    def onerror(func, p, exc):  # noqa: ANN001
        try:
            os.chmod(p, stat.S_IWRITE)
            func(p)
        except OSError:
            raise
    shutil.rmtree(path, onerror=onerror)


def _tail(text: str, n: int = 60) -> str:
    lines = text.splitlines()
    return "\n".join(lines[-n:])


def _decode(b: bytes) -> str:
    try:
        return b.decode("utf-8")
    except UnicodeDecodeError:
        return b.decode("mbcs", errors="replace")


def _run(cmd: list[str], timeout: float, cwd: Optional[Path] = None) -> tuple[int, str]:
    try:
        p = subprocess.run(cmd, capture_output=True, timeout=timeout, cwd=str(cwd) if cwd else None, stdin=subprocess.DEVNULL)
    except subprocess.TimeoutExpired as e:
        out = _decode((e.stdout or b"") + (e.stderr or b""))
        return 124, out + f"\n[timed out after {timeout:.0f}s]"
    return p.returncode, _decode(p.stdout + p.stderr)


def _ps(command: str, timeout: float = 30) -> tuple[int, str]:
    enc = base64.b64encode(command.encode("utf-16-le")).decode()
    return _run(["powershell", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", enc], timeout)


def _sinmai_running() -> list[dict]:
    """Sinmai.exe processes whose executable is the one in the package dir.
    Raises RuntimeError when it cannot be determined."""
    exe = str((_package() / "Sinmai.exe")).lower().replace("/", "\\")
    cmd = (
        "$ErrorActionPreference='Stop'; "
        "Get-CimInstance Win32_Process -Filter \"Name='Sinmai.exe'\" | "
        "ForEach-Object { '{0}|{1}' -f $_.ProcessId, $(if ($_.ExecutablePath) { $_.ExecutablePath } else { '?' }) }"
    )
    rc, out = _ps(cmd)
    if rc != 0:
        raise RuntimeError("could not list processes: " + _tail(out, 5))
    found = []
    for line in out.splitlines():
        line = line.strip()
        if "|" not in line:
            continue
        pid, path = line.split("|", 1)
        if path == "?":
            raise RuntimeError(f"Sinmai.exe pid {pid} is running but its path cannot be read; cannot tell whether it is the package one")
        if path.lower().replace("/", "\\") == exe:
            found.append({"pid": int(pid), "path": path})
    return found


# --------------------------------------------------------------------------------------
# live HTTP API
# --------------------------------------------------------------------------------------

class GameUnreachable(Exception):
    pass


def _http(method: str, path: str, body: Any = None, timeout: float = 12.0) -> tuple[int, str, bytes]:
    url = f"http://127.0.0.1:{_port()}{path}"
    data = None
    headers = {"X-PartnerLive2D": "1"}
    if method != "GET":
        data = json.dumps(body if body is not None else {}).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, r.headers.get("Content-Type", ""), r.read()
    except urllib.error.HTTPError as e:
        return e.code, e.headers.get("Content-Type", "") if e.headers else "", e.read()
    except (urllib.error.URLError, OSError, TimeoutError) as e:
        raise GameUnreachable(
            f"game not running / mod control API unreachable at 127.0.0.1:{_port()} ({type(getattr(e, 'reason', e)).__name__}). "
            "Start the game with the mod installed (see test_game_start), or check 'http'/'port' in "
            "LocalAssets\\PartnerLive2D\\settings.ini."
        ) from e


def _api(method: str, path: str, body: Any = None, timeout: float = 12.0) -> dict:
    """JSON call. Returns the mod's JSON (dict with ok); connection problems -> ok=False, game_running=False."""
    try:
        status, ctype, raw = _http(method, path, body, timeout)
    except GameUnreachable as e:
        return _err(str(e), game_running=False)
    try:
        data = json.loads(raw.decode("utf-8"))
    except Exception:  # noqa: BLE001
        return _err(f"unexpected non-JSON reply (HTTP {status}, {ctype})", http_status=status)
    if isinstance(data, dict):
        if status >= 400:
            data.setdefault("ok", False)
            data["http_status"] = status
        return data
    return {"ok": status < 400, "result": data}


def _api_png(path: str) -> tuple[Optional[bytes], Optional[dict]]:
    try:
        status, ctype, raw = _http("GET", path, None, 20.0)
    except GameUnreachable as e:
        return None, _err(str(e), game_running=False)
    if status == 200 and raw[:8] == b"\x89PNG\r\n\x1a\n":
        return raw, None
    try:
        d = json.loads(raw.decode("utf-8"))
        if isinstance(d, dict):
            d.setdefault("ok", False)
            d["http_status"] = status
            return None, d
    except Exception:  # noqa: BLE001
        pass
    return None, _err(f"no PNG in reply (HTTP {status}, {ctype})", http_status=status)


def _q(name: str) -> str:
    return urllib.parse.quote(name, safe="")


def _png_result(png: bytes, save_path: Optional[str]) -> Any:
    if save_path:
        p = Path(save_path)
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(png)
        return [Image(data=png, format="png"), f"saved to {p}"]
    return Image(data=png, format="png")


# ======================================================================================
# LIVE TOOLS (game must be running with the mod)
# ======================================================================================

@mcp.tool()
def game_status() -> dict:
    """Status of the running game's PartnerLive2D mod: mod version, screen size, all partners
    (config + model motion groups/expressions/hit areas + the live view ids showing each one),
    all live views, and the list of valid animation states.
    Returns ok=false / game_running=false when the game or the mod's control API is not reachable."""
    return _api("GET", "/api/status")


@mcp.tool()
def list_views() -> dict:
    """List the live Live2D views currently on screen (a view exists while a partner is shown, e.g.
    on the result screen / partner room / preview overlay; view ids vanish when the screen changes)."""
    return _api("GET", "/api/views")


@mcp.tool()
def view_info(view_id: int) -> dict:
    """Details of one live view: partner, context, current state/motion, etc."""
    return _api("GET", f"/api/views/{int(view_id)}")


@mcp.tool()
def set_partner_values(name: str, values: dict, save: bool = False) -> dict:
    """Edit partner.ini settings of a partner.

    values: {key: value}. Valid keys: model, naviCharaId, partnerId, icon, resultImage, tap, idle,
    scale (0.05-10), offsetX / offsetY (-5..5, in units of the stock partner's height; result screen and
    preview overlay), roomScale / roomOffsetX / roomOffsetY (the same for the collection's partner room),
    resultUnder (comma list of result-screen objects drawn under the partner), sprites (sprite manifest,
    relative path), renderSize (64-4096), flipY, lookStrength (0-3), debugDump, debugOverlay,
    motion.<State>, expression.<State> with State in Default, Welcom, FunStart, FunStartLoop, Sad01
    (motion value = model motion group, "" keeps idle). The music-select partner is placed by the select
    preset instead (music_select_layout).

    If the game is running the values are applied live to the running partner (visible immediately);
    save=True also writes them to partner.ini. If the game is NOT running the edit is done directly
    on partner.ini on disk (comments and key order preserved; always written) and the result says
    mode="offline". The result's "mode" field tells which happened."""
    if not isinstance(values, dict) or not values:
        return _err("values must be a non-empty object {key: value}")
    norm: dict[str, str] = {}
    bad: dict[str, str] = {}
    for k, v in values.items():
        try:
            norm[k] = _normalize_value(k, v)
        except ValueError as e:
            bad[k] = str(e)
    if bad:
        return _err("invalid values, nothing applied", errors=bad)

    live = _api("POST", f"/api/partners/{_q(name)}/config", {"values": norm, "save": bool(save)})
    if live.get("game_running") is not False:
        live["mode"] = "live"
        if live.get("ok") is False and live.get("http_status") == 404:
            live["hint"] = "the running game does not know this partner; if the folder was just created call rescan_partners (new partners with Partner.xml need a game restart)"
        return live

    # game not running -> edit partner.ini on disk
    try:
        d = _existing_partner_dir(name)
    except (ValueError, FileNotFoundError) as e:
        return _err(str(e), mode="offline", game_running=False)
    ini = d / "partner.ini"
    if not ini.exists():
        return _err(f"{ini} does not exist", mode="offline", game_running=False)
    _ini_save(ini, norm)
    return {
        "ok": True,
        "mode": "offline",
        "game_running": False,
        "note": "game not running: edited partner.ini on disk; takes effect next time the game starts (or rescan_partners / reload_partner while running)",
        "ini": str(ini),
        "applied": norm,
    }


@mcp.tool()
def save_partner(name: str) -> dict:
    """Write the running game's current (live-edited) values of a partner back to its partner.ini
    (comments kept). Needs the game running."""
    return _api("POST", f"/api/partners/{_q(name)}/save", {})


@mcp.tool()
def reload_partner(name: str) -> dict:
    """Re-read partner.ini from disk in the running game and rebuild the partner's model
    (use after editing the file by hand or after set_partner_values while the game was off)."""
    return _api("POST", f"/api/partners/{_q(name)}/reload", {})


@mcp.tool()
def rescan_partners() -> dict:
    """Make the running game rescan LocalAssets\\PartnerLive2D for new/changed partner folders.
    Returns the new status. NOTE: a brand-new partner also needs its Partner.xml loaded by the game,
    which only happens at game start."""
    return _api("POST", "/api/rescan", {})


@mcp.tool()
def preview_partner(name: str, show: bool = True) -> dict:
    """Show (show=True) or hide (show=False) a preview overlay of the partner on the running game's
    screen. Returns the new live view (its id is needed for play_state/play_motion/... and view_frame)."""
    return _api("POST", f"/api/partners/{_q(name)}/preview", {"show": bool(show)})


@mcp.tool()
def play_state(view_id: int, state: str) -> dict:
    """Play the animation mapped to a game state on a live view.
    state: Default (result screen opens), Welcom (partner room greeting), FunStart (cleared the song),
    FunStartLoop (cleared, looping), Sad01 (failed)."""
    if state not in STATES:
        return _err("state must be one of " + ", ".join(STATES))
    return _api("POST", f"/api/views/{int(view_id)}/state", {"state": state})


@mcp.tool()
def play_motion(view_id: int, group: str, index: int = -1) -> dict:
    """Play a motion of the model directly: group = motion group name from the model3.json
    (e.g. Idle, TapBody), index = motion number inside the group (-1 = random). Fails (409) when the
    group/index is unknown or a higher-priority motion is running."""
    return _api("POST", f"/api/views/{int(view_id)}/motion", {"group": group, "index": int(index)})


@mcp.tool()
def tap_partner(view_id: int) -> dict:
    """Simulate the player touching the partner: plays the partner's tap motion group."""
    return _api("POST", f"/api/views/{int(view_id)}/tap", {})


@mcp.tool()
def set_expression(view_id: int, name: str) -> dict:
    """Set a facial expression (name from the model's expressions list, see model_info/game_status)."""
    return _api("POST", f"/api/views/{int(view_id)}/expression", {"name": name})


@mcp.tool()
def look_at(view_id: int, x: float, y: float, seconds: float = 3.0) -> dict:
    """Make the partner look toward a point for `seconds`. x, y are the look-target coordinates
    (clamped to -1..1; 0,0 = straight ahead; try values and take a screenshot to see the direction)."""
    return _api("POST", f"/api/views/{int(view_id)}/look", {"x": float(x), "y": float(y), "seconds": float(seconds)})


@mcp.tool()
def close_view(view_id: int) -> dict:
    """Close a live view (e.g. a preview overlay)."""
    return _api("POST", f"/api/views/{int(view_id)}/close", {})


@mcp.tool()
def screenshot(save_path: Optional[str] = None) -> Any:
    """Screenshot of the whole running game screen (PNG), returned as an image. If save_path is
    given the PNG is also saved there."""
    png, err = _api_png("/api/screenshot.png")
    if err:
        return err
    return _png_result(png, save_path)


@mcp.tool()
def view_frame(view_id: int, save_path: Optional[str] = None) -> Any:
    """The rendered frame (PNG with transparency) of one live Live2D view, returned as an image.
    If save_path is given the PNG is also saved there."""
    png, err = _api_png(f"/api/views/{int(view_id)}/frame.png")
    if err:
        return err
    return _png_result(png, save_path)


# ======================================================================================
# OFFLINE TOOLS (disk)
# ======================================================================================

@mcp.tool()
def list_partners() -> dict:
    """List the custom partners installed on disk (Package\\LocalAssets\\PartnerLive2D\\*\\partner.ini):
    name, naviCharaId, partnerId, motion/expression mapping, whether the model file exists and
    whether the partner was created by this MCP, plus the model's motion groups (with counts),
    expressions and hit areas parsed from its model3.json."""
    root = _partners_root()
    items = []
    for d in _all_partner_dirs():
        try:
            items.append(_partner_summary(d))
        except Exception as e:  # noqa: BLE001
            items.append({"name": d.name, "folder": str(d), "error": str(e)})
    return {"ok": True, "root": str(root), "count": len(items), "partners": items}


@mcp.tool()
def get_partner(name: str) -> dict:
    """Full on-disk details of one partner: parsed partner.ini values, the raw ini text, the model's
    motion groups (with file lists), expressions, hit areas and any missing model files."""
    try:
        d = _existing_partner_dir(name)
    except (ValueError, FileNotFoundError) as e:
        return _err(str(e))
    s = _partner_summary(d, with_model=False)
    if not s.get("hasIni"):
        return _err(f"{d} has no partner.ini")
    s["iniText"] = "\n".join(_read_lines(d / "partner.ini"))
    if s.get("modelPath"):
        s["model"] = _parse_model3(Path(s["modelPath"]))
    s["ok"] = True
    return s


@mcp.tool()
def model_info(model3_json_path: str) -> dict:
    """Inspect any Live2D *.model3.json (or a folder containing one): motion groups with their
    file lists, expressions, hit areas, texture count and referenced files that are missing."""
    p = Path(model3_json_path)
    if p.is_dir():
        found, warns = _find_model3(p)
        if not found:
            return _err(f"no *.model3.json in {p}")
        p = found
    info = _parse_model3(p)
    info["ok"] = "error" not in info
    return info


def _used_ids(skip_name: Optional[str]) -> tuple[set[int], set[int]]:
    partner_ids: set[int] = set()
    navi_ids: set[int] = set()
    for d in _all_partner_dirs():
        if skip_name and d.name == skip_name:
            continue
        vals = dict(_read_ini_pairs(d / "partner.ini"))
        for key, bucket in (("partnerId", partner_ids), ("naviCharaId", navi_ids)):
            try:
                bucket.add(int(vals[key]))
            except (KeyError, ValueError):
                pass
    return partner_ids, navi_ids


def _pick_default_motions(groups: list[str]) -> tuple[dict[str, str], str, str]:
    def first(pattern: str, exclude: Optional[str] = None) -> Optional[str]:
        for g in groups:
            if re.search(pattern, g, re.I) and not (exclude and re.search(exclude, g, re.I)):
                return g
        return None

    idle = first(r"idle") or ""
    tap = first(r"tap") or first(r"touch|body|click") or next((g for g in groups if g != idle), "")
    m = {"Default": idle, "Welcom": tap, "FunStart": tap, "FunStartLoop": idle, "Sad01": idle}
    return m, idle, tap


@mcp.tool()
def create_partner(
    name: str,
    model_dir: str,
    navi_chara_id: int,
    display_name: str,
    partner_id: Optional[int] = None,
    replace_existing: bool = False,
    scale: float = 1.0,
    motions: Optional[dict] = None,
    overwrite: bool = False,
) -> dict:
    """Create a new custom partner on disk (game should not need to be running).

    name: folder name (ASCII letters/digits/-/_). model_dir: folder holding the Live2D model (or the
    model3.json itself); the WHOLE folder is copied into <partner>\\model\\. display_name: shown in-game.
    navi_chara_id: navi chara the model draws for. Ids 0..28 belong to the stock game: refused unless
    replace_existing=True, which makes the model replace that stock navi chara (then no Partner.xml is
    written and partner_id is optional/only used for stock icons). For a NEW partner pick an unused id
    >= 29 (e.g. 90); partner_id None = automatic (next free id >= 501; 1..38 are stock and refused).
    A new partner's Partner.xml (UTF-8 BOM) is written to Sinmai_Data\\StreamingAssets\\A500\\partner\\
    partner<6 digits>\\ from the mod's template: the game only reads it at startup, so RESTART the game.
    scale: initial scale. motions: optional {State: motion group} overrides, State in Default, Welcom,
    FunStart, FunStartLoop, Sad01; defaults come from the model's groups (Default/FunStartLoop/Sad01 =
    idle-like group, Welcom/FunStart = tap-like group). overwrite=True replaces an existing folder of
    the same name. Writes a '# created_by = mcp' comment into partner.ini (used by delete_partner)."""
    # ---- validation (nothing is written before everything checks out) ----
    if not isinstance(name, str) or not NAME_RE.match(name):
        return _err("name must be ASCII letters/digits/-/_ (max 64 chars)")
    display_name = (display_name or "").strip()
    if not display_name or "\n" in display_name or "\r" in display_name:
        return _err("display_name must be a non-empty single line")
    try:
        navi = int(navi_chara_id)
        scale_f = float(scale)
    except (TypeError, ValueError):
        return _err("navi_chara_id must be an integer and scale a number")
    if not 0.05 <= scale_f <= 10:
        return _err("scale must be within 0.05..10")
    if navi < 0:
        return _err("navi_chara_id must be >= 0")

    dest = _partners_root() / name
    if dest.exists() and not overwrite:
        return _err(f"partner folder '{name}' already exists; pass overwrite=True to replace it", folder=str(dest))

    src = Path(model_dir)
    if not src.exists():
        return _err(f"model_dir not found: {src}")
    model3, warnings = _find_model3(src)
    if model3 is None:
        return _err(f"no *.model3.json found under {src}")
    src_dir = model3.parent if src.is_file() else src
    try:
        s_res, d_res = src_dir.resolve(), dest.resolve()
        if s_res == d_res or d_res in s_res.parents or s_res in d_res.parents:
            return _err("model_dir overlaps the target partner folder; copy the model somewhere else first")
    except OSError:
        pass
    mi = _parse_model3(model3)
    if "error" in mi:
        return _err(f"model3.json problem: {mi['error']}")
    if mi["missingFiles"]:
        warnings.append("model3.json references missing files: " + ", ".join(mi["missingFiles"][:10]))
    groups = list(mi["motionGroups"].keys())

    used_partner, used_navi = _used_ids(skip_name=name if overwrite else None)
    stock_navi = navi in STOCK_NAVI_IDS
    write_xml = False
    pid: Optional[int] = None
    if replace_existing:
        if not stock_navi:
            return _err("replace_existing is only meant for stock navi chara ids 0..28; for a new partner leave it False")
        if partner_id is not None:
            if int(partner_id) not in STOCK_PARTNER_IDS:
                return _err("with replace_existing, partner_id (if given) must be a stock partner id 1..38 (used for its icon)")
            pid = int(partner_id)
    else:
        if stock_navi:
            return _err(f"navi_chara_id {navi} belongs to a stock partner (0..28). Pass replace_existing=True to replace it, or pick an unused id.")
        if navi in used_navi:
            return _err(f"navi_chara_id {navi} is already used by another custom partner")
        write_xml = True
        if partner_id is None:
            pid = FIRST_CUSTOM_PARTNER_ID
            while pid in used_partner or (_partner_xml_dir(pid) / "Partner.xml").exists():
                pid += 1
        else:
            pid = int(partner_id)
            if pid in STOCK_PARTNER_IDS:
                return _err(f"partner_id {pid} belongs to a stock partner (1..38)")
            if not 1 <= pid <= 999999:
                return _err("partner_id must be within 1..999999")
            if pid in used_partner:
                return _err(f"partner_id {pid} is already used by another custom partner")
            if (_partner_xml_dir(pid) / "Partner.xml").exists():
                # only OK if it is the xml of the very partner we are overwriting
                own = False
                if overwrite and (dest / "partner.ini").exists():
                    old = dict(_read_ini_pairs(dest / "partner.ini"))
                    own = old.get("partnerId") == str(pid)
                if not own:
                    return _err(f"a Partner.xml for id {pid} already exists in the game data; choose another partner_id")

    # motion mapping
    default_map, idle, tap = _pick_default_motions(groups)
    if motions:
        for k, g in motions.items():
            state = k[7:] if str(k).startswith("motion.") else str(k)
            if state not in STATES:
                return _err(f"motions: unknown state '{k}', use one of {', '.join(STATES)}")
            if g and groups and g not in groups:
                return _err(f"motions: group '{g}' not in the model (groups: {', '.join(groups)})")
            default_map[state] = g or ""

    xml_text = None
    if write_xml:
        tpl = _template_xml()
        if not tpl.exists():
            return _err(f"Partner.xml template not found: {tpl}")
        xml_text = _render_partner_xml(tpl.read_bytes().decode("utf-8-sig"), pid, navi, display_name)

    # ---- writes ----
    created: list[Path] = []
    try:
        if dest.exists():
            _remove_mcp_xml(dest)
            _rmtree(dest)
        dest.mkdir(parents=True)
        created.append(dest)
        shutil.copytree(src_dir, dest / "model")
        rel_model = (dest / "model" / model3.relative_to(src_dir)).relative_to(dest).as_posix()

        L = [
            f"# Live2D partner: {display_name} (created by the PartnerLive2D MCP server)",
            "# created_by = mcp",
            "# Paths are relative to this folder.",
            f"model = {rel_model}",
            "",
            "# naviChara id this model draws for (0-28 = stock partners, others = new partner).",
            f"naviCharaId = {navi}",
        ]
        if pid is not None:
            L.append(f"partnerId = {pid}")
        L += [
            "",
            "# optional pictures for the collection card / result photo (PNG)",
            "icon =",
            "resultImage =",
            "",
            "# game state -> motion group of the model (\"\" = keep idling)",
            "#   Default      result screen opens",
            "#   Welcom       partner room greeting",
            "#   FunStart     cleared the song (once)",
            "#   FunStartLoop cleared, looping afterwards",
            "#   Sad01        failed the song (loops)",
        ]
        for s in STATES:
            L.append(f"motion.{s} = {default_map[s]}")
        if idle:
            L.append(f"idle = {idle}")
        if tap:
            L.append(f"tap = {tap}")
        L += [
            "",
            "# expression.<state> = <expression name> (model has: "
            + (", ".join(mi["expressions"]) if mi["expressions"] else "none") + ")",
            "",
            "# size and position, relative to the stock partner's height",
            f"scale = {scale_f:g}",
            "offsetX = 0",
            "offsetY = 0",
            "renderSize = 1024",
            "flipY = false",
            "lookStrength = 1.0",
        ]
        _write_lines(dest / "partner.ini", L)

        xml_path = None
        if write_xml:
            xd = _partner_xml_dir(pid)
            existed = xd.exists()
            xd.mkdir(parents=True, exist_ok=True)
            if not existed:
                created.append(xd)
            xml_path = xd / "Partner.xml"
            xml_path.write_bytes(b"\xef\xbb\xbf" + xml_text.encode("utf-8"))
    except Exception as e:  # noqa: BLE001
        for p in reversed(created):
            try:
                _rmtree(p)
            except OSError:
                pass
        return _err(f"create failed, rolled back: {type(e).__name__}: {e}")

    res: dict[str, Any] = {
        "ok": True,
        "name": name,
        "folder": str(dest),
        "ini": str(dest / "partner.ini"),
        "model": rel_model,
        "naviCharaId": navi,
        "partnerId": pid,
        "partnerXml": str(xml_path) if xml_path else None,
        "motions": {s: default_map[s] for s in STATES},
        "motionGroups": groups,
        "expressions": mi["expressions"],
        "hitAreas": mi["hitAreas"],
        "warnings": warnings,
    }
    if write_xml:
        res["next"] = "New partner: Partner.xml is only read at game start, so (re)start the game. Then use preview_partner to look at it."
    else:
        res["next"] = "Replaces a stock navi chara: if the game is running call rescan_partners (or restart), then preview_partner."
    return res


def _render_partner_xml(template: str, partner_id: int, navi_id: int, display: str) -> str:
    esc = (display.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;"))
    out = re.sub(r"<dataName>[^<]*</dataName>", f"<dataName>partner{partner_id:06d}</dataName>", template, count=1)

    def block(tag: str, ident: int, text: str, s: str) -> str:
        pat = re.compile(r"(<" + tag + r">\s*<id>)[^<]*(</id>\s*<str>)[^<]*(</str>)")
        new, n = pat.subn(lambda m: f"{m.group(1)}{ident}{m.group(2)}{text}{m.group(3)}", s, count=1)
        if n != 1:
            raise ValueError(f"Partner.xml template has no <{tag}> block")
        return new

    out = block("name", partner_id, esc, out)
    out = block("naviChara", navi_id, esc, out)
    return out


def _remove_mcp_xml(folder: Path) -> Optional[str]:
    """Delete the Partner.xml belonging to an MCP-created partner folder. Returns a message."""
    ini = folder / "partner.ini"
    if not ini.exists():
        return None
    lines = _read_lines(ini)
    if not any(MARKER_RE.match(l) for l in lines):
        return None
    try:
        pid = int(dict(_read_ini_pairs(ini)).get("partnerId", ""))
    except ValueError:
        return None
    if pid in STOCK_PARTNER_IDS:
        return None
    for d in _all_partner_dirs():
        if d == folder:
            continue
        try:
            if int(dict(_read_ini_pairs(d / "partner.ini")).get("partnerId", "")) == pid:
                return f"kept Partner.xml for id {pid}: another partner folder ({d.name}) uses it"
        except ValueError:
            pass
    xd = _partner_xml_dir(pid)
    xml = xd / "Partner.xml"
    if not xml.exists():
        return None
    if f"<dataName>partner{pid:06d}</dataName>" not in xml.read_bytes().decode("utf-8-sig"):
        return f"kept {xml}: dataName does not match"
    xml.unlink()
    try:
        xd.rmdir()
    except OSError:
        pass
    return f"removed {xml}"


@mcp.tool()
def delete_partner(name: str, confirm: bool = False) -> dict:
    """Delete a custom partner folder from disk. Requires confirm=True. If the partner was created by
    create_partner (it has a '# created_by = mcp' line in partner.ini) its Partner.xml in the game
    data is removed too; partners made by hand keep their Partner.xml. A running game keeps showing the
    partner until it is restarted / rescanned."""
    if not confirm:
        return _err("refusing to delete without confirm=True")
    try:
        d = _existing_partner_dir(name)
    except (ValueError, FileNotFoundError) as e:
        return _err(str(e))
    if d.resolve().parent != _partners_root().resolve():
        return _err("refusing: folder is not a direct child of the PartnerLive2D folder")
    msgs = []
    mcp_made = False
    ini = d / "partner.ini"
    if ini.exists():
        mcp_made = any(MARKER_RE.match(l) for l in _read_lines(ini))
        m = _remove_mcp_xml(d)
        if m:
            msgs.append(m)
    try:
        _rmtree(d)
    except OSError as e:
        return _err(f"could not remove folder (files in use by the game?): {e}", notes=msgs)
    return {"ok": True, "deleted": str(d), "createdByMcp": mcp_made, "notes": msgs}


@mcp.tool()
def build_and_install() -> dict:
    """Build the mod (dotnet, build.ps1 -Install) and install it plus the deploy files into the game
    package. Refuses while Sinmai.exe from the package folder is running (checked by executable path;
    nothing is ever killed - stop the game yourself, e.g. test_game_stop). Returns the tail of the build output."""
    try:
        running = _sinmai_running()
    except RuntimeError as e:
        return _err("cannot verify that the game is stopped: " + str(e))
    if running:
        return _err("game is running (Sinmai.exe from the package folder): stop it first, nothing was built", running=running)
    script = _project() / "build.ps1"
    if not script.exists():
        return _err(f"build.ps1 not found at {script}")
    rc, out = _run(
        ["powershell", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(script), "-Install", "-Package", str(_package())],
        timeout=900,
        cwd=_project(),
    )
    return {"ok": rc == 0, "returncode": rc, "package": str(_package()), "output_tail": _tail(out, 40)}


@mcp.tool()
def read_mod_log(lines: int = 80, filter: Optional[str] = None) -> dict:
    """Tail of the MelonLoader log (Package\\MelonLoader\\Latest.log). `filter`: optional
    case-insensitive substring; only matching lines are considered (the last `lines` of them are returned)."""
    log = _package() / "MelonLoader" / "Latest.log"
    if not log.exists():
        return _err(f"log not found: {log}")
    try:
        text = _decode(log.read_bytes())
    except OSError as e:
        return _err(f"cannot read log: {e}")
    ls = text.splitlines()
    if filter:
        f = filter.lower()
        ls = [l for l in ls if f in l.lower()]
    n = max(1, min(int(lines), 5000))
    return {"ok": True, "log": str(log), "total_matching": len(ls), "text": "\n".join(ls[-n:])}


# ======================================================================================
# TEST-INSTANCE DRIVING (tools\drive.ps1)
# ======================================================================================

KEY_RE = re.compile(r"^[A-Za-z][A-Za-z0-9]{0,23}$")
SEQ_RE = re.compile(r"^[A-Za-z][A-Za-z0-9]{0,23}(\*\d{1,3})?(,[A-Za-z][A-Za-z0-9]{0,23}(\*\d{1,3})?)*$")


def _drive(args: list[str], timeout: float) -> tuple[int, str]:
    script = _project() / "tools" / "drive.ps1"
    if not script.exists():
        return 2, f"drive.ps1 not found at {script}"
    return _run(
        ["powershell", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(script), *args, "-Package", str(_package())],
        timeout=timeout,
        cwd=_project() / "tools",
    )


def _drive_result(rc: int, out: str) -> dict:
    return {"ok": rc == 0, "returncode": rc, "output": _tail(out, 20)}


@mcp.tool()
def test_game_start() -> dict:
    """Start a test instance of the game (tools\\drive.ps1 start): windowed 540x960, audio muted,
    shared-mode audio, launched via inject.exe + Sinmai.exe from the package folder. Refuses if a
    Sinmai.exe from the package is already running. The mod's control API comes up some tens of
    seconds later - poll game_status until it answers. Pair with test_game_stop (restores config/mute)."""
    try:
        running = _sinmai_running()
    except RuntimeError as e:
        return _err("cannot verify that no game is running: " + str(e))
    if running:
        return _err("a game from this package is already running", running=running)
    return _drive_result(*_drive(["start"], 120))


@mcp.tool()
def test_game_keys(sequence: str) -> dict:
    """Send key presses to the test game window. Safety: the window is brought to the front first and
    sending ABORTS the moment any other window has focus.

    sequence: comma separated key names with optional *repeat, e.g. "C,D*3" (each tap = 120 ms down,
    650 ms pause). Names are System.Windows.Forms.Keys names. maimai key map: buttons 1-8 =
    W,E,D,C,X,Z,A,Q (clockwise starting at the top-right); Enter held 3 s = tap the Aime card
    (use test_game_hold); Home = autoplay toggle; Pause held 2 s = skip song (test_game_hold)."""
    if not SEQ_RE.match(sequence or ""):
        return _err("sequence must look like 'C,D*3' (Keys names, optional *count)")
    n = sum(int(p.split("*")[1]) if "*" in p else 1 for p in sequence.split(","))
    return _drive_result(*_drive(["keys", sequence], 60 + n * 1.0))


@mcp.tool()
def test_game_hold(key: str, ms: int = 3000) -> dict:
    """Hold one key down for `ms` milliseconds in the test game window (aborts and releases the key if
    the window loses focus). maimai: Enter held 3000 ms = tap the Aime card (log in / start);
    Pause held 2000 ms = skip the current song; Home = autoplay toggle (tap). Buttons 1-8 =
    W,E,D,C,X,Z,A,Q clockwise from top-right."""
    if not KEY_RE.match(key or ""):
        return _err("key must be a System.Windows.Forms.Keys name such as Enter, Home, Pause, C")
    ms = int(ms)
    if not 10 <= ms <= 30000:
        return _err("ms must be within 10..30000")
    return _drive_result(*_drive(["key", key, str(ms)], 60 + ms / 1000))


@mcp.tool()
def test_game_shot() -> Any:
    """Screenshot of the test game window's client area (desktop capture via drive.ps1, so it also
    works when the control API is down), returned as an image. The window is brought to the front."""
    import tempfile
    fd, tmp = tempfile.mkstemp(suffix=".png", prefix="pl2d_shot_")
    os.close(fd)
    try:
        rc, out = _drive(["shot", tmp], 60)
        data = Path(tmp).read_bytes() if rc == 0 and os.path.exists(tmp) else b""
    finally:
        try:
            os.unlink(tmp)
        except OSError:
            pass
    if rc != 0 or data[:8] != b"\x89PNG\r\n\x1a\n":
        return _err("screenshot failed", output=_tail(out, 10))
    return Image(data=data, format="png")


@mcp.tool()
def test_game_stop() -> dict:
    """Stop the test game started by test_game_start (by PID, never by image name) and restore
    AquaMai.toml, the audio mute state and remove test partner folders."""
    return _drive_result(*_drive(["stop"], 60))


# ======================================================================================
# EXTENSION TOOLS: the mod's other HTTP routes (ngo_api.py) and the tools\ scripts (ngo_jobs.py)
# ======================================================================================

def _register_extensions() -> None:
    """Load the extension modules next to this file. A broken extension must not take the core tools
    above down with it, so a failure is reported on stderr and the server runs without them."""
    import sys
    import traceback
    here = str(Path(__file__).resolve().parent)
    if here not in sys.path:
        sys.path.insert(0, here)
    this = sys.modules[__name__]
    for name in ("ngo_api", "ngo_jobs"):
        try:
            __import__(name).register(mcp, this)
        except Exception:  # noqa: BLE001
            sys.stderr.write(f"partnerlive2d: extension {name} not loaded:\n{traceback.format_exc()}\n")


_register_extensions()


if __name__ == "__main__":
    mcp.run()
