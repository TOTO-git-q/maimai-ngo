# Silent automated test of the PartnerLive2D mod. ASCII only.
# - default audio output muted for the duration (restored at the end); game audio forced to shared
#   mode so the mute actually applies
# - game in a small window, AquaMai jumps straight into a song with autoplay -> result screen
# - waits for the mod's debug images, then stops the game by PID and restores every setting
param(
    [string]$Package = 'D:\maimai\Package',
    [int]$MusicId = 8,
    [int]$TimeoutSec = 240
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class EndpointMute {
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class Enumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice {
        [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr p);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr p);
        [PreserveSig] int GetChannelCount(out int c);
        [PreserveSig] int SetMasterVolumeLevel(float l, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float l, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float l);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float l);
        [PreserveSig] int SetChannelVolumeLevel(int ch, float l, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(int ch, float l, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(int ch, out float l);
        [PreserveSig] int GetChannelVolumeLevelScalar(int ch, out float l);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
    static IAudioEndpointVolume Get() {
        var en = (IMMDeviceEnumerator)new Enumerator();
        IMMDevice d; en.GetDefaultAudioEndpoint(0, 1, out d);
        var iid = typeof(IAudioEndpointVolume).GUID; object o;
        d.Activate(ref iid, 23, IntPtr.Zero, out o);
        return (IAudioEndpointVolume)o;
    }
    public static bool GetMute() { bool m; Get().GetMute(out m); return m; }
    public static void SetMute(bool m) { var g = Guid.Empty; Get().SetMute(m, ref g); }
}
'@

$toml = Join-Path $Package 'AquaMai.toml'
$tomlBak = Join-Path $env:TEMP ('AquaMai.toml.testbak_' + [guid]::NewGuid().ToString('N'))
$testIniDir = Join-Path $Package 'LocalAssets\PartnerLive2D\zz-test-default'
$wasMuted = [EndpointMute]::GetMute()
$procs = @()

function Set-TomlSection([string]$text, [string]$section, [string[]]$lines) {
    # replace the body of [section] (up to the next blank line before a '## ' or '[') with $lines
    $pattern = '(?ms)^\#?\[' + [regex]::Escape($section) + '\]\r?\n(?:(?!\r?\n\r?\n).)*'
    $body = '[' + $section + "]`r`n" + ($lines -join "`r`n")
    if ([regex]::IsMatch($text, $pattern)) { return [regex]::Replace($text, $pattern, $body, 1) }
    return $text + "`r`n" + $body + "`r`n"
}

try {
    Copy-Item $toml $tomlBak -Force
    $t = [IO.File]::ReadAllText($toml)
    $t = Set-TomlSection $t 'GameSystem.Sound' @('EnableExclusive = false', 'Enable8Channel = false')
    $t = Set-TomlSection $t 'GameSystem.Window' @('Windowed = true', 'Borderless = false', 'Width = 540', 'Height = 960')
    $t = Set-TomlSection $t 'Utils.AntiLag' @('ActivateWhileBackground = true', 'SetHighPriority = false')
    [IO.File]::WriteAllText($toml, $t, (New-Object Text.UTF8Encoding($false)))

    # the autoplay guest uses the default partner (navi chara 0): point a debug copy of the model at it
    New-Item -ItemType Directory -Force $testIniDir | Out-Null
    $src = Join-Path $Package 'LocalAssets\PartnerLive2D\hiyori\partner.ini'
    $ini = (Get-Content $src) -replace '^naviCharaId\s*=.*', 'naviCharaId = 0' -replace '^partnerId\s*=.*', 'partnerId = -1' -replace '^model\s*=.*', 'model = ../hiyori/model/Hiyori.model3.json'
    $ini += 'debugDump = true'
    $ini += 'debugOverlay = true'
    Set-Content -Path (Join-Path $testIniDir 'partner.ini') -Value $ini -Encoding ASCII
    Remove-Item (Join-Path $testIniDir 'debug_*.png') -ErrorAction SilentlyContinue

    [EndpointMute]::SetMute($true)
    Write-Host "audio muted (was muted: $wasMuted)"

    $env:OPENSSL_ia32cap = ':~0x20000000'
    $procs += Start-Process -FilePath (Join-Path $Package 'inject.exe') -WorkingDirectory $Package -WindowStyle Hidden -PassThru `
        -ArgumentList '-d', '-k', 'mai2hook.dll', 'amdaemon.exe', '-f', '-c', 'config_common.json', 'config_server.json', 'config_client.json'
    $game = Start-Process -FilePath (Join-Path $Package 'Sinmai.exe') -WorkingDirectory $Package -PassThru `
        -ArgumentList '-monitor', '1', '-screen-fullscreen', '0', '--melonloader.hideconsole'
    $procs += $game
    Write-Host "game pid $($game.Id)"

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $shot = Join-Path $testIniDir 'debug_screen_3.png'
    while ((Get-Date) -lt $deadline -and -not $game.HasExited) {
        if (Test-Path $shot) { Start-Sleep -Seconds 2; break }
        Start-Sleep -Seconds 2
    }
    if (Test-Path $shot) { Write-Host "RESULT: debug images captured in $testIniDir" }
    elseif ($game.HasExited) { Write-Host "RESULT: game exited early (code $($game.ExitCode))" }
    else { Write-Host "RESULT: timeout, no debug image" }
}
finally {
    foreach ($p in $procs) { if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } }
    Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'amdaemon.exe' -and $_.ExecutablePath -and $_.ExecutablePath.StartsWith($Package, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    if (Test-Path $tomlBak) { Copy-Item $tomlBak $toml -Force; Remove-Item $tomlBak }
    [EndpointMute]::SetMute($wasMuted)
    Write-Host "restored AquaMai.toml and mute=$wasMuted"
}
