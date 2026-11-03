"""Per-track report of a real play session: loading times, in-song hitches, cleanup time and the heap left after it,
and the process memory sampled from outside.

Reads the MelonLoader log of one game run (the "[proc]", "[clean]" and "[play]" lines; the last two come from
PlayStats.cs / NgoCleanupTiming.cs) and, if present, D:\\maimai\\playlogs\\monitor_*.csv written by play-monitor.ps1
for the same run. Nothing is written; the report goes to stdout.
usage: python tools/ngo_play_report.py [melonloader_log] [monitor_csv]
       (defaults: the newest log in Package\\MelonLoader\\Logs and the monitor file of the same game start)"""
import csv, glob, os, re, sys
import xml.etree.ElementTree as ET
from datetime import datetime, timedelta

PKG = r"D:\maimai\Package"
PLAYLOGS = r"D:\maimai\playlogs"
DIFF = ["Basic", "Advanced", "Expert", "Master", "Re:Master"]
LINE = re.compile(r"\[(\d\d:\d\d:\d\d\.\d+)\] (.*)")
PROC = re.compile(r"\[proc\] \+ (\S+)")
CLEAN = re.compile(r"\[clean\] unload\+GC (\d+) ms, heap (\d+) MB")
PLAY = re.compile(r"\[play\] music (-?\d+) diff (-?\d+): ([\d.]+) s, (\d+) frames \(([\d.]+) fps\), (\d+) over 25 ms, "
                  r"(\d+) over 50 ms, worst ([\d.]+) ms, load (\d+) ms, gc (\d+)")
RETRY = re.compile(r"\[AddPlayCountWhenQuickRetry\] Player 0 MusicID (\d+) Difficulty (\d+)")
STALL = re.compile(r"\[stall\] (\d+) ms at ([\d.]+) s into music (-?\d+) \((\d\d:\d\d:\d\d\.\d+)\)")


def newest(pattern):
    files = glob.glob(pattern)
    return max(files, key=os.path.getmtime) if files else None


def read_log(path):
    events, day, last = [], 0, None
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            m = LINE.match(line)
            if not m:
                continue
            t = datetime.strptime(m.group(1), "%H:%M:%S.%f")
            if last is not None and t + timedelta(days=day) < last - timedelta(hours=12):
                day += 1   # past midnight
            t += timedelta(days=day)
            last = t
            events.append((t, m.group(2).strip()))
    return events


_titles = {}
def title(music_id):
    if music_id not in _titles:
        name = ""
        hit = glob.glob(os.path.join(PKG, "Sinmai_Data", "StreamingAssets", "*", "music", "music%06d" % music_id, "Music.xml"))
        if hit:
            try:
                name = ET.parse(hit[0]).getroot().findtext("name/str") or ""
            except ET.ParseError:
                pass
        _titles[music_id] = name
    return _titles[music_id]


def tracks_of(events):
    tracks, cur, last_fade, last_proc, select_from, in_song = [], None, None, None, None, False
    for t, msg in events:
        m = PROC.search(msg)
        if m:
            name = m.group(1)
            if name.endswith("FadeProcess"):
                last_fade = t
                continue
            # the song's own fade-out still runs with GC switched off: in_song lasts until the next screen
            in_song = name == "Process.GameProcess"
            if name == "Process.GameProcess":
                if last_proc == "Process.RetryWaitProcess" and cur is not None:
                    cur["retries"] += 1
                else:
                    cur = {"start": t, "retries": 0, "music": None, "diff": None, "plays": [], "cleans": [],
                           "start_load": (t - last_fade).total_seconds() if last_fade else None,
                           "result_load": None, "select_load": None}
                    tracks.append(cur)
            elif name == "Process.ResultProcess" and cur is not None:
                cur["result_load"] = (t - last_fade).total_seconds() if last_fade else None
            elif name == "Process.NextTrackProcess":
                select_from = t
            elif name == "Process.MusicSelectProcess" and select_from is not None and cur is not None:
                cur["select_load"] = (t - select_from).total_seconds()
                select_from = None
            last_proc = name
            continue
        m = PLAY.search(msg)
        if m and cur is not None:
            cur["plays"].append({"music": int(m.group(1)), "diff": int(m.group(2)), "seconds": float(m.group(3)),
                                 "frames": int(m.group(4)), "fps": float(m.group(5)), "over25": int(m.group(6)),
                                 "over50": int(m.group(7)), "worst": float(m.group(8)), "load": int(m.group(9)), "gc": int(m.group(10))})
            cur["music"], cur["diff"] = int(m.group(1)), int(m.group(2))
            continue
        m = RETRY.search(msg)
        if m and cur is not None and cur["music"] is None:
            cur["music"], cur["diff"] = int(m.group(1)), int(m.group(2))
            continue
        m = CLEAN.search(msg)
        if m and cur is not None and not in_song:
            # a cleanup belongs to the track it follows: the heap it leaves is what the next song starts with
            # (one inside a song is a no-op: the game has switched GC off there)
            cur["cleans"].append((t, int(m.group(1)), int(m.group(2))))
    return tracks


