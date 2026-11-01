"""PartnerLive2D MCP: wrappers for the project's tools\\ scripts.

Quick, read-only reports run synchronously (play_report, heapwalk_diff). The off-screen game tests run for
minutes, so they are background JOBS: the tool starts the script through job_boot.py and returns at once;
job_status polls it, job_stop stops it cooperatively (the script's own cleanup runs). Every game job:
  * refuses to start while any Sinmai.exe of the package runs (same rule as test_game_start);
  * runs muted / windowed through tools\\drive.ps1 (inside the script) and is kept parked off screen;
  * snapshots settings.ini first, so a job that had to be killed can have the test-only keys put back.
Processes are only ever stopped by their exact PID, checked against the creation time recorded at start.
"""
from __future__ import annotations

import ctypes
import ctypes.wintypes as wt
import json
import os
import re
import shutil
import subprocess
import time
from pathlib import Path
from typing import Any, Optional

import ngo_common as C

HERE = Path(__file__).resolve().parent
BOOT = HERE / "job_boot.py"

# off-screen game scripts run_offscreen_test may start (each starts and stops its own drive.ps1 game)
OFFSCREEN_SCRIPTS = {
    "ngo_sprite_test.py": "sprite partner on select / Menu (Confirm) / START (Enter) / result tiers + taps",
    "ngo_full_walk.py": "whole flow attract -> login -> every select phase -> track start -> play -> result -> logout",
    "ngo_skin_walk.py": "attract -> login -> every screen to music select, one shot each (skin check)",
    "phase_shots.py": "login, then every music-select phase captured",
    "ngo_attract_check.py": "attract screen only, no login: processes + a frame every CHECK_DT s for <seconds>",
}
# environment switches those scripts read
SCRIPT_ENV = {"DRIVE_FULL_ENTRY", "DRIVE_KEEP_TIMEOUT", "DRIVE_NO_SERVER", "BURST", "BURST_DT", "BURST_AT", "BURST_NOW",
              "ATTRACT_SECONDS", "SELECTPERF", "TST_FRAMES", "TST_DT", "MODE_RIGHT", "TESTINPUT", "BOOT_SHOTS",
              "CAT_LEFT", "CAT_BURST", "SELECT_ONLY", "DIFF_LEFT", "LEFT_KEY", "PLAY_DT", "PLAY_MAX", "CHECK_DT"}
# settings.ini keys the test scripts switch on for a run (put back after a killed job)
TEST_KEYS = ("testInput", "frameLog", "testFirstPlay", "inputLog", "selectPartner")

# vkeys names -> (virtual key, default hold ms). Buttons 1-8 = W E D C X Z A Q clockwise from top-right.
VKEYS = {"scan": (0x0D, 3000), "W": (0x57, 180), "E": (0x45, 180), "D": (0x44, 180), "C": (0x43, 180),
         "X": (0x58, 180), "Z": (0x5A, 180), "A": (0x41, 180), "Q": (0x51, 180),
         "Test": (0x70, 180), "Service": (0x71, 180), "Coin": (0x72, 180)}
LABEL_RE = re.compile(r"^[A-Za-z0-9_.-]{1,40}$")
JOB_RE = re.compile(r"^[0-9]{8}-[0-9]{6}-[a-z_]{1,24}$")

STILL_ACTIVE = 259


# --------------------------------------------------------------------------------------
# processes (exact PID + creation time)
# --------------------------------------------------------------------------------------

_k32 = ctypes.WinDLL("kernel32", use_last_error=True) if os.name == "nt" else None
if _k32:
    _k32.OpenProcess.restype = wt.HANDLE
    _k32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
    _k32.GetProcessTimes.argtypes = [wt.HANDLE] + [ctypes.POINTER(wt.FILETIME)] * 4
    _k32.GetExitCodeProcess.argtypes = [wt.HANDLE, ctypes.POINTER(wt.DWORD)]
    _k32.TerminateProcess.argtypes = [wt.HANDLE, wt.UINT]
    _k32.CloseHandle.argtypes = [wt.HANDLE]


def _proc_created(pid: int, access: int = 0x1000) -> tuple[Optional[int], Optional[int]]:
    """(creation FILETIME, handle) of a live process, (None, None) when it is gone."""
    if not _k32 or not pid:
        return None, None
    h = _k32.OpenProcess(access, False, int(pid))
    if not h:
        return None, None
    c, e, k, u = wt.FILETIME(), wt.FILETIME(), wt.FILETIME(), wt.FILETIME()
    code = wt.DWORD()
    if not _k32.GetProcessTimes(h, ctypes.byref(c), ctypes.byref(e), ctypes.byref(k), ctypes.byref(u)) \
            or not _k32.GetExitCodeProcess(h, ctypes.byref(code)) or code.value != STILL_ACTIVE:
        _k32.CloseHandle(h)
        return None, None
    return (c.dwHighDateTime << 32) | c.dwLowDateTime, h


