"""Runs one tools\\ script as a background job of the partnerlive2d MCP server (ngo_jobs.py). ASCII only.

usage: python job_boot.py <job_dir> <drive_state.json> <script.py> [script args...]

- the script runs as __main__ with its own argv, so it behaves exactly as when started by hand;
- a file named STOP in <job_dir> raises KeyboardInterrupt in the script, so its own finally blocks run
  (settings.ini restore, drive.ps1 stop) - the cooperative way to stop a job;
- while it runs, the test game's window (gamePid in drive-state.json, i.e. only the game drive.ps1 started)
  is kept parked off screen at x = -4200, without focus changes;
- <job_dir>\\exit.json gets the exit code and the error, if any.
"""
import ctypes
import ctypes.wintypes as wt
import json
import os
import runpy
import sys
import threading
import time
import traceback
import _thread

JOB_DIR, STATE, SCRIPT = sys.argv[1], sys.argv[2], sys.argv[3]
ARGS = sys.argv[4:]
done = threading.Event()
stopped = threading.Event()


def watch_stop():
    flag = os.path.join(JOB_DIR, "STOP")
    while not done.is_set():
        if os.path.exists(flag):
            stopped.set()
            _thread.interrupt_main()
            return
        done.wait(1.0)


def park_loop():
    if os.name != "nt":
        return
    u32 = ctypes.windll.user32
    enum_proc = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    while not done.is_set():
        try:
            pid = json.load(open(STATE, encoding="utf-8-sig")).get("gamePid")
        except (OSError, ValueError):
            pid = None
        if pid:
            found = []

            def cb(h, _):
                p = wt.DWORD()
                u32.GetWindowThreadProcessId(h, ctypes.byref(p))
                if p.value == pid and u32.IsWindowVisible(h):
                    found.append(h)
                return True
            u32.EnumWindows(enum_proc(cb), 0)
            for h in found:
                r = wt.RECT()
                u32.GetWindowRect(h, ctypes.byref(r))
                if r.left > -3000:
                    # SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
                    u32.SetWindowPos(h, 0, -4200, 40, 0, 0, 0x0001 | 0x0004 | 0x0010)
        done.wait(0.5)


def write_exit(code, error):
    tmp = os.path.join(JOB_DIR, "exit.json.tmp")
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump({"exit": code, "error": error, "stopped": stopped.is_set(), "ended": time.strftime("%Y-%m-%d %H:%M:%S")}, f)
    os.replace(tmp, os.path.join(JOB_DIR, "exit.json"))


threading.Thread(target=watch_stop, daemon=True).start()
threading.Thread(target=park_loop, daemon=True).start()
sys.argv = [SCRIPT] + ARGS
sys.path.insert(0, os.path.dirname(os.path.abspath(SCRIPT)))
code, error = 0, None
try:
    print("[job] start %s %s" % (os.path.basename(SCRIPT), " ".join(ARGS)), flush=True)
    runpy.run_path(SCRIPT, run_name="__main__")
except SystemExit as e:
    if e.code is None:
        code = 0
    elif isinstance(e.code, int):
        code = e.code
    else:
        code, error = 1, str(e.code)
except KeyboardInterrupt:
    code, error = 130, "stopped by job_stop" if stopped.is_set() else "interrupted"
except BaseException:
    code, error = 1, traceback.format_exc()
finally:
    done.set()
    try:
        write_exit(code, error)
    except OSError:
        pass
    print("[job] exit %s %s" % (code, error or ""), flush=True)
sys.exit(code)