def read_monitor(path):
    if not path or not os.path.isfile(path):
        return []
    try:
        base = datetime.strptime(os.path.basename(path)[8:16], "%Y%m%d")
    except ValueError:
        base = datetime.fromtimestamp(os.path.getmtime(path))
    rows = []
    with open(path, newline="") as f:
        for r in csv.DictReader(f):
            try:
                t = datetime.combine(base.date(), datetime.strptime(r["time"], "%H:%M:%S").time())
                rows.append((t, int(r["private_mb"]), r))
            except (KeyError, ValueError):
                pass
    return rows


def sample_at(rows, when):
    """private MB of the monitor sample closest to `when` (log times carry no date: compare clock times)"""
    if not rows or when is None:
        return None
    key = lambda r: abs((r[0].hour * 3600 + r[0].minute * 60 + r[0].second) - (when.hour * 3600 + when.minute * 60 + when.second))
    best = min(rows, key=key)
    return best[1] if key(best) <= 30 else None


def read_procs(path):
    """procs_*.csv from proc-sampler.ps1: (seconds of day, total CPU %, busiest processes) once a second"""
    rows = []
    if path and os.path.isfile(path):
        with open(path, newline="") as f:
            for r in csv.DictReader(f):
                try:
                    t = datetime.strptime(r["time"], "%H:%M:%S.%f")
                    rows.append((t.hour * 3600 + t.minute * 60 + t.second + t.microsecond / 1e6, float(r["total_cpu_pct"]), r["top"]))
                except (KeyError, ValueError):
                    pass
    return rows


def stalls_of(events):
    """[stall] lines (PlayStats: a frame over 100 ms in a song), each with the song length from the next [play] line of
    that music, so a stall can be placed as mid-song or at the end"""
    out = []
    for i, (t, msg) in enumerate(events):
        m = STALL.search(msg)
        if not m:
            continue
        music, length = int(m.group(3)), None
        for _, later in events[i + 1:]:
            p = PLAY.search(later)
            if p and int(p.group(1)) == music:
                length = float(p.group(3)); break
        out.append({"ms": int(m.group(1)), "at": float(m.group(2)), "music": music, "wall": m.group(4), "length": length})
    return out


def same_run(kind, first, log):
    """playlogs\\<kind>_<game start>.csv of the run whose log starts at `first`"""
    cands = []
    for p in glob.glob(os.path.join(PLAYLOGS, kind + "_*.csv")):
        try:
            st = datetime.strptime(os.path.basename(p)[len(kind) + 1:len(kind) + 16], "%Y%m%d_%H%M%S")
        except ValueError:
            continue
        gap = abs((st.hour * 3600 + st.minute * 60 + st.second) - (first.hour * 3600 + first.minute * 60 + first.second))
        if gap <= 120 and abs((datetime.fromtimestamp(os.path.getmtime(log)).date() - st.date()).days) <= 1:
            cands.append((gap, p))
    return min(cands)[1] if cands else None


def fmt(v, spec="{:.2f}", none="-"):
    return none if v is None else spec.format(v)


def trend(values):
    """least-squares slope per step"""
    pts = [(i, v) for i, v in enumerate(values) if v is not None]
    if len(pts) < 3:
        return None
    n = len(pts); mx = sum(i for i, _ in pts) / n; my = sum(v for _, v in pts) / n
    den = sum((i - mx) ** 2 for i, _ in pts)
    return sum((i - mx) * (v - my) for i, v in pts) / den if den else None