def _alive(pid: int, created: Optional[int]) -> bool:
    ct, h = _proc_created(pid)
    if h:
        _k32.CloseHandle(h)
    return ct is not None and (created is None or ct == created)


def _kill_exact(pid: int, created: Optional[int]) -> bool:
    """TerminateProcess on this PID only if it is still the process we started (same creation time)."""
    ct, h = _proc_created(pid, 0x1000 | 0x0001)
    if not h:
        return False
    try:
        if created is not None and ct != created:
            return False
        return bool(_k32.TerminateProcess(h, 1))
    finally:
        _k32.CloseHandle(h)


# --------------------------------------------------------------------------------------
# job bookkeeping
# --------------------------------------------------------------------------------------

def _jobs_root() -> Path:
    return C.state_dir() / "jobs"


def _job_dir(job_id: Optional[str]) -> Optional[Path]:
    root = _jobs_root()
    if job_id:
        if not JOB_RE.match(job_id):
            return None
        d = root / job_id
        return d if (d / "job.json").exists() else None
    if not root.is_dir():
        return None
    dirs = sorted((d for d in root.iterdir() if (d / "job.json").exists()), key=lambda d: d.name)
    return dirs[-1] if dirs else None


def _read_json(p: Path) -> Optional[dict]:
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def _job_info(d: Path, tail: int = 0) -> dict:
    meta = _read_json(d / "job.json") or {}
    ex = _read_json(d / "exit.json")
    alive = _alive(meta.get("pid", 0), meta.get("created"))
    if alive:
        state = "running"
    elif ex is None:
        state = "died"           # killed, or crashed before job_boot could write exit.json
    elif ex.get("stopped"):
        state = "stopped"
    else:
        state = "finished" if ex.get("exit") == 0 else "failed"
    info = {"job": d.name, "kind": meta.get("kind"), "state": state, "pid": meta.get("pid"),
            "started": meta.get("started"), "outDir": meta.get("outDir"), "log": str(d / "output.log"),
            "startsGame": meta.get("startsGame", False)}
    if ex:
        info["exit"] = ex
    if meta.get("note"):
        info["note"] = meta["note"]
    if tail:
        try:
            lines = (d / "output.log").read_bytes().decode("utf-8", errors="replace").splitlines()
            info["logTail"] = "\n".join(lines[-tail:])
        except OSError:
            info["logTail"] = ""
    return info


def _running_game_jobs() -> list[dict]:
    root = _jobs_root()
    if not root.is_dir():
        return []
    out = []
    for d in root.iterdir():
        if (d / "job.json").exists():
            i = _job_info(d)
            if i["state"] == "running" and i["startsGame"]:
                out.append(i)
    return out


