# Performance: stutter, memory growth, leaks

## Facts about this runtime

- Unity 2018.4 Mono uses the **Boehm GC**: non-generational, every collection scans the whole heap, so a heap that
  keeps growing makes every later collection slower. Managed garbage per frame = periodic hitches.
- **The game switches GC off during songs**: `GameProcess.OnStart` sets `GarbageCollector.GCMode = Disabled`,
  `OnRelease` turns it back on and calls `GC.Collect`. In-song stutter is therefore never GC; the stalls are at song
  start (chart / movie load, ~0.5 s) and in the fades. The collect in `OnRelease` gives one ~0.75 s frame at the start
  of the result screen - that is the game's own design.
- The game cleans memory (`MAI2System.SystemConfig.unloadUnusedAndGC` = `Resources.UnloadUnusedAssets` + full
  `GC.Collect`) in every fade and once more in `GameProcess.OnStart`. Over a long session the song-start run reached
  1-1.8 s, visible as a frozen first frame. `NgoCleanupTiming` skips a second run within `cleanupMinGap` (default 20 s)
  of the last one, so the cleanup stays under the fade before the track-start card. The fade-out after a song calls
  it while GC is still off: logged as ~0 ms, a no-op.
- **ConditionalWeakTable leaks** on Boehm (no ephemerons: its keys are held strongly). Two CWTs in this mod kept every
  finished song select / result screen alive (~+50 MB live heap per track). Never use CWT here; use an instance-id
  dictionary or a list cleared at screen end (see `Patches.MonitorIndex`, `MusicSelectCategoryInput`).
- `UnityEngine.Object.name` allocates a new string on every read; hooks on `Image` (sprite setter, OnEnable,
  OnPopulateMesh) run for every UI image, many times per frame in gameplay. Read names once per instance id
  (`NameCache`). The select layout once built path strings every frame: 110-185 ms stalls every ~11 s on music
  select until it cached them.
- The select layout applies on phase changes only and does a light `Maintain` per frame. (The first version applied
  every frame: ~0.9 ms each, the first one 107 ms because a PNG was decoded twice; the frame-end replay costs
  ~0.01 ms.) PNGs are cached and preloaded at startup (`MusicSelectPngCache`).

## Always-on measurements (also in real play)

- `[play] music <id> diff <d>: <s> s, <n> frames (<fps> fps), <a> over 25 ms, <b> over 50 ms, worst <w> ms, load <l> ms, gc <g>`
  - one line per chart from `PlayStats.cs` (the first 3 frames after `GameProcess.OnStart` count as load).
- `[clean] unload+GC <ms> ms, heap <MB> MB` - every cleanup run with the managed heap left after it; skipped runs
  are logged only with `frameLog`.
- `[proc] + Process.X` - every screen, with the MelonLoader timestamp.
- `<GAME_ROOT>\playlogs\monitor_*.csv` from the launcher's `play-monitor.ps1` (private / working set MB, CPU %, GPU
  clock / load / temp / pstate / slowdown reasons every 5 s).

`frameLog = true` adds `[frame]` lines for every frame over 25 ms, 10 s summaries and a per-minute `[mem]` census
(`FindObjectsOfTypeAll` of materials / textures / sprites / RTs / meshes / GameObjects, material growth, collection
census). The census itself causes hitches: test only, never for a real session.

## Play report (MCP `play_report`, `tools\ngo_play_report.py`)

Reads the newest `MelonLoader\Logs\*.log` (or the given one) and the monitor CSV of the same game start. One row per
chart: start time, song (title from `Music.xml`), difficulty, quick retries, frames over 25 / 50 ms, worst frame,
chart load ms, fade -> song s, end -> result s, result -> select s, longest cleanup ms, heap after cleanup, process MB
at song start; then first -> last values and per-track slopes. Healthy: result -> select ~1.6 s and flat, heap after
cleanup flat over 20+ tracks, private MB flat (3.5-3.9 GB was normal on the original machine), GPU well under its
limits. A heap that climbs tens of MB per track = a leak; a cleanup that grows = heap growth; many >50 ms frames
in songs = not GC, look at the GPU (clock lock, temperature, power mode) or background work.

## Soak and heap walks (test instance only)

- MCP `soak_test(minutes, play_seconds, heapwalk_after_tracks=[1, 3])` (`tools\ngo_soak.py`): testInput + frameLog on
  for the run, autoplay Critical, each track skipped after `play_seconds`, process sampled every 30 s
  (`soak.csv`: private MB, handles, working set), mod log copied as `game.log`. Off screen the game runs ~30 fps, so
  frame times are not representative: judge growth. With OneKeyEntryEnd the credit ends after ~4 tracks and the
  script logs in only once per run, so one soak = one credit.
- MCP `test_heapwalk` (`POST /api/test/heapwalk`, `HeapWalk.cs`): walks every static field (closed generic singleton
  bases included) and every live MonoBehaviour / ScriptableObject, totals objects and bytes by type and by root field,
  writes a TSV (`kind name count bytes`; `meta heap_after_gc`, `meta reached`). The game freezes while it runs. Take
  two walks on the song list a few tracks apart, then `heapwalk_diff`: the roots / types that grew are the leak.
  Move the TSVs out of `<ASSETS>` (the release packer would ship them); `test_heapwalk` moves its own.
- `test_select_perf(reset=True)`, idle on the music list, `test_select_perf()` = cost of the select layout code.

Verified example: the CWT fix took the live heap after the result -> select cleanup from 327 -> 485 MB over 4 tracks
(before) to 320 -> 358 MB, flat (after); a 23-track real session then stayed flat (result -> select 1.58-1.62 s).

## Cabinet-side causes of micro-stutter

- GPU clock idling down between bursts -> the launcher's GPU lock (launcher.md).
- Power mode not really on "best performance" (vendor power manager) -> vendor performance mode.
- `fit-window.ps1` resizing the window repeatedly -> it now only re-asserts z-order.
- E-cores / EcoQoS -> P-core affinity and throttling off (launcher).
- Audio on Bluetooth / HDMI -> latency and glitches; use the wired default device.