def main():
    log = sys.argv[1] if len(sys.argv) > 1 else newest(os.path.join(PKG, "MelonLoader", "Logs", "*.log"))
    if not log:
        sys.exit("no MelonLoader log found")
    events = read_log(log)
    mon = sys.argv[2] if len(sys.argv) > 2 else None
    # the sampler files are named after the game's start, a second or two before the log's first line
    if mon is None and events:
        mon = same_run("monitor", events[0][0], log)
    procs_file = same_run("procs", events[0][0], log) if events else None
    rows = read_monitor(mon)
    tracks = tracks_of(events)

    print("日志:", log)
    print("采样:", mon or "（没有，这局没开 play-monitor）")
    print()
    head = ["#", "开始", "歌曲", "难度", "重开", "掉帧>25ms", ">50ms", "最慢帧ms", "谱面加载ms", "淡出→开歌s", "结束→结算s",
            "结算→选歌s", "清理最长ms", "清理后堆MB", "进程内存MB"]
    table = []
    for i, tr in enumerate(tracks, 1):
        plays = tr["plays"]
        full = plays[-1] if plays else None   # the attempt that reached the result
        heap = tr["cleans"][-1][2] if tr["cleans"] else None
        table.append([
            str(i), tr["start"].strftime("%H:%M"),
            (title(tr["music"]) if tr["music"] is not None else "")[:18],
            DIFF[tr["diff"]] if tr["diff"] is not None and 0 <= tr["diff"] < len(DIFF) else "",
            str(tr["retries"]) if tr["retries"] else "",
            str(sum(p["over25"] for p in plays)) if plays else "-",
            str(sum(p["over50"] for p in plays)) if plays else "-",
            fmt(max(p["worst"] for p in plays), "{:.0f}") if plays else "-",
            fmt(full["load"], "{:.0f}") if full else "-",
            fmt(tr["start_load"]), fmt(tr["result_load"]), fmt(tr["select_load"]),
            fmt(max(c[1] for c in tr["cleans"]), "{:.0f}") if tr["cleans"] else "-",
            fmt(heap, "{:.0f}"), fmt(sample_at(rows, tr["start"]), "{:.0f}"),
        ])
    widths = [max(len(head[c]), *(len(r[c]) for r in table)) if table else len(head[c]) for c in range(len(head))]
    print("  ".join(h.ljust(w) for h, w in zip(head, widths)))
    for r in table:
        print("  ".join(v.ljust(w) for v, w in zip(r, widths)))
    print()

    if not tracks:
        print("这份日志里没有打歌记录。")
        return
    heaps = [tr["cleans"][-1][2] if tr["cleans"] else None for tr in tracks]
    cleans = [max(c[1] for c in tr["cleans"]) if tr["cleans"] else None for tr in tracks]
    selects = [tr["select_load"] for tr in tracks]
    procs = [sample_at(rows, tr["start"]) for tr in tracks]
    plays = [p for tr in tracks for p in tr["plays"]]

    def span(name, vals, unit, spec="{:.0f}"):
        v = [x for x in vals if x is not None]
        if not v:
            print(f"{name}: 没有数据")
            return
        s = trend(vals)
        print(f"{name}: 第一首 {spec.format(v[0])} → 最后 {spec.format(v[-1])} {unit}（范围 {spec.format(min(v))}–{spec.format(max(v))}"
              + (f"，每首平均 {s:+.2f} {unit}" if s is not None else "") + "）")

    print(f"共 {len(tracks)} 首（含 {sum(t['retries'] for t in tracks)} 次快速重开）")
    span("结算→选歌加载", selects, "s", "{:.2f}")
    span("清理耗时（每轮最长）", cleans, "ms")
    span("清理后的托管堆", heaps, "MB")
    span("进程内存（开歌时）", procs, "MB")
    if plays:
        frames = sum(p["frames"] for p in plays)
        over25 = sum(p["over25"] for p in plays); over50 = sum(p["over50"] for p in plays)
        print(f"打歌中: {frames} 帧里 {over25} 帧超过 25 ms（{over50} 帧超过 50 ms），最慢 {max(p['worst'] for p in plays):.0f} ms，"
              f"平均 {sum(p['fps'] for p in plays) / len(plays):.1f} fps；歌曲中途触发 GC {sum(p['gc'] for p in plays)} 次")
    else:
        print("打歌中: 这局的模组版本还没有 [play] 记录（下次启动起才有）")

    stalls = stalls_of(events)
    procs = read_procs(procs_file)
    print()
    print(f"卡顿（单帧 >100 ms）: {len(stalls)} 次" + ("" if procs_file else "；没有进程采样（这局没开 proc-sampler）"))
    for s in stalls:
        where = ""
        if s["length"]:
            left = s["length"] - s["at"]
            where = f"，全长 {s['length']:.0f} s，" + ("在结尾 %.1f s 内" % left if left <= 5 else "离结尾还有 %.0f s" % left)
        print(f"  {s['wall']}  {s['ms']} ms  第 {s['at']:.1f} s  {title(s['music'])[:18]}{where}")
        if procs:
            t = datetime.strptime(s["wall"], "%H:%M:%S.%f"); sec = t.hour * 3600 + t.minute * 60 + t.second + t.microsecond / 1e6
            # a sample covers the second before its own time stamp
            near = [r for r in procs if sec <= r[0] <= sec + 1.6]
            if near:
                print(f"      那一秒全机 CPU {near[0][1]:.0f}%（全部核心算 100%），最忙的进程：{near[0][2]}")


if __name__ == "__main__":
    main()