def _start_job(kind: str, script_name: str, args: list[str], env_extra: Optional[dict], out_dir: Optional[str],
               note: str = "") -> dict:
    err = C.scripts_compatible(kind) or C.guard_no_game(kind)
    if err:
        return err
    busy = _running_game_jobs()
    if busy:
        return C.core._err(f"{kind}: another game job is running ({busy[0]['job']}); stop it first with job_stop", running=busy)
    if C.drive_state_path().exists():
        return C.core._err(f"{kind}: {C.drive_state_path()} exists, so an earlier test run was not stopped (AquaMai.toml and "
                           "the mute may still be in test mode). Check game_processes, then run test_game_stop once.")
    script = C.tools_dir() / script_name
    if not script.exists():
        return C.core._err(f"{kind}: script not found: {script}")
    if not BOOT.exists():
        return C.core._err(f"job_boot.py missing next to the MCP server: {BOOT}")
    for _ in range(3):
        job_id = f"{C.stamp()}-{kind}"
        d = _jobs_root() / job_id
        if not d.exists():
            break
        time.sleep(1.1)
    else:
        return C.core._err("could not pick a job id")
    d.mkdir(parents=True)
    out = Path(out_dir) if out_dir else d / "out"
    out.mkdir(parents=True, exist_ok=True)
    full_args = [str(a) for a in args]
    full_args = [str(out) if a == "{out}" else a for a in full_args]
    try:
        if C.settings_path().exists():
            shutil.copy2(C.settings_path(), d / "settings.ini.snapshot")
    except OSError:
        pass
    env = os.environ.copy()
    env.update({"PYTHONIOENCODING": "utf-8", "PYTHONUNBUFFERED": "1"})
    env.update({k: str(v) for k, v in (env_extra or {}).items()})
    argv = [C.python_exe(), "-u", str(BOOT), str(d), str(C.drive_state_path()), str(script), *full_args]
    log = open(d / "output.log", "wb")
    no_window, breakaway = 0x08000000, 0x01000000
    try:
        try:
            p = subprocess.Popen(argv, stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL,
                                 cwd=str(C.tools_dir()), env=env, creationflags=no_window | breakaway)
        except OSError:   # the parent's job object may not allow breakaway
            p = subprocess.Popen(argv, stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL,
                                 cwd=str(C.tools_dir()), env=env, creationflags=no_window)
    finally:
        log.close()
    created, h = _proc_created(p.pid)
    if h:
        _k32.CloseHandle(h)
    meta = {"kind": kind, "script": str(script), "args": full_args, "env": {k: str(v) for k, v in (env_extra or {}).items()},
            "pid": p.pid, "created": created, "started": time.strftime("%Y-%m-%d %H:%M:%S"), "outDir": str(out),
            "startsGame": True, "note": note}
    (d / "job.json").write_text(json.dumps(meta, indent=1), encoding="utf-8")
    return {"ok": True, "job": job_id, "pid": p.pid, "outDir": str(out), "log": str(d / "output.log"),
            "next": "poll job_status(job) every 30-60 s; job_stop(job) stops it and the test game", **({"note": note} if note else {})}


def _restore_test_keys(d: Path) -> Optional[dict]:
    """After a killed job: put the test-only settings.ini keys back to their values in the start snapshot."""
    snap = d / "settings.ini.snapshot"
    cur_p = C.settings_path()
    if not snap.exists() or not cur_p.exists():
        return None
    try:
        before = C.ini_values(snap.read_bytes().decode("utf-8-sig"))
        raw = cur_p.read_bytes()
        bom = raw.startswith(b"\xef\xbb\xbf")
        text = raw.decode("utf-8-sig")
        now = C.ini_values(text)
        wanted = {k: before.get(k) for k in TEST_KEYS if before.get(k) != now.get(k)}
        if not wanted:
            return {"settingsRestored": []}
        new = C.ini_set_keys(text, wanted)
        cur_p.write_bytes((b"\xef\xbb\xbf" if bom else b"") + new.encode("utf-8"))
        return {"settingsRestored": sorted(wanted)}
    except OSError as e:
        return {"settingsRestoreError": str(e)}


def _stop_test_game() -> dict:
    """drive.ps1 stop, but only when the running package game is the drive.ps1 test instance."""
    st = C.read_drive_state()
    if not st:
        return {"testGame": "none (no drive-state.json: the script's own cleanup already ran drive.ps1 stop)"}
    try:
        g = C.classify_games()
    except RuntimeError as e:
        return {"testGame": f"not stopped: cannot list processes ({e})"}
    if g["other"]:
        return {"testGame": "not stopped: another game (not started by drive.ps1) is running; stop the test instance by hand by its PID",
                "running": g["processes"]}
    if not g["test"]:
        return {"testGame": "not running, but drive-state.json is left over (AquaMai.toml / mute not restored yet): "
                            "run test_game_stop once"}
    rc, out = C.core._drive(["stop"], 90)
    return {"testGame": "stopped" if rc == 0 else "drive.ps1 stop failed", "driveOutput": C.core._tail(out, 8)}


# ======================================================================================
# tools
# ======================================================================================

def game_processes() -> dict:
    """Which maimai processes run right now: every Sinmai.exe of the package (pid, path, testInstance = started
    by tools\\drive.ps1, per tools\\drive-state.json) and the running MCP game jobs. A Sinmai.exe that is not the
    test instance is someone's real game: test tools refuse to touch it and nothing may stop it."""
    try:
        g = C.classify_games()
    except RuntimeError as e:
        return C.core._err(str(e))
    return {"ok": True, "processes": g["processes"], "testInstanceRunning": bool(g["test"]),
            "otherGameRunning": bool(g["other"]), "driveState": g["driveState"], "jobs": _running_game_jobs()}


