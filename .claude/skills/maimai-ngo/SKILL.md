---
name: maimai-ngo
description: Working knowledge for the maimai DX 1.70 (SDEZ) "NEEDY GIRL OVERDOSE" re-skin and its PartnerLive2D MelonLoader mod - the Harmony hooks and what each screen does, LocalAssets\PartnerLive2D (settings.ini, select_layout.ini, partner.ini, sprite packs), the sprite-pair partner backend, the mod's HTTP control API and the partnerlive2d MCP server, building / installing the mod, off-screen muted test runs (drive.ps1, vkeys into amdaemon), resources.assets texture / colour / mesh patching with UnityPy, the launcher (priority, P-cores, GPU clock lock, monitor sampling), GC / leak / stutter checks and play reports, select / result / partner-room layout tuning, account tweaks, and a long pitfalls list. Use it for ANY work on the maimai NGO skin, PartnerLive2D, Sinmai.exe / AquaMai / segatools / Maimoller cabinet setups, or when asked to tune, test, patch, debug, measure or package the skin.
---

# maimai NGO skin + PartnerLive2D

The skin turns maimai DX 1.70 (Unity 2018.4.7f1, Mono with the Boehm GC) into "maimai NEEDY GIRL OVERDOSE" in two layers:

1. **Asset patches** - textures, Image colours, sprite meshes, prefab values, particle systems written straight into
   `Sinmai_Data\resources.assets` (plus a few StreamingAssets bundles and XMLs) by the Python tools in `tools\`.
   No runtime cost; this is where almost all of the look lives.
2. **The PartnerLive2D mod** (`Mods\PartnerLive2D.dll`, C#, Harmony) for what assets cannot do: the animated partner
   (Live2D / own rig / sprite pair) on music select, result and the partner room, the music-select layout, the boot
   cover, the random Ame/KAngel transitions, the upload / track-start scenes, the pixel font, cleanup timing, the
   always-on play statistics, account tweaks and a local HTTP control + test API.

## Paths used in this skill

| placeholder | meaning | env var (MCP) |
|---|---|---|
| `<GAME_ROOT>` | folder with `launcher.ps1`, `Play-maimai.bat`, `playlogs\`, backups (parent of `<PKG>`) | `PARTNERLIVE2D_GAME_ROOT` |
| `<PKG>` | the game package (`Sinmai.exe`, `Mods\`, `Sinmai_Data\`) | `PARTNERLIVE2D_PACKAGE` |
| `<ASSETS>` | `<PKG>\LocalAssets\PartnerLive2D` (mod data) | - |
| `<PROJECT>` | this repository (`src\`, `tools\`, `mcp\`) | `PARTNERLIVE2D_PROJECT` (default: the folder above `mcp\`) |
| `<STATE>` | MCP job logs, snapshots, moved heap walks, backups | `PARTNERLIVE2D_STATE` (default: `%TEMP%\partnerlive2d-mcp`) |

The game folder must sit on a plain-ASCII path and not on `E:` or `Y:` (segatools redirects those drives).
Many `tools\*.py` scripts have a fixed package path and port 8791 built in (constants at the top of each script);
the MCP job tools refuse to run them against another setup instead of driving the wrong folder. Edit the constants
to match your machine.

## Ground rules (each has bitten before)

- **Never start a game while any `Sinmai.exe` of `<PKG>` runs** - someone may be playing, or another agent is
  testing. Check with `game_processes` (or `Get-CimInstance Win32_Process -Filter "Name='Sinmai.exe'"` and compare
  ExecutablePath); if one runs, wait and re-check every 60 s. Only the game whose PID is in
  `tools\drive-state.json` is a test instance.
- **Stop processes only by exact PID**, and only ones you started. `taskkill /IM` / `Stop-Process -Name` kill every
  process of that name on the machine. `drive.ps1 stop` also stops every `amdaemon.exe` under `<PKG>`, so run it only
  when the running game is your own test instance.
- **Tests run off screen and muted**: `tools\drive.ps1 start` mutes the default audio endpoint, switches AquaMai to a
  540x960 shared-audio window and disables AquaMai AntiLag (it taps a global Ctrl every ~10 s, which lands in whatever
  window the user is typing in); park the window at x = -4200 and press keys through `tools\vkeys.py` (no focus).
  Focus-stealing key presses let the user's own typing reach the game (one Esc = AquaMai InstantQuit).
- **Always end with `drive.ps1 stop`** (`test_game_stop`, or `job_stop` for MCP jobs): it restores `AquaMai.toml`,
  the mute state and removes test partner folders. A leftover `tools\drive-state.json` means a run was not stopped.
- **Never build or install while the game runs** (the loaded DLL is locked; a staged copy goes to
  `<GAME_ROOT>\pending-mods\`, which the launcher moves in before the next start).
- **Back up before writing any game file**; patch tools write `resources.assets.new` and a separate copy step puts it
  in place (see `references/skin-asset-pipeline.md`).
- **Private files never go into a commit, a pack or a screenshot you share**: `segatools.ini` (machine ID and network
  settings), `DEVICE\aime.txt` (card number), `AquaMai.toml`, `config_*.json`, `<ASSETS>\account_edit*.ini`, game
  logs, `heapwalk\*.tsv`, screenshots that show a profile name. The release packers already skip them.
- **Test logins use a registered card** (`DEVICE\aime.txt`): guest play skips the profile, partner, collection and
  account-tweak paths you usually need to see. Every login ends with an upload, so tests do change the account's
  play history (autoplayed scores are protected by AquaMai DontRuinMyAccount). Never press a button on the attract
  screen except the card scan: any button there starts a GUEST entry.

## Where to look

| task | read | MCP tools |
|---|---|---|
| what a hook / screen does, file layouts, ini keys | `references/architecture.md` | `game_status`, `get_partner`, `get_live_partner` |
| HTTP routes, every MCP tool, safety of each | `references/http-api-and-mcp.md` | all |
| build, install, run a test, drive keys, read logs | `references/build-install-test.md` | `build_and_install`, `test_game_*`, `run_offscreen_test`, `job_*`, `read_mod_log` |
| change a texture, colour, mesh, particle, bundle | `references/skin-asset-pipeline.md` | `test_ui_dump`, `test_tree`, `test_renderers`, `music_select_sprite` |
| launcher, GPU lock, monitor CSV | `references/launcher.md` | `play_report` |
| stutter, memory growth, leaks, play reports | `references/performance.md` | `play_report`, `soak_test`, `test_heapwalk`, `heapwalk_diff`, `test_select_perf` |
| partner size / place on select, result, room; draw order | `references/tuning-layouts.md` | `select_tune`, `music_select_*`, `set_partner_placement`, `test_tree` |
| raise character levels, equip a partner, profile edits | `references/account-tweaks.md` | - |
| something behaves oddly | `references/pitfalls.md` (read it before any non-trivial change) | |

## First steps in a new session

1. `game_processes` - is a game running, and is it a test instance?
2. `read_mod_log(filter="PartnerLive2D")` or `game_status` (game running) to see the installed mod's state.
3. For a real session someone played: `play_report` (per-track load times, hitches, heap after cleanup).
4. Before changing art: `test_ui_dump` on the screen in question, then the stock-format study
   (`references/skin-asset-pipeline.md`, "Study the stock format first").
5. Before tuning a layout: `references/tuning-layouts.md`; prefer candidate sheets (`select_tune`) over one-off edits.

## MCP server in one paragraph

`<PROJECT>\mcp\server.py` (FastMCP, stdio, Python 3.12 + `mcp`; `pefile` for vkeys) registers as `partnerlive2d`:
`claude mcp add --scope user partnerlive2d -- <python.exe> <PROJECT>\mcp\server.py` (set the env vars above when
your folders differ). 59 tools: live partner control (views, motions, screenshots), offline partner management,
build / install, the test game (`test_game_*`), the remaining HTTP routes (`theme`, `music_select_*`,
`get_live_partner`, `set_partner_placement`, `test_*`), report scripts (`play_report`, `heapwalk_diff`) and
background test jobs (`soak_test`, `select_tune`, `to_collection`, `run_offscreen_test`, `test_game_vkeys`,
`job_status` / `job_stop` / `list_jobs`). Tests: `python mcp\test_offline.py`, `python mcp\test_ngo_tools.py`,
`python mcp\test_stdio.py` (fake game folders; they never start the game).
