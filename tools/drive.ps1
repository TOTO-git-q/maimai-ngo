# Drive the game for manual-style testing. ASCII only.
#   drive.ps1 start                 mute audio, small window, shared-mode audio, launch (state in drive-state.json)
#   drive.ps1 key <Name> [holdMs]   focus game window, press/hold a key (Name = System.Windows.Forms.Keys name)
#   drive.ps1 shot <file.png>       focus game window, capture it
#   drive.ps1 stop                  stop game by PID, restore AquaMai.toml + mute + test configs
param(
    [Parameter(Position = 0)][string]$Cmd,
    [Parameter(Position = 1)][string]$Arg1,
    [Parameter(Position = 2)][int]$Arg2 = 120,
    [string]$Package = 'D:\maimai\Package'
)
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot 'drive-state.json'

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Drv {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    public static void Key(byte vk, bool up) {
        byte sc = (byte)MapVirtualKey(vk, 0);
        uint flags = up ? 2u : 0u;
        if (vk == 0x24 || vk == 0x2D || vk == 0x2E || vk == 0x21 || vk == 0x22 || vk == 0x23 || (vk >= 0x25 && vk <= 0x28)) flags |= 1u; // extended
        keybd_event(vk, sc, flags, UIntPtr.Zero);
    }
}
'@
[void][Drv]::SetProcessDPIAware()

function Get-Game {
    if (-not (Test-Path $state)) { throw 'not started' }
    $s = Get-Content $state -Raw | ConvertFrom-Json
    $p = Get-Process -Id $s.gamePid -ErrorAction SilentlyContinue
    if (-not $p) { throw "game (pid $($s.gamePid)) is not running" }
    return $p
}

function Focus-Game($p) {
    $h = $p.MainWindowHandle
    for ($i = 0; $i -lt 5; $i++) {
        if ([Drv]::GetForegroundWindow() -eq $h) { return $h }
        # a synthetic Alt tap lifts Windows' foreground lock for the calling process
        [Drv]::Key(0x12, $false); [Drv]::Key(0x12, $true)
        [void][Drv]::ShowWindow($h, 9)
        [void][Drv]::BringWindowToTop($h)
        [void][Drv]::SetForegroundWindow($h)
        Start-Sleep -Milliseconds 200
    }
    if ([Drv]::GetForegroundWindow() -ne $h) { throw 'could not bring the game window to the front' }
    return $h
}