def play_report(log_path: Optional[str] = None, monitor_csv: Optional[str] = None) -> dict:
    """Per-track report of a real play session (tools\\ngo_play_report.py), read-only, safe while the game runs:
    for every chart the start time, song, difficulty, quick retries, frames over 25 / 50 ms, worst frame, chart
    load ms, fade->song / end->result / result->select load seconds, the longest cleanup and the managed heap
    left after it, and the process memory sampled from outside; then the trends (per-track slope) over the
    session. Inputs: the MelonLoader log ([proc], [play] from PlayStats.cs, [clean] from NgoCleanupTiming.cs;
    default = newest Package\\MelonLoader\\Logs\\*.log) and the launcher's monitor CSV (playlogs\\monitor_<game
    start>.csv by play-monitor.ps1; default = the one matching the log). Column headers are Chinese."""
    script = C.tools_dir() / "ngo_play_report.py"
    if not script.exists():
        return C.core._err(f"script not found: {script}")
    if log_path is None and monitor_csv is not None:
        logs = sorted((C.core._package() / "MelonLoader" / "Logs").glob("*.log"), key=lambda p: p.stat().st_mtime)
        if not logs:
            return C.core._err("no MelonLoader log found; pass log_path")
        log_path = str(logs[-1])
    for p in (log_path, monitor_csv):
        if p is not None and not Path(p).is_file():
            return C.core._err(f"file not found: {p}")
    boot = ("import importlib.util, sys\n"
            "spec = importlib.util.spec_from_file_location('ngo_play_report', sys.argv[1])\n"
            "m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)\n"
            "m.PKG, m.PLAYLOGS = sys.argv[2], sys.argv[3]\n"
            "sys.argv = [sys.argv[1]] + sys.argv[4:]\n"
            "m.main()\n")
    argv = [C.python_exe(), "-X", "utf8", "-c", boot, str(script), str(C.core._package()), str(C.playlogs_dir())]
    argv += [a for a in (log_path, monitor_csv) if a]
    rc, out = C.core._run(argv, timeout=180, cwd=C.tools_dir())
    return {"ok": rc == 0, "returncode": rc, "report": out.strip()}


def heapwalk_diff(earlier: Optional[str] = None, later: Optional[str] = None, top: int = 25) -> dict:
    """Compare two heap walks (tools\\ngo_heapwalk_diff.py): heap after GC and reached bytes before -> after, then
    the roots (static fields / MonoBehaviours) and the types that grew most. Defaults: the two newest walk_*.tsv
    in PARTNERLIVE2D_STATE\\heapwalk (where test_heapwalk moves them) or the game's LocalAssets heapwalk folder.
    A root that grows by tens of MB per track is a leak (e.g. a ConditionalWeakTable, whose keys stay strong on
    this game's Boehm GC)."""
    script = C.tools_dir() / "ngo_heapwalk_diff.py"
    if not script.exists():
        return C.core._err(f"script not found: {script}")
    if earlier is None or later is None:
        cands = []
        for folder in (C.state_dir() / "heapwalk", C.assets_root() / "heapwalk"):
            if folder.is_dir():
                cands += list(folder.glob("walk_*.tsv"))
        cands.sort(key=lambda p: p.stat().st_mtime)
        if len(cands) < 2:
            return C.core._err("need two heap walks (test_heapwalk twice, a few tracks apart) or pass both paths")
        earlier, later = str(cands[-2]), str(cands[-1])
    for p in (earlier, later):
        if not Path(p).is_file():
            return C.core._err(f"file not found: {p}")
    try:
        top = max(1, min(int(top), 200))
    except (TypeError, ValueError):
        top = 25
    rc, out = C.core._run([C.python_exe(), "-X", "utf8", str(script), earlier, later, str(top)], timeout=120, cwd=C.tools_dir())
    return {"ok": rc == 0, "returncode": rc, "earlier": earlier, "later": later, "diff": out.strip()}


