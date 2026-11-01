# Build, install, test, logs

## Build the mod

- Needs: the .NET 8 SDK, the game installed (the project references `<PKG>\Sinmai_Data\Managed\*.dll`, MelonLoader
  and 0Harmony from `<PKG>`), and the Cubism SDK for Native (its x64 `Live2DCubismCore.dll` is copied next to
  `Sinmai.exe` on install). `build.ps1` has the dotnet path, the Cubism core path and the NuGet / dotnet cache folders
  of the original machine written at the top - point them at your own before the first build.
- `powershell -ExecutionPolicy Bypass -File build.ps1` builds `bin\PartnerLive2D.dll` and the Headless harness;
  `-Install [-Package <PKG>]` also copies the DLL to `<PKG>\Mods\`, the Cubism core next to `Sinmai.exe` and the whole
  `deploy\` tree into `<PKG>` (this overwrites `<ASSETS>\hiyori\partner.ini` and `A500\partner\partner000501\Partner.xml`
  with the repo versions).
- MCP `build_and_install` runs the same and refuses while the package's `Sinmai.exe` runs. While the game runs the
  DLL is locked: put a new build in `<GAME_ROOT>\pending-mods\`; `launcher.ps1` moves it into `Mods\` before the next
  start.
- Headless checks: `tests\Headless\bin\Release\net8.0\Headless.exe [<Cubism Samples\Resources folder>]` (rig, sprite
  pair, select layout validation, PNG cache, theme, main-thread, math, every sample model);
  `Headless.exe spritepack <manifest.json>` validates a sprite export; `Headless.exe rigdump <model3> <params.json>`.
- MCP server tests: `python mcp\test_offline.py`, `python mcp\test_ngo_tools.py`, `python mcp\test_stdio.py`.

## Ship the skin to another cabinet

- Whole skin: `python tools\ngo_pack_release.py <out>` -> `files\` (as installed now), `original\` (stock copies taken
  from the original SDEZ 1.70 install archive, path built into the script), `manifest.json` (sha256 now / stock),
  `install.bat` / `uninstall.bat` (ASCII .ps1 behind them), notes. It never packs segatools.ini, aime.txt,
  AquaMai.toml, config_*.json, appdata, amfs, logs, photos, caches, the test theme, `account_edit.applied.ini`.
- Mod-only patch (e.g. the stutter / leak fix): `python tools\ngo_pack_patch.py <out> <notes.txt>` -> DLL + install /
  uninstall scripts (the replaced DLL is kept as `.before-<name>`) + the optional GPU clock lock helpers. Its
  install script refuses while that package's game runs.
- The asset patches are not portable as a diff: `resources.assets` is shipped whole (it is the same SDEZ 1.70 file on
  every cabinet before patching).

## Test runs

### tools\drive.ps1

| command | effect |
|---|---|
| `start` | backs up `AquaMai.toml` to `tools\AquaMai.toml.drivebak`; sets shared audio (no exclusive, no 8ch), Windowed 540x960; comments out `[Utils.AntiLag]`; turns `EntryToMusicSelection` on for every login (skips photo / area / ticket / tutorial screens) unless `DRIVE_FULL_ENTRY=1`; comments out `[GameSystem.DisableTimeout]` (stock timers) unless `DRIVE_KEEP_TIMEOUT=1`; `DRIVE_NO_SERVER=1` also comments out `[GameSettings.ForceAsServer]`; mutes the default endpoint; starts `inject.exe` + `Sinmai.exe -monitor 1 -screen-fullscreen 0`; writes `tools\drive-state.json` (gamePid, injectPid, wasMuted, tomlBak) |
| `stop` | Stop-Process gamePid + injectPid, every `amdaemon.exe` under `<PKG>`; restores AquaMai.toml and the mute; deletes `<ASSETS>\zz-test-default`; deletes drive-state.json |
| `key`, `keys`, `shot` | legacy: bring the window to the front, real key events / desktop capture. Not for off-screen work |

`start` opens the window on screen; the scripts (and MCP jobs) park it at x = -4200 at once.

### Keys without focus: tools\vkeys.py

Buttons, Test / Service and the card scan are read inside `amdaemon.exe` by `mai2hook.dll` and `hinata.dll` with
`user32!GetAsyncKeyState`, and mai2hook accepts them only while `GetForegroundWindow()` is the game window. So:
`VirtualKeys(amdaemon_pid, game_hwnd)` writes two tiny x64 stubs into amdaemon and repoints those modules' import
entries (GetAsyncKeyState -> a 256-entry table written from Python, GetForegroundWindow -> the game window);
`press(vk, ms)` holds a key; `restore()` puts the imports back so the real keyboard works again. Wait about 1 s with
every key up before `restore()`: the io board polls only while the game counts as foreground, and restoring right
after a press leaves the last key latched (a "dead" confirm button). The mod's own `testInput` hook lives in
Sinmai.exe and cannot reach these keys.

`tools\phase_round_lib.press(name, ms)` = attach, press, 0.8 s, restore (names: scan, C, X, D, Service).
MCP `test_game_vkeys` does the same for the drive.ps1 game, choosing its amdaemon by parent PID.

### Cabinet key map (segatools defaults here)

| key | cabinet | notes |
|---|---|---|
| Enter held 3 s | card scan (DEVICE\aime.txt) | the only key allowed on the attract screen |
| W E D C X Z A Q | ring buttons 1-8, clockwise from the top right | C = button 4 = yes / OK / NEXT / START; X = button 5 |
| F2 (Service) | long press on music select = end the credit with upload (OneKeyEntryEnd); held in a song = skip (OneKeyRetrySkip) | |
| F1 (Test) | test menu | |
| F3 (coin) | coin; on the attract screen the mod counts it for the quick-login helper | |

### A login-to-result walk (what the scripts do)

1. Poll the screen (`/api/test/processes`, MCP `test_screen_state`) until `AdvertiseProcess`; wait ~6 s.
2. Scan (3000 ms). On `EntryProcess` press C ("play with this Aime?" -> yes), or the card screen times out back to
   the attract loop. A registered card can need a second scan until the entry state is `ConfirmPlay`.
3. Press C every ~2.5 s through the info screens until `MusicSelectProcess` (mode select times out at a 7 s cadence;
   2.5 s works). Never press C on a ticket screen you did not mean to use: it spends a ticket (drive.ps1's
   EntryToMusicSelection skips those screens).
4. Genre -> C -> Music list. C until phase `Menu`, then C (150 ms) = START -> `GameProcess`.
5. Service 2500 ms skips the chart -> `ResultProcess` -> C.
6. Logout: Service 2500 ms on music select; on `ContinueProcess` press X (no); C through the upload / game-over
   screens until `AdvertiseProcess`.

### Test scripts in tools\ (each starts and stops its own drive.ps1 game unless noted)

| script | purpose |
|---|---|
| `ngo_attract_check.py <out> [s]` | attract screen only, no login (CHECK_DT) |
| `ngo_skin_walk.py <out>` | attract -> login -> every screen to music select; BURST / BURST_AT / ATTRACT_SECONDS / SELECTPERF (use `DRIVE_FULL_ENTRY=1`) |
| `ngo_full_walk.py <out>` | whole flow incl. track start, play frames, result, logout; DIFF_LEFT, LEFT_KEY, PLAY_DT, PLAY_MAX, TESTINPUT, CAT_LEFT, SELECT_ONLY ... |
| `ngo_sprite_test.py <out>` | sprite partner: select Idle / Random, tap, Menu Confirm, START Enter, result tier + Reaction (forces `selectPartner = ngo_pair` for the run) |
| `ngo_select_tune.py <out> <cands> [result_cands]` | candidate sheets for select / result placement (see tuning-layouts.md) |
| `ngo_to_collection.py <out>` + `ngo_key.py <png> keys...` | reach the end-of-credit collection screen, then drive it by hand; the game is left running |
| `ngo_soak.py <out> [min]` | long autoplay soak with process sampling and optional heap walks |
| `phase_shots.py`, `phase_round.py`, `phase_idle.py`, `phase_play.py`, `offscreen_tour.py` | older select-phase / hitch probes and the full-credit tour |
| `ngo_entry_shot.py`, `entry_flow_test.py`, `f3_login_test.py`, `freedom_entry_test.py` | entry-screen and login-path checks |
| `ngo_attract_burst.py`, `ngo_flash_watch.py`, `ngo_burst_during.py`, `ngo_autoplay_during.py`, `ngo_force_rank.py` | helpers that run beside another script against a running game |
| `ngo_demo.py <log dir>` | NOT a test: a demo on the real cabinet with the normal launcher, on screen and with sound (log in, play a song, skip, result, log out); only when someone wants to watch the skin on the cabinet |

MCP: `run_offscreen_test`, `soak_test`, `select_tune`, `to_collection` start these as background jobs.

## Logs

- `<PKG>\MelonLoader\Latest.log` (current run) and `Logs\<date>.log` (one per run). Mod lines carry `[PartnerLive2D]`.
  MCP `read_mod_log(lines, filter)`; `play_report` reads the dated logs.
- Tags: `[proc] + Process.X` (every screen), `[play]` (per chart), `[clean]` (cleanup ms + heap), `[account]`
  (tweaks at login), `[test]` (test-only paths), `[input]` (inputLog), `[frame]` / `[mem]` (frameLog only).
- Other useful lines: `control page: http://127.0.0.1:8791/`, `partner '<name>': naviCharaId .., partnerId ..`,
  `skip <folder>: ...` (partner.ini rejected), `<name> [<context> #<id>]: placed at (..) size ..`,
  `music-select baseline: ...`, `music-select disabled and restored: ...` (feature fault: fix, then restart),
  `<name>: sprite unavailable; using Live2D: ...`, `<name>: resultUnder '...' not found or already a canvas`.
- `<GAME_ROOT>\launcher.log` (launcher steps), `<GAME_ROOT>\playlogs\monitor_<start>.csv` (see launcher.md).
- Screens: MCP `screenshot` (mod API; works off screen), `view_frame` (partner RT), `test_ui_dump` (what draws what).
