# The launcher (`<GAME_ROOT>\launcher.ps1`)

Started by `Play-maimai.bat` (hidden PowerShell, minimised). It lives in the game folder, not in this repo; the GPU
lock helpers ship in the patch pack (`tools\patch_extras\`, `<GAME_ROOT>\gpu-lock\`). ASCII only, because Windows
PowerShell 5.1 reads a script without BOM in the ANSI code page (on a Chinese system GBK eats quotes and the script
dies). Log: `<GAME_ROOT>\launcher.log`, rewritten each start.

Order of work:

1. Checks: the game folder is not on `E:` / `Y:` (segatools redirects them) and is plain ASCII; `Sinmai.exe` exists.
2. Per-user Windows prefs for `Sinmai.exe` (idempotent): `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` =
   `GpuPreference=2;` (discrete GPU) and AppCompat `~ DISABLEDXMAXIMIZEDWINDOWEDMODE` (no fullscreen optimisations).
3. Power mode slider -> "Best performance" (`PowerSetActiveOverlayScheme`, GUID `ded574b5-45a0-4f42-8737-46345c09c238`),
   restored on exit. Some laptops pin the overlay in their vendor power manager; then this has no effect and the
   vendor's performance mode (e.g. a key combo) must be chosen by hand.
4. Logs the default audio output and warns for Bluetooth / HDMI / virtual devices: AquaMai opens the DEFAULT device in
   WASAPI exclusive mode, and plugging in an HDMI screen silently changes the default.
5. Stops leftovers of THIS install only, matched by full executable path, by PID (`Sinmai.exe`, `amdaemon.exe`,
   `inject.exe`); moves staged mods from `pending-mods\` into `Mods\`.
6. Starts `inject.exe -d -k mai2hook.dll amdaemon.exe -f -c config_common.json config_server.json config_client.json`
   (with `OPENSSL_ia32cap=:~0x20000000` as the stock start.bat), then `Sinmai.exe -monitor <n>
   --melonloader.hideconsole --melonloader.disablestartscreen`. Monitor `auto` = the 2nd screen when one is connected
   (the controller's screen). For a monitor other than 1 it adds `-popupwindow -screen-width/-height` with the screen's
   real pixels (Unity ignores per-monitor DPI scaling and would open half size with a title bar) and starts
   `fit-window.ps1`, which keeps the window topmost at screen size without taking focus (it only resizes when the
   window is actually off; resizing every 2 s made the game hitch).
7. **GPU clock lock**: runs the scheduled task "maimai GPU lock" (`nvidia-smi -lgc 1800,3090`) so the GPU never idles
   down between bursts; "maimai GPU unlock" (`nvidia-smi -rgc`) on exit. The tasks need admin once:
   `gpu-lock\setup-gpu-lock.bat` registers them with `Register-ScheduledTask`, no trigger, highest privileges
   (schtasks `/SD` wanted the date in the local format and refused a fixed one); `remove-gpu-lock.bat` deletes them.
8. **Monitor sampling**: starts `play-monitor.ps1 -GamePid <pid>` hidden. Every 5 s it appends to
   `playlogs\monitor_<game start yyyyMMdd_HHmmss>.csv`: `time, private_mb, working_set_mb, cpu_pct, gpu_mhz,
   gpu_util_pct, gpu_temp_c, gpu_pstate, gpu_slowdown` (nvidia-smi clocks.gr, utilization, temperature, pstate,
   clocks_event_reasons.active). One sampler per game run; it can also be started by hand while the game runs.
   `tools\ngo_play_report.py` (MCP `play_report`) joins it with the MelonLoader log.
9. **Scheduling**: `Sinmai.exe` -> High priority, affinity = the P-cores (mask of the logical processors with the
   highest EfficiencyClass from `GetLogicalProcessorInformationEx`; logged as `mask 0x...`), power throttling (EcoQoS)
   off via `SetProcessInformation(ProcessPowerThrottling)`; `amdaemon.exe` -> AboveNormal, throttling off.
10. Waits for the game to exit, then stops this install's amdaemon / inject by PID, unlocks the GPU, restores power.

`<GAME_ROOT>\auto-login.py` (vkeys scan + "yes" on `ConfirmPlay`, F3 quick login) is kept but no longer started by
the launcher: the game itself logs in on Enter = card scan. A launcher started from a sandboxed / packaged agent host
may get virtualised registry writes and child processes; start `Play-maimai.bat` through `explorer.exe` there.

Cabinet notes that are not code: the controller screen may be a portrait 4K TV (AquaMai Window = windowed borderless
at the panel's native size is correct there); TVs often default to 30 Hz - set 60 Hz, the game vsyncs at 60; game
audio must be the Windows default device.