def soak_test(out_dir: Optional[str] = None, minutes: float = 25.0, play_seconds: float = 25.0,
              heapwalk_after_tracks: Optional[list[int]] = None) -> dict:
    """Background JOB (tools\\ngo_soak.py): the "gets slower the longer you play" soak, off screen and muted.
    Turns settings.ini testInput + frameLog on for the run (restored at the end), starts the test game, logs in
    with the card in DEVICE\\aime.txt, plays track after track with the game's autoplay, each skipped after
    play_seconds (AquaMai OneKeyRetrySkip = Service held), and samples the process (private MB, handles) every
    30 s next to the mod's [mem] lines. heapwalk_after_tracks e.g. [1, 3]: a heap walk on the song list after
    those tracks (then heapwalk_diff). One soak = one credit (OneKeyEntryEnd ends it after ~4 tracks).
    Off-screen runs at ~30 fps, so frame times are not representative - judge growth (heap, private MB).
    Outputs in out_dir: soak.csv, soak.log, game.log. Returns at once; poll job_status."""
    try:
        minutes, play_seconds = float(minutes), float(play_seconds)
    except (TypeError, ValueError):
        return C.core._err("minutes and play_seconds must be numbers")
    if not 1 <= minutes <= 240:
        return C.core._err("minutes must be within 1..240")
    if not 5 <= play_seconds <= 300:
        return C.core._err("play_seconds must be within 5..300")
    walks = heapwalk_after_tracks or []
    if not all(isinstance(t, int) and 0 < t < 1000 for t in walks):
        return C.core._err("heapwalk_after_tracks must be a list of positive track numbers")
    if not (C.game_root() / "auto-login.py").exists():
        return C.core._err(f"{C.game_root() / 'auto-login.py'} not found (the soak logs in with it)")
    env = {"PLAY": f"{play_seconds:g}"}
    if walks:
        env["WALK"] = ",".join(str(t) for t in walks)
    return _start_job("soak", "ngo_soak.py", ["{out}", f"{minutes:g}"], env, out_dir,
                      note="stopping waits for the current sleep (up to play_seconds)")


def select_tune(select_candidates: list[dict], result_candidates: Optional[list[dict]] = None,
                out_dir: Optional[str] = None) -> dict:
    """Background JOB (tools\\ngo_select_tune.py): side-by-side candidate sheets for the partner's size and place,
    off screen and muted. Logs in with the card, waits on the music list until the partner's sprites are loaded,
    applies each select candidate live (music-select partner rect), grabs the screen, puts the original rect back.
    select_candidates: [{"label": "h800 y-200", "height": 800, "bottomY": -200, "bottomX": 0}, ...]
      (height 100..2400, bottomY -1400..400, bottomX -600..600; label's first word names the file).
    result_candidates (optional): [{"label": "s0.9", "scale": 0.9, "offsetX": -0.15, "offsetY": 0.15,
      "resultUnder": "UI_DXRating/Base, UI_MusicData"}, ...] = partner.ini keys of the partner folder ngo_pair
      (the script's built-in partner); one chart is started and skipped, the result hierarchy is dumped
      (tree_near.json / tree_far.json) and each candidate is applied live and grabbed.
    Nothing is saved: values are restored, then logout. Writes cand_<label>.png, sheet.png (lower screens side by
    side), result_<label>.png, result_sheet.png, tune.log. Apply the winner with music_select_layout(save) /
    set_partner_placement(save=True). Returns at once; poll job_status."""
    if not isinstance(select_candidates, list) or not select_candidates:
        return C.core._err("select_candidates must be a non-empty list")
    ranges = {"height": (100, 2400), "bottomY": (-1400, 400), "bottomX": (-600, 600)}
    seen: set[str] = set()

    def label_ok(c: dict) -> Optional[str]:
        lab = c.get("label")
        if not isinstance(lab, str) or not lab.strip():
            return "every candidate needs a label"
        first = lab.split()[0]
        if not LABEL_RE.match(first):
            return f"label {lab!r}: its first word must be letters/digits/._- (it names the PNG)"
        if first in seen:
            return f"label {lab!r}: first words must be unique"
        seen.add(first)
        return None

    for c in select_candidates:
        if not isinstance(c, dict):
            return C.core._err("candidates must be objects")
        e = label_ok(c)
        if e:
            return C.core._err(e)
        keys = [k for k in c if k != "label"]
        if not keys or any(k not in ranges for k in keys):
            return C.core._err(f"select candidate {c.get('label')!r}: keys must be among height, bottomY, bottomX")
        for k in keys:
            v = c[k]
            if not isinstance(v, (int, float)) or isinstance(v, bool) or not ranges[k][0] <= v <= ranges[k][1]:
                return C.core._err(f"select candidate {c['label']!r}: {k} must be a number in {ranges[k]}")
    rlist = result_candidates or []
    if rlist:
        if not (C.assets_root() / "ngo_pair" / "partner.ini").exists():
            return C.core._err("result_candidates tune the partner folder 'ngo_pair' (built into the script), which is not installed")
        seen.clear()
        for c in rlist:
            if not isinstance(c, dict):
                return C.core._err("candidates must be objects")
            e = label_ok(c)
            if e:
                return C.core._err(e)
            for k, v in c.items():
                if k == "label":
                    continue
                try:
                    C.core._normalize_value(k, v)
                except ValueError as ex:
                    return C.core._err(f"result candidate {c['label']!r}: {ex}")
    tmp = C.state_dir() / "candidates" / C.stamp()
    tmp.mkdir(parents=True, exist_ok=True)
    sel = tmp / "select.json"
    sel.write_text(json.dumps(select_candidates), encoding="utf-8")
    args = ["{out}", str(sel)]
    if rlist:
        res = tmp / "result.json"
        res.write_text(json.dumps(rlist), encoding="utf-8")
        args.append(str(res))
    return _start_job("select_tune", "ngo_select_tune.py", args, None, out_dir)


