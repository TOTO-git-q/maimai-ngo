# Pitfalls (symptom -> cause -> fix)

## Processes, safety, test runs

1. **Killed someone's game / a stranger process.** `taskkill /IM Sinmai.exe` or `Stop-Process -Name` hits every
   process of that name on the machine. -> Stop only by exact PID you started; check the executable path.
2. **`drive.ps1 stop` stopped the other game's amdaemon.** It stops every `amdaemon.exe` under `<PKG>` by path. ->
   Run it only when the running game is your drive.ps1 instance (`game_processes`).
3. **A stale `tools\drive-state.json`** (a crashed run): the next `drive.ps1 start` backs up the still-modified
   AquaMai.toml as the "original" and `stop` would Stop-Process PIDs that may belong to new processes by now. -> Check
   the PIDs are gone, run `drive.ps1 stop` once, then start (MCP game jobs refuse until it is gone).
4. **Ctrl typed into the user's window every ~10 s during a test.** AquaMai `[Utils.AntiLag]` taps a global Ctrl; the
   test game counts as focused, so `ActivateWhileBackground = false` does not help. -> drive.ps1 comments the whole
   section out for tests and restores the toml on stop. Never run a test game with the user's own toml.
5. **One Esc quit the test game / the user's keystrokes reached it.** Borrowing focus per key press. -> vkeys (no
   focus); keep the window parked at x = -4200; never use drive.ps1 `key`/`keys`/`shot` off screen.
6. **Keys "do nothing" through the mod's `/api/test/key`.** Buttons / Test / Service / scan are read in amdaemon
   (mai2hook.dll, hinata.dll), not in Sinmai. -> vkeys into amdaemon.
7. **Confirm button dead after a test (real buttons too).** vkeys restored the imports right after a press; the io
   board polls only while the game counts as foreground, so the last state stayed latched. -> Wait ~1 s with every key
   up before `restore()`; to unstick: attach, sleep 1 s, restore.
8. **A GUEST credit started.** A button pressed on the attract screen. -> Only the card scan there.
9. **Login timed out back to the attract loop.** EntryProcess needs C ("play with this Aime?" -> yes) after the scan;
   a registered card may need a second scan until the entry state is `ConfirmPlay`.
10. **A ticket was spent.** C pressed on the ticket screen. -> drive.ps1's EntryToMusicSelection skips it; with
    `DRIVE_FULL_ENTRY=1` press carefully.
