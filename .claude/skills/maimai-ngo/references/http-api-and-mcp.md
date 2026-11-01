# HTTP control API and the partnerlive2d MCP server

## The mod's HTTP API (`src\Unity\ControlServer.cs`)

- Listens on `http://127.0.0.1:<port>/` (settings.ini `port`, default 8791; `http = false` turns it off).
- Every non-GET request needs the header `X-PartnerLive2D: 1`; a foreign `Origin` is rejected (403). JSON bodies.
- `GET /` is the human tuning page (`src\Unity\ui.html`, embedded): sliders for partner values and the select preset.
- Work runs on Unity's main thread through `MainThread.Call` with a 5 s timeout: during loads, heap walks or a frozen
  game you get HTTP 503 "game main thread did not respond". Heap walk allows 600 s.
- Errors: `{"ok": false, "error": "..."}` with 400 (bad value), 404 (unknown route / partner / view), 405 (method),
  409 (state: feature off, testInput off, no select session), 500 (exception).

| route | method | what |
|---|---|---|
| `/api/status` | GET | mod version, screen size, partners (+ model groups, live view ids), views, states, music-select snapshot |
| `/api/rescan` | POST | re-read every `partner.ini` |
| `/api/screenshot.png` | GET | whole game screen (works with the window off screen) |
| `/api/partners`, `/api/partners/<name>` | GET | live partner configs (incl. unsaved edits) |
| `/api/partners/<name>/config` | POST | `{"values": {key: value}, "save": bool}` live edit (rebuild for model / renderSize / sprites) |
| `/api/partners/<name>/save`, `/reload`, `/preview` | POST | write live values to partner.ini / re-read the file / overlay preview `{"show": bool}` |
| `/api/views`, `/api/views/<id>` | GET | live views: context (musicSelect, result, room, overlay), state, rect, loaded / total sprite textures, VRAM estimate |
| `/api/views/<id>/frame.png` | GET | the view's RT (premultiplied RGBA) |
| `/api/views/<id>/state`, `/motion`, `/tap`, `/expression`, `/look`, `/close` | POST | drive a view (sprite views: `motion` = clip group, `tap` = Tap / Reaction) |
| `/api/theme` | GET / POST | runtime theme status / `{"enabled": bool}` |
| `/api/music-select` | GET | preset, fault message, sessions -> sides (phase, reason, layout, viewId, stockPartner, reaction) |
| `/api/music-select/layout` | POST | partial preset (validated as a whole first), `save` optional |
| `/api/music-select/node` | POST | `{"path": "<under Main>", scale, offsetX, offsetY, hidden, sprite}` |
| `/api/music-select/image` | POST | `{"id", sprite, parent, siblingIndex, x, y, width, height, hidden}` or `{"id", "delete": true}` |
| `/api/music-select/reset`, `/save` | POST | defaults (feature off) in memory / write `select_layout.ini` + 3 settings.ini lines |
| `/api/music-select/tree?root=&depth=&player=` | GET | select hierarchy as the layout sees it (409 outside select) |
| `/api/music-select/sprite.png?path=&player=` | GET | PNG of the sprite a select node shows |
| `/api/test/processes?since=` | GET | process log (`name`, `t`), `testInput` flag |
| `/api/test/entry` | GET | card-screen state (`ConfirmEntry`, `ConfirmPlay`, ...) |
| `/api/test/quicklogin` | GET | coin-key presses counted on the attract screen |
| `/api/test/music?find=<id>` | GET | song under the cursor; where music id `find` is (category / index) |
| `/api/test/ui` | GET | visible Images / SpriteRenderers: sprite, texture, screen rect, type, tint, path |
| `/api/test/tree?of=&up=&depth=` | GET | hierarchy around the first active object named `of` (canvases, sorting, graphics, rects) |
| `/api/test/renderers` | GET | menu-background renderers: sorting layer / order / queue, shader, bounds, cameras |
| `/api/test/selectperf?reset=1` | GET | select layout cost per call kind + frame-time stats |
| `/api/test/autoplay` | POST | `{"mode": None|Critical|Perfect|Great|Good|Miss|Random}` (needs testInput) |
| `/api/test/rank` | POST | `{"rank": "Rank_SSSP" | "None"}` forced result rank animation (needs testInput) |
| `/api/test/heapwalk` | POST | managed heap walk -> `<ASSETS>\heapwalk\walk_<time>.tsv` (freezes the game) |
| `/api/test/key` | POST | in-game virtual key (needs testInput) - does NOT reach the buttons (they are read in amdaemon); not wrapped |