def to_collection(out_dir: Optional[str] = None) -> dict:
    """Background JOB (tools\\ngo_to_collection.py): off screen and muted, log in with the card, start and skip
    the credit's tracks until the end-of-credit collection screen (CollectionProcess: partner room, icons...),
    grab collection_0.png and LEAVE THE TEST GAME RUNNING there for hand-driven checks with test_game_vkeys
    (e.g. ["X"], ["C"]) and screenshot / test_ui_dump. Finish with test_game_stop (or job_stop). The job state
    is "finished" once it has arrived; poll job_status."""
    return _start_job("to_collection", "ngo_to_collection.py", ["{out}"], None, out_dir,
                      note="the test game stays running when this finishes: end with test_game_stop")


def run_offscreen_test(script: str, out_dir: Optional[str] = None, args: Optional[list[str]] = None,
                       env: Optional[dict] = None) -> dict:
    """Background JOB: one of the project's other off-screen, muted test scripts (each starts and stops its own
    drive.ps1 test game; the window is kept parked off screen):
      ngo_sprite_test.py   - sprite partner: select Idle/Random, tap, Menu Confirm, START Enter, result tier + tap
      ngo_full_walk.py     - whole flow attract -> login -> select phases -> track start -> play -> result -> logout
      ngo_skin_walk.py     - attract -> login -> every screen up to music select (set env DRIVE_FULL_ENTRY=1)
      phase_shots.py       - login, every music-select phase
      ngo_attract_check.py - attract screen only, NO login; args ["<seconds>"], env CHECK_DT
    The out folder is passed as the first argument; `args` are appended. env: only the switches these scripts
    read (DRIVE_FULL_ENTRY, DRIVE_KEEP_TIMEOUT, DRIVE_NO_SERVER, BURST, BURST_DT, BURST_AT, BURST_NOW,
    ATTRACT_SECONDS, SELECTPERF, TST_FRAMES, TST_DT, MODE_RIGHT, TESTINPUT, BOOT_SHOTS, CAT_LEFT, CAT_BURST,
    SELECT_ONLY, DIFF_LEFT, LEFT_KEY, PLAY_DT, PLAY_MAX, CHECK_DT). All but ngo_attract_check log in with the
    card in DEVICE\\aime.txt. Returns at once; poll job_status."""
    if script not in OFFSCREEN_SCRIPTS:
        return C.core._err("script must be one of: " + ", ".join(sorted(OFFSCREEN_SCRIPTS)))
    extra = [str(a) for a in (args or [])]
    if any(("\n" in a or "\r" in a) for a in extra) or len(extra) > 8:
        return C.core._err("args: at most 8 single-line strings")
    env = env or {}
    bad = [k for k in env if k not in SCRIPT_ENV]
    if bad:
        return C.core._err("env keys not allowed: " + ", ".join(bad) + "; allowed: " + ", ".join(sorted(SCRIPT_ENV)))
    if any(("\n" in str(v) or "\r" in str(v)) for v in env.values()):
        return C.core._err("env values must be single-line")
    kind = re.sub(r"[^a-z_]", "", script[:-3].lower())[:24] or "test"
    return _start_job(kind, script, ["{out}", *extra], env, out_dir)