11. **Mode select timed out.** A 7 s press cadence was too slow. -> ~2.5 s.
12. **Normal-mode START waits forever.** AquaMai DisableTimeout + ForceAsServer: the in-store matching countdown never
    ends. drive.ps1 restores the stock timers for tests (`DRIVE_KEEP_TIMEOUT=1` keeps the user's). Freedom mode skips
    the wait, so judge START-related clips (Enter / ReleaseWait) in freedom mode; normal mode adds ~50 s.
13. **New cards stuck after START in normal mode.** An older `hideMatching` skipped the whole `PartyExec`, which in
    NORMAL mode is what turns START (PrepareFinish) into the track start; freedom / course set that flag elsewhere.
    -> Hide the matching category by dropping its placeholder after `SetConnectData` and answering "no change" from
    `IsConnectStart` / `IsConnectStop`; never skip PartyExec.
14. **Freedom mode never ends; killing the game lost the session's scores.** InfinityTime + DisableTimeout = no end,
    and a kill skips the end-of-session upload. -> Service long press on music select (AquaMai OneKeyEntryEnd).
15. **A soak played only ~4 tracks.** OneKeyEntryEnd ends the credit; the helper logs in once per run. One soak = one
    credit.
16. **Autoplay always shows rank D.** AquaMai DontRuinMyAccount resets the autoplayed score. -> `/api/test/rank`
    (forced rank animation) to see high-rank clips and partner tiers.
17. **Off-screen frame times look terrible.** Off screen the game runs ~30 fps. -> Judge growth (heap, private MB)
    off screen; judge stutter on the cabinet with `[play]` lines.
18. **Test sounds on the speakers.** -> drive.ps1 mutes the default endpoint and restores the previous mute; never
    start the game with the normal launcher for a test.
19. **`build_and_install` refuses although no game is visible.** A crashed game can linger as a 0-thread zombie
    (cannot be killed: access denied). -> Confirm no live Sinmai (`Threads.Count -gt 0`), then `build.ps1 -Install`.
20. **New DLL not picked up.** The game was running (DLL locked). -> Stage into `pending-mods\`; the launcher moves it
    in before the next start.
21. **MelonLoader writes no log, game exits in seconds.** The `version.dll` proxy was not loaded (System32's was used
    instead) on one machine. -> The same proxy as `winhttp.dll`; keep only one proxy (`version.dll.disabled`).
22. **hinata.dll changed by itself.** It auto-updates at launch (the old copy is kept as `.old`).
23. **AquaMai loaded twice.** A local AquaMai.dll next to MuMod, or a chart manager installing it. -> MuMod only.
24. **PowerShell script dies at the first quote on a non-English Windows.** PS 5.1 reads BOM-less scripts in the ANSI
    code page. -> `.ps1` / `.bat` ASCII only (non-ASCII text in a separate UTF-8 data file).
25. **Registry / process changes from an agent session do not stick.** Packaged / sandboxed hosts virtualise HKCU and
    may contain child processes. -> Start installers and the launcher through `explorer.exe` (or a desktop shortcut
    the user double-clicks).

## The mod and its config

26. **Partner silently missing.** partner.ini has an unknown key (PartnerConfig.Set throws), the `model` file does not
    exist, or `naviCharaId` is missing; the log says `skip <folder>: ...`. Markers must be comments
    (`# created_by = mcp`).
27. **Partner not on the result screen.** It shows only for the EQUIPPED partner id (`EquipPartnerID` == partner.ini
    `partnerId`). -> Equip it (in game, or `equipPartner` once).
28. **New partner not in the collection.** `Partner.xml` is read only at game start (UTF-8 with BOM, `isDefault`
    true). -> Restart after `create_partner`.
29. **A stock navi flashes / shows instead of the custom one.** A new naviChara id has no prefab; the game falls back
    to a stock navi, which the mod hides (CanvasGroup alpha 0). Screens the mod does not handle yet (first-play
    settings, login bonus, shop get window, friend battle result) show that fallback.
30. **Select layout ignored after a restart.** `selectView = true` in settings.ini decides at startup; `enabled` inside
    `select_layout.ini` is ignored then.
31. **Select layout switched itself off.** Any layout / model / reaction exception faults the feature for the run
    (`music_select_info().failure`, log `music-select disabled and restored`). -> Fix, restart.
32. **Saved preset lost.** `music_select_reset` then `music_select_save` writes the defaults; there is no route to
    re-read `select_layout.ini`. -> Restart to get the saved preset back; MCP save keeps a backup.
33. **docs\SELECT_VIEW.md ranges do not match.** They are from the first version (partner height max 900 etc.). ->
    Trust `MusicSelectSettings.cs` (height 100..2400, bottomY -1400..400, bottomX -600..600).
34. **Comment lines in settings.ini starting with `;`.** Parsed as junk keys when they contain `=` (harmless only
    because the key starts with `;`). -> Use `#`. No ini file here supports inline comments: everything after `=` is
    the value (`scale = 0.9  # bigger` fails to parse and the whole partner is skipped).
35. **Settings change had no effect.** settings.ini is read once at game start. -> Restart (or use the live API).
36. **HTTP 503 "main thread did not respond".** Loading screen, heap walk or a frozen game; MainThread.Call waits 5 s.
    -> Retry after the load.
37. **POST refused with 403.** Missing `X-PartnerLive2D: 1` header, or a browser `Origin` from another site.
38. **`/api/test/autoplay` 409.** settings.ini `testInput = true` was not set at game start.
39. **Heap-walk TSVs ended up in a release.** They are written into `<ASSETS>\heapwalk\`. -> Move them out
    (`test_heapwalk` does; soak walks need a manual move).
40. **Every finished screen stayed in memory (+~50 MB per track).** `ConditionalWeakTable` holds keys strongly on
    Boehm. -> Never use CWT; instance-id dictionaries cleared per screen.
41. **Hitches every ~11 s on music select.** Per-frame managed garbage (path strings, `Object.name` reads) feeding the
    GC. -> Cache names / paths; no allocation in per-frame hooks.
42. **Hitches during a long real session after turning on diagnostics.** `frameLog`'s per-minute census
    (`FindObjectsOfTypeAll`) hitches by itself. -> Real play uses the always-on `[play]` / `[clean]` lines.
43. **Freeze at song start growing over a session.** The game's own unload+GC at `GameProcess.OnStart` right after the
    fade's one. -> `NgoCleanupTiming` (`cleanupMinGap`).
44. **One ~0.75 s frame when the result screen opens.** The game's `GC.Collect` in `GameProcess.OnRelease` (GC was off
    during the song). By design; not a mod bug.
45. **Select hitched once, 107 ms.** A PNG decoded twice on the first apply. -> PNG cache + preload at startup.

## Sprite partner backend

46. **Sprite partner falls back to Live2D: "sprite compositing shader unavailable" / Standard missing.** The game build
    has no Standard shader. -> `Sprites/Default` with `_RendererColor` and `_Flip` set.
47. **Sprites invisible or upside down.** Raw BC7 upload needs bottom-up rows; the packs are straight alpha with
    nearest-colour bleed (Sprites/Default premultiplies). -> Export with `ngo_sprites_export.py`, check with
    `Headless.exe spritepack`.
48. **Exporter / review script crashed decoding BC7.** `etcpak.decompress_bc7` fails (MemoryError on bytes, TypeError
    on bytearray) on this setup. -> `texture2ddecoder.decode_bc7` (returns BGRA). In task specs, allow an equivalent
    tool when a named one fails.
49. **Clip switch jumps by a pixel.** The K idle loop's rest frame is 1-2 px off from the other K clips. -> Every
    switch crossfades 0.12 s (outgoing at full opacity, incoming over it at the fade weight).
50. **Pair looks half loaded for ~25 s off screen.** The loader uploads about one texture per frame (622 textures,
    ~261 MB VRAM on select). -> Wait for `loadedTextures == totalTextures` (view info) before judging.
51. **K and Ame overlap wrongly.** K is drawn in front of Ame (`drawOrder ["Ame", "K"]`).
52. **Sprites look soft.** They are upscaled ~1.5x on screen (pair ~900 px tall in a 1300-unit rect). Check sharpness
    in game, not in the export.

## Asset patching

53. **Game file corrupted / truncated.** Writing while UnityPy still reads the source lazily. -> Build all output with
    `env.file.save()` first, write `.new`, assert the size (> 60 MB), copy over with the game stopped
    (`ngo_install_new.ps1`).
54. **First edit to an object vanished.** Read-modify-save of the same object twice in one run (the second read comes
    from the source). -> One read-modify-save per object per run.
55. **MonoBehaviour typetrees fail.** Loaded a backup copy outside the game folder (external references missing). ->
    Load the game folder's file with `TypeTreeGenerator("2018.4.7f1")` + `load_local_dll_folder(<Managed>)`.
56. **Redrawn panel came out the wrong colour / muddy.** Many panel textures are white masks the game tints (Image
    `m_Color`, or code). A coloured texture under a tint turns muddy. -> Keep the texture white/grey, change the tint
    (`ngo_tint_map.py`, `ngo_patch_colors.py`); code-set tints need runtime `NgoTint` (`ngo_tint.txt`).
57. **Difficulty colours broke.** `Null_Selector_ColorBase._colors` is indexed by difficulty. -> Only the category
    blue (index 7) changes; BASIC green, ADVANCED, EXPERT red, MASTER purple ... stay.
58. **Redrawn art clipped at odd edges.** Stock sprites have meshes traced around the stock art and trimmed
    `textureRect`s. -> `ngo_full_quad.py` (not for Sliced sprites) and the patcher's rect opening.
59. **9-slice art deformed.** Drawn as a full picture. -> Respect the border (e.g. tray top 72 rows fixed, 12
    stretched); preview with the game's slicing.
60. **Selected tab face faded to white on every pulse.** Base and overlay roles swapped: the base is the full-colour
    art, the `WhiteBase` overlay a white silhouette whose alpha the game pulses. Side tabs: the overlay is only a faint
    highlight, so outlines belong in the tinted base.
61. **Changing `v170transi` did nothing.** It is an unreferenced full image; the transition is assembled at runtime
    from white parts tinted by Image colour (`ChangeScreen_01`) plus the `Sub_*` banners.
62. **Title logo flashed solid white.** `Mask_Logo_all` is only the shine's mask; a solid logo there makes the 67 %
    white flash cover the whole logo. -> Leave that mask empty.
63. **Title logo did not fit.** 2x the source logo is wider than the circle; the display frame is 900x259.
64. **Game crashed entering the attract screen after a background patch.** Changed a sprite's `m_SortingLayer`. ->
    Material render queue 2450 instead (MeshRenderer sorting layer edits have no runtime effect either).
65. **Background image upside down / off centre.** The sky plane shows its texture rotated 180 deg; 1 unit = 1 px; the
    plane centre is 410 px above the circle centre (local y -413.15).
66. **Background will not "float" without a mod.** The stock Loop animation drives no Transforms; motion comes from
    shaders and particles. -> Re-tune the particle systems (`ngo_bg_motion.py`).
67. **A patch disappeared after re-running the motion replay.** `ngo_motion_try.sh` starts from the before-motion
    backup and re-applies only what it lists. -> Add every new patch to it; it does not replay the end flow.
68. **Geometry got applied twice.** Tab-row / board fit scripts are relative. -> Never re-run them on a fitted preset;
    the ini is the source of truth.
69. **The puppet still showed the bear on result / select.** A second copy lives in the navi bundle
    (`navichara\prefab\ui_navichara_00.ab`). -> Patch it too (`NGO_TARGET=<ab>`), keep `.before-*` copies.
70. **Upload screen still showed mascots.** They live in `DataSave/Chara` (several rigs picked at random). -> One
    CanvasGroup on the group; find the Chara holding Tapp first (the continue prefab carries the same strip).
71. **Transition always showed the same variant.** Refreshing the roll clock on every image kept the first roll
    forever. -> One roll per transition (images within ~2 s follow it).
72. **Attract screen went black when skipping the logos.** The SEGA / ALL.Net clip also brings the title canvas up.
    -> Play it at 30x instead of skipping.
73. **Runtime theme flashes stock art.** ThemeOverride swaps after the game set the sprite. -> Patch the assets;
    keep the runtime theme for quick experiments only.
74. **A redraw was rejected by the patcher.** PNG size differs from the stock texture (skipped, logged). -> Exact stock
    size.
75. **Pixel font not applied.** Unity 2018 cannot load a TTF from disk; the bytes must be in the game's LiberationSans
    Font object (`ngo_font_data.py`) and `pixel_font.txt` must exist.

## Cabinet and launcher

76. **Game window half size with a title bar on the controller screen.** Unity ignores per-monitor DPI. -> Launcher
    passes `-popupwindow -screen-width/-height` with the real pixels and runs `fit-window.ps1`.
77. **Hitches every 2 s on the cabinet.** `fit-window.ps1` resized the window on a timer. -> Resize only when off
    screen; otherwise only z-order.
78. **Game paused / buttons dead when the PC's focus moved.** Without AquaMai AntiLag ActivateWhileBackground the game
    pauses in the background. -> On for real play (off for tests, see 4).
79. **Micro-stutter in songs on a laptop.** Vendor power management pinned "best efficiency", GPU idling down. ->
    Vendor performance mode, the launcher's GPU clock lock, P-cores, EcoQoS off.
80. **GPU lock task missing.** It needs admin once: `gpu-lock\setup-gpu-lock.bat` (Register-ScheduledTask; schtasks
    `/SD` refused a fixed date in a non-US date format).
81. **No sound / laggy sound.** AquaMai WASAPI exclusive uses the DEFAULT device; HDMI / Bluetooth took over the default.
    -> Set the wired device as default (launcher.log warns).
82. **TV at 30 Hz.** Many 4K TVs default to 30 Hz; the game vsyncs at 60. -> Set 60 Hz.
83. **Game folder on E: / Y: or with non-ASCII characters.** segatools redirects E:/Y:; the game chokes on non-ASCII
    paths. -> e.g. `<GAME_ROOT>`.

## MCP and agents

84. **MCP job refuses "written for <PKG>".** The tools\ game scripts have the package path and port 8791
    built in. -> Use that layout or edit the scripts' constants (and then `ngo_common.SCRIPTS_PACKAGE`).
85. **A job "died".** It was killed (or crashed) before writing `exit.json`. -> `job_stop(job)` cleans up (test keys
    back in settings.ini, drive.ps1 stop for the test instance).
86. **Edited the MCP server and the tools did not change.** The client caches the server process. -> Restart the
    client / MCP server.
87. **An image-model or coding agent (e.g. Codex exec) stopped half way.** Long exec runs end by themselves after a few
    minutes / an output budget. -> Have it end its worklog with `CONTINUE: <next>` and relaunch (`tools\codex_loop.sh`)
    until the worklog ends with DONE or BLOCKED.

## Collection screen and hooks that never fire

88. **A hook on a tiny game method never runs.** Mono's JIT inlines small methods (a few IL bytes, e.g.
    `AssetManager.GetPartnerTexture2D(string)` called from the `(int)` overload, `InputManager.GetTouchPanelAreaDown`).
    The custom partner showed a white square in the collection list because the list asks by id. -> Hook the overload
    the caller really uses (`GetPartnerTexture2D(int)`), or a bigger method one level down
    (`IO.TouchPanel.GameInputLog.TriggerOn`, internal: find it with `AccessTools.TypeByName`).
89. **The partner card in the collection carousel shows no partner picture.** Only the list inside the partner genre
    does. The genre cards carry title sprites: `UI_CLC_ContentsTitle_05` = partner (left->right: 07 exit-left, 03 title,
    01 icon, 02 plate, 06 frame, 05 partner, _Shop, 07 exit). The carousel does not wrap. Button 6 = left, 3 = right,
    4 = enter. -> `tools\ngo_collection_check.py` walks there by title.
90. **The partner room never opens from keys.** It is the present screen and opens on a touch of the centre card's
    button area (B4/B5) when the partner is owned and not a copyright partner. -> `POST /api/test/touch {"area":"B4"}`
    (one touch-down, delivered on the first frame the game asks for that area).
91. **`/api/test/ui` coordinates of an off-screen test window are not 0..540.** They come from the canvas camera.
    -> Compare widths/relative x (the centre card is the widest), do not test for "x inside the screen".
92. **The card scan is ignored and the run becomes a guest play.** The reader may come up minutes after the attract
    screen when MuMod loads slowly, and on a failed server lookup the entry screen asks `ConfirmGuest`. -> Scan until
    hinata.log logs a new read; press yes only in entry state `ConfirmPlay`; never press anything else on the attract
    screen (`tools\ngo_to_collection_lib.py`).
93. **A fake first-play test changed the real account's team.** The first-play path refills `CharaSlot` with starters
    and the upload saved it. -> Restore every field the faked path can touch (TestFirstPlay now restores CharaSlot /
    CharaLockSlot); a lost team comes back with `account_edit.ini` `chara = ...`.