Only `key`, `autoplay`, `rank` (and `testFirstPlay`) depend on `testInput`; the other test routes always answer.

## The MCP server

`<PROJECT>\mcp\server.py` (core tools) loads `ngo_api.py` (remaining HTTP routes) and `ngo_jobs.py` (tools\
scripts, background jobs) through a guarded import: a broken extension is reported on stderr and the core tools keep
working. Helpers shared by both: `ngo_common.py`; job runner: `job_boot.py`.

Configuration (read at call time): `PARTNERLIVE2D_PACKAGE` (default `<PKG>`), `PARTNERLIVE2D_PROJECT`
(default: the folder above `mcp\`), `PARTNERLIVE2D_PORT` (else settings.ini `port`, else 8791),
`PARTNERLIVE2D_GAME_ROOT` (default: parent of the package), `PARTNERLIVE2D_PLAYLOGS` (default `<GAME_ROOT>\playlogs`),
`PARTNERLIVE2D_STATE` (default `%TEMP%\partnerlive2d-mcp`), `PARTNERLIVE2D_PYTHON` (default: the server's python).

Register: `claude mcp add --scope user partnerlive2d -- <python.exe> <PROJECT>\mcp\server.py`
(add `-e PARTNERLIVE2D_PACKAGE=...` etc. when your folders differ). Restart the client after editing server files.

### Safety model

- Live tools act on whatever game answers on the port and never refuse (tuning on a running cabinet is normal).
- `build_and_install`, `test_game_start` and every game JOB refuse while any `Sinmai.exe` of the package runs.
- `test_autoplay`, `test_force_rank`, `test_heapwalk`, `test_game_vkeys` act only on the drive.ps1 test instance
  (PID in `tools\drive-state.json`) and refuse when any other game runs (`force=True` exists on the API ones for a
  game you started yourself some other way).
- Game jobs also refuse when `tools\drive-state.json` is left over (an earlier run was not stopped) or another game
  job is running, and when the package / port differ from what the scripts have built in.
- Processes are stopped only by exact PID; job PIDs are checked against their creation time before a terminate.

### Tools

Core (`server.py`, unchanged names):

| tool | use |
|---|---|
| `game_status`, `list_views`, `view_info` | live state |
| `set_partner_values(name, values, save)` | partner.ini keys live (or offline on disk when the game is off) - now also roomScale / roomOffsetX / roomOffsetY / resultUnder / sprites |
| `save_partner`, `reload_partner`, `rescan_partners`, `preview_partner` | partner lifecycle |
| `play_state`, `play_motion`, `tap_partner`, `set_expression`, `look_at`, `close_view` | drive a view |
| `screenshot`, `view_frame` | PNG images |
| `list_partners`, `get_partner`, `model_info`, `create_partner`, `delete_partner` | partners on disk (create writes Partner.xml; restart needed) |
| `build_and_install` | `build.ps1 -Install` (refuses while the game runs) |
| `read_mod_log(lines, filter)` | tail of `MelonLoader\Latest.log` |
| `test_game_start`, `test_game_stop` | drive.ps1 start / stop (start does NOT park the window) |
| `test_game_keys`, `test_game_hold`, `test_game_shot` | drive.ps1 key / keys / shot: these bring the window to the FRONT and use real key events - prefer `test_game_vkeys` and `screenshot` |

Extension, HTTP routes (`ngo_api.py`):

| tool | wraps |
|---|---|
| `theme(enabled=None)` | GET / POST `/api/theme` |
| `music_select_info()` | GET `/api/music-select` |
| `music_select_layout(changes, save=False)` | POST `.../layout` (documented keys and ranges) |
| `music_select_node(path, values)` | POST `.../node` (values may set `sprite: null`) |
| `music_select_image(image_id, values, delete)` | POST `.../image` |
| `music_select_reset(confirm)` | POST `.../reset` (warns: a later save would overwrite the saved preset) |
| `music_select_save(backup=True)` | POST `.../save`, copying select_layout.ini + settings.ini to `<STATE>\backups\` first |
| `music_select_tree(root, depth, player, save_path)` | GET `.../tree` |
| `music_select_sprite(path, player, save_path)` | GET `.../sprite.png` (image) |
| `get_live_partner(name)` | GET `/api/partners/<name>` |
| `set_partner_placement(name, screen, scale, offset_x, offset_y, result_under, save)` | result / room keys through `set_partner_values` |
| `test_screen_state(last)` | processes + entry state + quick-login count in one call (`screen` = current process) |
| `test_music_probe(find_id)` | GET `/api/test/music` |
| `test_ui_dump(contains, limit, save_path)` | GET `/api/test/ui` with filter / limit / full dump to file |
| `test_tree(of, up, depth, save_path)` | GET `/api/test/tree` |
| `test_renderers(save_path)` | GET `/api/test/renderers` |
| `test_select_perf(reset)` | GET `/api/test/selectperf` |
| `test_autoplay(mode, force)` | POST `/api/test/autoplay` (test instance only) |
| `test_force_rank(rank, force)` | POST `/api/test/rank` (test instance only) |
| `test_heapwalk(out_dir, force)` | POST `/api/test/heapwalk` (test instance only), moves the TSV out of LocalAssets |

Extension, scripts and jobs (`ngo_jobs.py`):

| tool | wraps | kind |
|---|---|---|
| `game_processes()` | process list + drive-state + running jobs | instant |
| `play_report(log_path, monitor_csv)` | `tools\ngo_play_report.py` (paths injected from the env vars) | instant, read-only |
| `heapwalk_diff(earlier, later, top)` | `tools\ngo_heapwalk_diff.py` (default: two newest walks) | instant |
| `soak_test(out_dir, minutes, play_seconds, heapwalk_after_tracks)` | `tools\ngo_soak.py` | job |
| `select_tune(select_candidates, result_candidates, out_dir)` | `tools\ngo_select_tune.py` | job |
| `to_collection(out_dir)` | `tools\ngo_to_collection.py` (leaves the test game running) | job |
| `run_offscreen_test(script, out_dir, args, env)` | whitelisted: `ngo_sprite_test.py`, `ngo_full_walk.py`, `ngo_skin_walk.py`, `phase_shots.py`, `ngo_attract_check.py` | job |
| `test_game_vkeys(keys, shot, save_path)` | the `tools\ngo_key.py` method: vkeys into the test game's amdaemon (picked by parent = drive-state injectPid), then a mod screenshot | instant |
| `job_status(job, tail)`, `list_jobs()` | job state, log tail, out folder files | instant |
| `job_stop(job, wait_seconds, force, stop_game)` | STOP file -> KeyboardInterrupt (script cleanup runs); `force` = exact-PID terminate + test keys back in settings.ini; then drive.ps1 stop for the test instance | instant |

Job mechanics (`job_boot.py`): the script runs as `__main__` with its normal argv and cwd `tools\`; output goes to
`<STATE>\jobs\<id>\output.log`, the exit record to `exit.json`; while it runs, the window of the drive-state game is
kept parked at x = -4200 (drive.ps1 itself opens it on screen); `settings.ini` is snapshotted at start. States:
running, finished, failed, stopped, died (no exit record: killed or crashed - run `job_stop` to clean up).
Stopping waits for the script's current `sleep` (up to `play_seconds` for a soak).

### Typical sequences

- Where is the game? `game_processes` -> `test_screen_state` -> `screenshot`.
- One off-screen check without login: `run_offscreen_test("ngo_attract_check.py", args=["120"], env={"CHECK_DT": "5"})`,
  poll `job_status`, the frames land in the out folder.
- Hand-driven collection-screen check: `to_collection` -> wait for state `finished` -> `test_game_vkeys(["X"])` ...
  -> `test_game_stop`.
- Leak hunt: `soak_test(minutes=25, heapwalk_after_tracks=[1, 3])` -> `heapwalk_diff()` (it also looks in
  `<ASSETS>\heapwalk`, where the soak's own walks land - move them out of LocalAssets afterwards) ->
  `play_report(log_path="<out>\\game.log")` for the per-track numbers (test runs have no monitor CSV).