def test_game_vkeys(keys: list[str], shot: bool = True, save_path: Optional[str] = None,
                    between_seconds: float = 1.2) -> Any:
    """Press cabinet keys in the OFF-SCREEN test game without giving it focus (the tools\\ngo_key.py method:
    tools\\vkeys.py points amdaemon.exe's GetAsyncKeyState / GetForegroundWindow imports at a virtual key table,
    the real keyboard is ignored meanwhile, imports restored afterwards), then grab the screen through the mod.
    keys: names with optional hold ms, e.g. ["C", "X:250", "scan"]. Names: scan (card scan, held 3000 ms by
    default), W E D C X Z A Q (ring buttons 1-8 clockwise from the top right; C = button 4 = yes/OK/NEXT,
    X = button 5), Service (long press ends a credit / skips a song with AquaMai), Test (opens the test menu),
    Coin. Never press anything on the attract screen except scan (any button there starts a GUEST entry).
    Only drives the drive.ps1 test instance, never another game, and not while a game job is driving it."""
    if not isinstance(keys, list) or not keys or len(keys) > 40:
        return C.core._err("keys must be a list of 1-40 key names")
    plan = []
    for k in keys:
        name, _, ms = str(k).partition(":")
        if name not in VKEYS:
            return C.core._err(f"unknown key {name!r}; use " + ", ".join(VKEYS))
        try:
            hold = int(ms) if ms else VKEYS[name][1]
        except ValueError:
            return C.core._err(f"bad hold time in {k!r}")
        if not 10 <= hold <= 10000:
            return C.core._err("hold ms must be within 10..10000")
        plan.append((name, VKEYS[name][0], hold))
    g = C.guard_test_instance("test_game_vkeys")
    if g:
        return g
    busy = _running_game_jobs()
    if busy:
        return C.core._err(f"a game job is driving the test game ({busy[0]['job']}); wait for it or job_stop it")
    t = _test_game_targets()
    if isinstance(t, dict):
        return t
    amdaemon, hwnd, VirtualKeys = t
    pressed = []
    try:
        vk = VirtualKeys(amdaemon, hwnd)
    except OSError as e:
        return C.core._err(f"could not attach the virtual keyboard to amdaemon pid {amdaemon}: {e}")
    try:
        for i, (name, code, hold) in enumerate(plan):
            vk.press(code, hold)
            pressed.append(f"{name}:{hold}")
            time.sleep(0.8 if i == len(plan) - 1 else max(0.8, float(between_seconds)))
    finally:
        time.sleep(1.0)   # the io board polls only while the game counts as foreground: let it see every key up
        vk.restore()
    summary = {"ok": True, "pressed": pressed, "amdaemon": amdaemon}
    try:
        import ngo_api
        summary["screen"] = ngo_api.test_screen_state(3).get("screen")
    except Exception:  # noqa: BLE001
        pass
    if not shot:
        return summary
    time.sleep(0.4)
    png, err = C.core._api_png("/api/screenshot.png")
    if err:
        summary["screenshotError"] = err.get("error")
        return summary
    res = C.core._png_result(png, save_path)
    return (res if isinstance(res, list) else [res]) + [json.dumps(summary)]


def _test_game_targets() -> Any:
    """(amdaemon pid, game window, VirtualKeys class) of the drive.ps1 test game, or an error dict. The amdaemon is the
    one under the package whose parent is drive-state's inject.exe (or the only one under the package)."""
    st = C.read_drive_state() or {}
    game_pid, inject_pid = st.get("gamePid"), st.get("injectPid")
    if not game_pid:
        return C.core._err("no drive-state.json game")
    pkg = str(C.core._package()).rstrip("\\")
    rc, out = C.core._ps(
        "$ErrorActionPreference='Stop'; Get-CimInstance Win32_Process -Filter \"Name='amdaemon.exe'\" | "
        "ForEach-Object { '{0}|{1}|{2}' -f $_.ProcessId, $_.ParentProcessId, $_.ExecutablePath }")
    if rc != 0:
        return C.core._err("cannot list amdaemon.exe: " + C.core._tail(out, 3))
    cands = []
    for line in out.splitlines():
        parts = line.strip().split("|", 2)
        if len(parts) == 3 and parts[2].lower().startswith(pkg.lower() + "\\"):
            cands.append((int(parts[0]), int(parts[1])))
    mine = [c for c in cands if c[1] == inject_pid] or (cands if len(cands) == 1 else [])
    if len(mine) != 1:
        return C.core._err(f"cannot pick the test game's amdaemon.exe exactly (candidates {cands}, inject pid {inject_pid})")
    hwnd = _main_window(int(game_pid))
    if not hwnd:
        return C.core._err(f"no visible window for the test game pid {game_pid} yet")
    import sys
    tdir = str(C.tools_dir())
    if tdir not in sys.path:
        sys.path.insert(0, tdir)
    try:
        from vkeys import VirtualKeys  # noqa: E402  (tools\vkeys.py)
    except ImportError as e:
        return C.core._err(f"cannot import tools\\vkeys.py ({e}); it needs the pefile package")
    return mine[0][0], hwnd, VirtualKeys


