# partnerlive2d MCP server

Stdio MCP server that lets an AI agent work on the **PartnerLive2D** maimai mod and the NGO skin: live control of
the running game (the mod's HTTP API on `127.0.0.1:8791`), offline partner management on disk (partner.ini, model
copy, `Partner.xml`), building/installing the mod, reading the MelonLoader log, driving a test instance of the game,
the mod's tuning / debug routes and the project's test and report scripts. Python 3.12 + `mcp` (and `pefile` for
`test_game_vkeys`).

## Files

| file | content |
|---|---|
| `server.py` | core tools (28) + loading of the extensions (a broken extension is reported on stderr, the core keeps working) |
| `ngo_api.py` | tools for the remaining HTTP routes: theme, music-select tuning, live partner values, `/api/test/*` |
| `ngo_jobs.py` | report scripts, background test jobs, vkeys key presses |
| `ngo_common.py` | shared paths, "which game is running" guards, ini helpers |
| `job_boot.py` | runs one `tools\` script as a job (STOP file, keeps the test window off screen, exit record) |
| `test_offline.py`, `test_ngo_tools.py`, `test_stdio.py` | tests (fake game folders; never start the game) |

## Configuration (environment, read at call time)

| variable | default |
|---|---|
| `PARTNERLIVE2D_PACKAGE` | `<PKG>` |
| `PARTNERLIVE2D_PROJECT` | the folder above `mcp\` |
| `PARTNERLIVE2D_PORT` | `port` in `LocalAssets\PartnerLive2D\settings.ini`, else 8791 |
| `PARTNERLIVE2D_GAME_ROOT` | parent of the package (launcher, `auto-login.py`, `playlogs\`) |
| `PARTNERLIVE2D_PLAYLOGS` | `<game root>\playlogs` |
| `PARTNERLIVE2D_STATE` | `%TEMP%\partnerlive2d-mcp` (job logs, settings snapshots, moved heap walks, backups) |
| `PARTNERLIVE2D_PYTHON` | the python running the server (for jobs and report scripts) |

The `tools\*.py` game scripts have `<PKG>` and port 8791 built in; the job tools refuse to run them
against another package or port.

## Tools

Live (game running, else a clear "game not running" error): `game_status`, `list_views`, `view_info`,
`set_partner_values` (falls back to editing partner.ini when the game is off; also roomScale / roomOffsetX /
roomOffsetY / resultUnder / sprites), `save_partner`, `reload_partner`, `rescan_partners`, `preview_partner`,
`play_state`, `play_motion`, `tap_partner`, `set_expression`, `look_at`, `close_view`, `screenshot`, `view_frame`.

Offline: `list_partners`, `get_partner`, `model_info`, `create_partner`, `delete_partner`, `build_and_install`
(refuses while Sinmai.exe from the package is running), `read_mod_log`.

Test instance (`tools\drive.ps1`): `test_game_start`, `test_game_keys`, `test_game_hold`, `test_game_shot`,
`test_game_stop`. Key map: buttons 1-8 = W,E,D,C,X,Z,A,Q (clockwise from top-right); Enter held 3 s = Aime card.
`test_game_keys` / `test_game_hold` / `test_game_shot` bring the window to the front; for off-screen runs use
`test_game_vkeys` and `screenshot`.

Mod routes (`ngo_api.py`): `theme`, `music_select_info`, `music_select_layout`, `music_select_node`,
`music_select_image`, `music_select_reset`, `music_select_save` (backs up first), `music_select_tree`,
`music_select_sprite`, `get_live_partner`, `set_partner_placement` (result / room keys), `test_screen_state`,
`test_music_probe`, `test_ui_dump`, `test_tree`, `test_renderers`, `test_select_perf`, `test_autoplay`,
`test_force_rank`, `test_heapwalk` (the last three only on the drive.ps1 test instance).

Scripts and jobs (`ngo_jobs.py`): `game_processes`, `play_report`, `heapwalk_diff` (instant); `soak_test`,
`select_tune`, `to_collection`, `run_offscreen_test` (background jobs: refuse while any game of the package runs,
keep the window off screen, snapshot settings.ini); `test_game_vkeys`; `job_status`, `job_stop`, `list_jobs`.

Notes: tools that write to the package (`create_partner`, `delete_partner`, offline `set_partner_values`,
`build_and_install`, `music_select_save`) change real game files. The "created by this MCP" marker is the comment line
`# created_by = mcp` in partner.ini (a plain `created_by = mcp` key would make the mod skip the partner:
PartnerConfig.Set throws on unknown keys). A brand-new partner's `Partner.xml` is only read at game start.
Processes are only stopped by exact PID (jobs: checked against their creation time).

## Register with Claude Code

```
claude mcp add --scope user partnerlive2d -- <path to python.exe> <repo>\mcp\server.py
```

or in a config file:

```json
{
  "mcpServers": {
    "partnerlive2d": {
      "command": "<path to python.exe>",
      "args": ["<repo>\\mcp\\server.py"],
      "env": {"PARTNERLIVE2D_PACKAGE": "<game folder>\\Package"}
    }
  }
}
```

## Tests

`python test_offline.py` (fake package in `_testpkg`, removed afterwards; includes a stub HTTP server for the live
paths; uses the Cubism SDK's Haru sample, `CUBISM_SDK_NATIVE` = SDK folder), `python test_ngo_tools.py` (fake game +
fake project with stand-in scripts in `_testngo`; jobs, guards, reports, every new route against a stub API) and
`python test_stdio.py` (spawns the server, initialize + tools/list, all 59 tools present).