switch ($Cmd) {
    'start' {
        $toml = Join-Path $Package 'AquaMai.toml'
        $bak = Join-Path $PSScriptRoot 'AquaMai.toml.drivebak'
        Copy-Item $toml $bak -Force
        . (Join-Path $PSScriptRoot 'test-lib.ps1')
        $t = [IO.File]::ReadAllText($toml)
        $t = Set-TomlSection $t 'GameSystem.Sound' @('EnableExclusive = false', 'Enable8Channel = false')
        $t = Set-TomlSection $t 'GameSystem.Window' @('Windowed = true', 'Borderless = false', 'Width = 540', 'Height = 960')
        # AntiLag taps a system-wide Ctrl every ~10 s. The off-screen test game is made to count as focused (so test keys
        # reach it), so even ActivateWhileBackground = false keeps typing Ctrl into whatever the user is using:
        # switch AntiLag off entirely for test runs (the user's own AquaMai.toml is restored on stop)
        $t = [regex]::Replace($t, '(?ms)^\#?\[Utils\.AntiLag\]\r?\n(?:(?!\r?\n\r?\n).)*', "#[Utils.AntiLag]`r`n#ActivateWhileBackground = false", 1)
        # test runs only: go straight from login to music select (skips photo/area/ticket/tutorial screens)
        # (DRIVE_FULL_ENTRY: keep every screen between login and music select, e.g. for skin checks)
        if (-not $env:DRIVE_FULL_ENTRY) { $t = Set-TomlSection $t 'Tweaks.TimeSaving.EntryToMusicSelection' @('EnableOnlyInGuestMode = false') }
        # test runs only: keep the stock timers. With DisableTimeout a normal-mode TRACK START waits forever for the
        # in-store matching countdown (the user plays freedom mode, which skips that wait)
        if (-not $env:DRIVE_KEEP_TIMEOUT) { $t = $t.Replace("`n[GameSystem.DisableTimeout]", "`n#[GameSystem.DisableTimeout]") }
        if ($env:DRIVE_NO_SERVER) { $t = $t.Replace("`n[GameSettings.ForceAsServer]", "`n#[GameSettings.ForceAsServer]") }
        [IO.File]::WriteAllText($toml, $t, (New-Object Text.UTF8Encoding($false)))
        $wasMuted = [EndpointMute]::GetMute()
        [EndpointMute]::SetMute($true)
        $env:OPENSSL_ia32cap = ':~0x20000000'
        $inj = Start-Process -FilePath (Join-Path $Package 'inject.exe') -WorkingDirectory $Package -WindowStyle Hidden -PassThru `
            -ArgumentList '-d', '-k', 'mai2hook.dll', 'amdaemon.exe', '-f', '-c', 'config_common.json', 'config_server.json', 'config_client.json'
        $game = Start-Process -FilePath (Join-Path $Package 'Sinmai.exe') -WorkingDirectory $Package -PassThru `
            -ArgumentList '-monitor', '1', '-screen-fullscreen', '0', '--melonloader.hideconsole'
        @{ gamePid = $game.Id; injectPid = $inj.Id; wasMuted = $wasMuted; tomlBak = $bak } | ConvertTo-Json | Set-Content $state
        Write-Host "started game pid $($game.Id), muted (was $wasMuted)"
    }
    'key' {
        $p = Get-Game
        [void](Focus-Game $p)
        $vk = [byte][System.Windows.Forms.Keys]$Arg1
        if ([Drv]::GetForegroundWindow() -ne $p.MainWindowHandle) { throw 'ABORTED: game is not the foreground window' }
        [Drv]::Key($vk, $false)
        $until = (Get-Date).AddMilliseconds($Arg2)
        while ((Get-Date) -lt $until) {
            if ([Drv]::GetForegroundWindow() -ne $p.MainWindowHandle) { [Drv]::Key($vk, $true); throw 'ABORTED: focus lost while holding key' }
            Start-Sleep -Milliseconds 50
        }
        [Drv]::Key($vk, $true)
        Write-Host "key $Arg1 held $Arg2 ms"
    }
    'keys' {
        # sequence like "D*19,C,Z*5,C" ; each tap 80 ms down, 120 ms gap
        $p = Get-Game
        [void](Focus-Game $p)
        foreach ($item in $Arg1.Split(',')) {
            $parts = $item.Split('*'); $n = if ($parts.Count -gt 1) { [int]$parts[1] } else { 1 }
            $vk = [byte][System.Windows.Forms.Keys]$parts[0]
            for ($i = 0; $i -lt $n; $i++) {
                if ([Drv]::GetForegroundWindow() -ne $p.MainWindowHandle) { throw "ABORTED: another window took focus, no more keys sent ($item #$i)" }
                [Drv]::Key($vk, $false); Start-Sleep -Milliseconds 120; [Drv]::Key($vk, $true); Start-Sleep -Milliseconds 650
            }
        }
        Write-Host "keys $Arg1"
    }
    'shot' {
        $p = Get-Game
        $h = Focus-Game $p
        Start-Sleep -Milliseconds 250
        $r = New-Object Drv+RECT; [void][Drv]::GetClientRect($h, [ref]$r)
        $pt = New-Object Drv+POINT; [void][Drv]::ClientToScreen($h, [ref]$pt)
        $w = $r.R - $r.L; $hh = $r.B - $r.T
        $bmp = New-Object Drawing.Bitmap -ArgumentList $w, $hh
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($pt.X, $pt.Y, 0, 0, (New-Object Drawing.Size -ArgumentList $w, $hh))
        $bmp.Save($Arg1); $g.Dispose(); $bmp.Dispose()
        Write-Host "shot $w x $hh -> $Arg1"
    }
    'stop' {
        . (Join-Path $PSScriptRoot 'test-lib.ps1')
        $s = if (Test-Path $state) { Get-Content $state -Raw | ConvertFrom-Json } else { $null }
        if ($s) {
            foreach ($id in @($s.gamePid, $s.injectPid)) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
        }
        Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'amdaemon.exe' -and $_.ExecutablePath -and $_.ExecutablePath.StartsWith($Package, [StringComparison]::OrdinalIgnoreCase) } |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        if ($s -and (Test-Path $s.tomlBak)) { Copy-Item $s.tomlBak (Join-Path $Package 'AquaMai.toml') -Force; Remove-Item $s.tomlBak }
        if ($s) { [EndpointMute]::SetMute([bool]$s.wasMuted) }
        $testDir = Join-Path $Package 'LocalAssets\PartnerLive2D\zz-test-default'
        if (Test-Path $testDir) { [IO.Directory]::Delete($testDir, $true) }
        if (Test-Path $state) { Remove-Item $state }
        Write-Host "stopped and restored"
    }
    default { throw "unknown command '$Cmd'" }
}