def _main_window(pid: int) -> Optional[int]:
    if os.name != "nt":
        return None
    u32 = ctypes.windll.user32
    found: list[int] = []
    proto = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)

    def cb(h, _):
        p = wt.DWORD()
        u32.GetWindowThreadProcessId(h, ctypes.byref(p))
        if p.value == pid and u32.IsWindowVisible(h):
            found.append(h)
        return True
    u32.EnumWindows(proto(cb), 0)
    return found[0] if found else None


def job_status(job: Optional[str] = None, tail: int = 40) -> dict:
    """State of a background job (default: the newest): running / finished / failed / stopped / died, exit code
    and error, the last `tail` lines of its output (the script's own log lines), the out folder with its newest
    files (PNG sheets, logs, JSON). "died" = no exit record (killed or crashed): check game_processes and clean up
    with job_stop."""
    d = _job_dir(job)
    if d is None:
        return C.core._err("no such job" if job else "no jobs yet")
    try:
        tail = max(0, min(int(tail), 400))
    except (TypeError, ValueError):
        tail = 40
    info = _job_info(d, tail)
    out = Path(info["outDir"]) if info.get("outDir") else None
    if out and out.is_dir():
        files = sorted((f for f in out.iterdir() if f.is_file()), key=lambda f: f.stat().st_mtime, reverse=True)
        info["files"] = [{"name": f.name, "bytes": f.stat().st_size} for f in files[:30]]
        info["fileCount"] = len(files)
    info["ok"] = True
    return info


def job_stop(job: Optional[str] = None, wait_seconds: float = 90.0, force: bool = False, stop_game: bool = True) -> dict:
    """Stop a background job (default: the newest). Writes a STOP file: the script gets KeyboardInterrupt at its
    next step, so its own cleanup runs (settings.ini restore, drive.ps1 stop); waits up to wait_seconds. If it is
    still alive: force=True terminates exactly that PID (verified by its creation time) and puts the test-only
    settings.ini keys (testInput, frameLog, testFirstPlay, inputLog, selectPartner) back to their values from the
    job's start snapshot. stop_game=True then runs drive.ps1 stop if the drive.ps1 test game is still up - never
    when a game that drive.ps1 did not start is running."""
    d = _job_dir(job)
    if d is None:
        return C.core._err("no such job" if job else "no jobs yet")
    meta = _read_json(d / "job.json") or {}
    pid, created = meta.get("pid", 0), meta.get("created")
    res: dict[str, Any] = {"ok": True, "job": d.name}
    if _alive(pid, created):
        (d / "STOP").write_text("stop", encoding="ascii")
        t_end = time.time() + max(0.0, min(float(wait_seconds), 900.0))
        while time.time() < t_end and _alive(pid, created):
            time.sleep(1.0)
        if _alive(pid, created):
            if not force:
                info = _job_info(d, 15)
                info.update(ok=False, error="job still running after the STOP request; call again with force=True to "
                                           "terminate its PID (its own cleanup will then not run)")
                return info
            res["killed"] = _kill_exact(pid, created)
            time.sleep(1.0)
            r = _restore_test_keys(d)
            if r:
                res.update(r)
    elif not (d / "exit.json").exists() and meta.get("startsGame"):
        r = _restore_test_keys(d)   # it died earlier without its own cleanup
        if r:
            res.update(r)
    if stop_game and meta.get("startsGame"):
        res.update(_stop_test_game())
    res["status"] = _job_info(d, 10)
    return res


def list_jobs(limit: int = 20) -> dict:
    """The newest background jobs (state, kind, start time, out folder), newest first."""
    root = _jobs_root()
    if not root.is_dir():
        return {"ok": True, "jobs": []}
    try:
        limit = max(1, min(int(limit), 200))
    except (TypeError, ValueError):
        limit = 20
    dirs = sorted((x for x in root.iterdir() if (x / "job.json").exists()), key=lambda x: x.name, reverse=True)[:limit]
    return {"ok": True, "root": str(root), "jobs": [_job_info(x) for x in dirs]}


# ======================================================================================

TOOLS = [
    game_processes, play_report, heapwalk_diff,
    soak_test, select_tune, to_collection, run_offscreen_test,
    test_game_vkeys, job_status, job_stop, list_jobs,
]


def register(mcp: Any, server_module: Any) -> None:
    C.bind(server_module)
    for fn in TOOLS:
        mcp.tool()(fn)
