# maimai one-click launcher (local play).
# ASCII only on purpose: Windows PowerShell 5.1 reads non-BOM scripts as ANSI/GBK.
#
# What it does, in order:
#   1. per-user Windows prefs for Sinmai.exe: use the NVIDIA GPU, no fullscreen optimizations
#   2. switch the power-mode slider to "Best performance" (restored on exit)
#   3. start amdaemon (through segatools inject) and Sinmai.exe
#   4. Sinmai.exe -> High priority, pinned to P-cores, power throttling (EcoQoS) off
#   5. when the game exits: stop this folder's amdaemon/inject by PID, restore power mode
#
# Usage: Play-maimai.bat            (monitor auto: 2nd screen if one is connected)
#        Play-maimai.bat -Monitor 1 (force a Unity display index)

param(
    [string]$Monitor = 'auto'
)

$ErrorActionPreference = 'Stop'
$Root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$Pkg    = Join-Path $Root 'Package'
$Sinmai = Join-Path $Pkg 'Sinmai.exe'
$LogFile = Join-Path $Root 'launcher.log'

function Log([string]$msg) {
    $line = '{0} {1}' -f (Get-Date -Format 'HH:mm:ss'), $msg
    Add-Content -Path $LogFile -Value $line -Encoding UTF8
}

function Fail([string]$msg) {
    Log "ERROR: $msg"
    Add-Type -AssemblyName System.Windows.Forms
    [void][System.Windows.Forms.MessageBox]::Show($msg, 'maimai launcher', 'OK', 'Error')
    exit 1
}

Set-Content -Path $LogFile -Value ('launcher start ' + (Get-Date -Format 's')) -Encoding UTF8

# segatools redirects E:\ and Y:\, and the game chokes on non-ASCII paths.
if ($Root -match '^[EeYy]:') { Fail "The game folder must not be on drive E: or Y:. Move it (e.g. D:\maimai)." }
if ($Root -match '[^\x20-\x7e]') { Fail "The game folder path must be plain English letters/digits: $Root" }
if (-not (Test-Path $Sinmai)) { Fail "Sinmai.exe not found under $Pkg" }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MmNative {
    [DllImport("powrprof.dll")] public static extern uint PowerGetActualOverlayScheme(out Guid g);
    [DllImport("powrprof.dll")] public static extern uint PowerSetActiveOverlayScheme(ref Guid g);

    [StructLayout(LayoutKind.Sequential)]
    struct PowerThrottlingState { public uint Version; public uint ControlMask; public uint StateMask; }
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetProcessInformation(IntPtr h, int cls, ref PowerThrottlingState s, int size);
    // ProcessPowerThrottling = 4, EXECUTION_SPEED = 1; ControlMask set + StateMask clear = never throttle
    public static bool DisableThrottling(IntPtr h) {
        var s = new PowerThrottlingState { Version = 1, ControlMask = 1, StateMask = 0 };
        return SetProcessInformation(h, 4, ref s, Marshal.SizeOf(typeof(PowerThrottlingState)));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetLogicalProcessorInformationEx(int rel, IntPtr buf, ref uint len);
    // Mask of the cores with the highest EfficiencyClass (= P-cores on hybrid CPUs).
    public static long PerformanceCoreMask() {
        uint len = 0;
        GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref len);
        IntPtr buf = Marshal.AllocHGlobal((int)len);
        try {
            if (!GetLogicalProcessorInformationEx(0, buf, ref len)) return 0;
            long[] maskByClass = new long[256];
            int top = 0, off = 0;
            while (off < len) {
                IntPtr p = new IntPtr(buf.ToInt64() + off);
                int size = Marshal.ReadInt32(p, 4);
                int eff = Marshal.ReadByte(p, 9);
                long mask = Marshal.ReadInt64(p, 32); // GROUP_AFFINITY.Mask, group 0
                maskByClass[eff] |= mask;
                if (eff > top) top = eff;
                off += size;
            }
            return maskByClass[top];
        } finally { Marshal.FreeHGlobal(buf); }
    }
}
'@

# --- 1. per-user app prefs (idempotent) --------------------------------------
$gpuKey = 'HKCU:\Software\Microsoft\DirectX\UserGpuPreferences'
if (-not (Test-Path $gpuKey)) { New-Item -Path $gpuKey -Force | Out-Null }
Set-ItemProperty -Path $gpuKey -Name $Sinmai -Value 'GpuPreference=2;'
$layersKey = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers'
if (-not (Test-Path $layersKey)) { New-Item -Path $layersKey -Force | Out-Null }
Set-ItemProperty -Path $layersKey -Name $Sinmai -Value '~ DISABLEDXMAXIMIZEDWINDOWEDMODE'
Log 'GPU preference = high performance; fullscreen optimizations off'

# --- 2. power mode --------------------------------------------------------------
$bestPerf = [Guid]'ded574b5-45a0-4f42-8737-46345c09c238'
$savedOverlay = [Guid]::Empty
$overlayChanged = $false
$gpuLocked = $false
try {
    if ([MmNative]::PowerGetActualOverlayScheme([ref]$savedOverlay) -eq 0 -and
        [MmNative]::PowerSetActiveOverlayScheme([ref]$bestPerf) -eq 0) {
        $overlayChanged = $true
        Log "power mode -> best performance (was $savedOverlay)"
    }
} catch { Log "power mode not changed: $($_.Exception.Message)" }

# --- audio check (log only) -----------------------------------------------------
# AquaMai opens the DEFAULT output in WASAPI exclusive mode. Plugging in an HDMI
# screen or a Bluetooth headset can silently change the default, so record it.
try {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MmAudio {
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class Enumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice {
        [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, out IntPtr iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
    }
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore {
        [PreserveSig] int GetCount(out int c);
        [PreserveSig] int GetAt(int i, out PropertyKey k);
        [PreserveSig] int GetValue(ref PropertyKey k, out PropVariant v);
    }
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid fmtid; public int pid; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVariant {
        [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p;
    }
    public static string DefaultOutputName() {
        var en = (IMMDeviceEnumerator)new Enumerator();
        IMMDevice d; if (en.GetDefaultAudioEndpoint(0, 1, out d) != 0) return null;
        IPropertyStore s; if (d.OpenPropertyStore(0, out s) != 0) return null;
        var k = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
        PropVariant v; if (s.GetValue(ref k, out v) != 0 || v.vt != 31) return null;
        return Marshal.PtrToStringUni(v.p);
    }
}
'@
    $out = [MmAudio]::DefaultOutputName()
    Log "default audio output: $out"
    if ($out -match 'Bluetooth|Hands-Free|NVIDIA|HDMI|Steam|Virtual') {
        Log 'WARNING: default output is Bluetooth/HDMI/virtual; expect extra or unstable audio latency'
    }
} catch { Log "audio check skipped: $($_.Exception.Message)" }

# Processes of THIS install only, matched by full path (never by image name alone).
function Get-OurProcesses([string[]]$names) {
    Get-CimInstance Win32_Process | Where-Object {
        $names -contains $_.Name -and $_.ExecutablePath -and
        $_.ExecutablePath.StartsWith($Pkg, [StringComparison]::OrdinalIgnoreCase)
    }
}
function Stop-OurProcesses([string[]]$names) {
    foreach ($p in Get-OurProcesses $names) {
        Log "stopping $($p.Name) pid $($p.ProcessId)"
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

try {
    # leftovers from a crashed session
    Stop-OurProcesses @('Sinmai.exe', 'amdaemon.exe', 'inject.exe')

    # mods staged while the game was running (the loaded DLL is locked) are swapped in before start
    $pending = Join-Path $PSScriptRoot 'pending-mods'
    if (Test-Path $pending) {
        Get-ChildItem $pending -Filter '*.dll' | ForEach-Object {
            Move-Item $_.FullName (Join-Path $Pkg ('Mods\' + $_.Name)) -Force
            Log "installed staged mod $($_.Name)"
        }
    }

    # --- 3. start ---------------------------------------------------------------
    $env:OPENSSL_ia32cap = ':~0x20000000'   # same as the original start.bat
    Start-Process -FilePath (Join-Path $Pkg 'inject.exe') -WorkingDirectory $Pkg -WindowStyle Hidden `
        -ArgumentList '-d', '-k', 'mai2hook.dll', 'amdaemon.exe', '-f', '-c',
                      'config_common.json', 'config_server.json', 'config_client.json' | Out-Null
    Log 'amdaemon started via inject'

    if ($Monitor -eq 'auto') {
        Add-Type -AssemblyName System.Windows.Forms
        $Monitor = if ([System.Windows.Forms.Screen]::AllScreens.Count -ge 2) { '2' } else { '1' }
    }
    $gameArgs = @('-monitor', $Monitor, '--melonloader.hideconsole', '--melonloader.disablestartscreen')
    if ($Monitor -ne '1') {
        # controller screen (portrait TV): borderless window at its native pixel size; otherwise Unity
        # reuses a remembered resolution and shows a cropped, title-barred window
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type @'
using System.Runtime.InteropServices;
public static class LaunchMode {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
    public static int[] Size(string dev) {
        var dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        return EnumDisplaySettings(dev, -1, ref dm) ? new[] { dm.dmPelsWidth, dm.dmPelsHeight } : null;
    }
}
'@
        $tv = [System.Windows.Forms.Screen]::AllScreens | Where-Object { -not $_.Primary } | Select-Object -First 1
        $px = if ($tv) { [LaunchMode]::Size($tv.DeviceName) } else { $null }
        if ($px) {
            $gameArgs += @('-popupwindow', '-screen-width', "$($px[0])", '-screen-height', "$($px[1])")
            Log "controller screen $($tv.DeviceName) $($px[0])x$($px[1])"
        }
    }
    $game = Start-Process -FilePath $Sinmai -WorkingDirectory $Pkg -PassThru -ArgumentList $gameArgs
    Log "Sinmai started pid $($game.Id) on monitor $Monitor"
    # lock the NVIDIA GPU clock (1800-3090 MHz) while the game runs, so it never idles down between bursts; the
    # elevated scheduled tasks come from gpu-lock\setup-gpu-lock.bat (run once as administrator)
    $gpuLocked = $false
    & schtasks.exe /Query /TN 'maimai GPU lock' 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        & schtasks.exe /Run /TN 'maimai GPU lock' | Out-Null
        $gpuLocked = $true
        Log 'GPU clock locked 1800-3090 MHz'
    } else { Log 'GPU clock lock not set up (gpu-lock\setup-gpu-lock.bat)' }
    # memory / CPU / GPU samples for this run (playlogs\monitor_*.csv), read by tools\ngo_play_report.py
    if (Test-Path (Join-Path $PSScriptRoot 'play-monitor.ps1')) {
        Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass',
            '-File', (Join-Path $PSScriptRoot 'play-monitor.ps1'), '-GamePid', $game.Id | Out-Null
    }
    # (off 2026-11-01: login by the game itself, Enter = card scan) log in with the card in DEVICE\aime.txt once the attract screen is up (see auto-login.py)
    # $py = (Get-Command python.exe -ErrorAction SilentlyContinue).Source
    # if ($py) {
    # Start-Process -FilePath $py -WindowStyle Hidden -ArgumentList "`"$(Join-Path $PSScriptRoot 'auto-login.py')`"", $game.Id | Out-Null
    # Log 'auto-login started'
    # }
    if ($Monitor -ne '1') {
        # Unity ignores the controller screen's display scaling and opens at half size; fit it to the real pixels
        Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass',
            '-File', (Join-Path $PSScriptRoot 'fit-window.ps1'), '-GamePid', $game.Id | Out-Null
    }

    # --- 4. lock scheduling -----------------------------------------------------
    $game.PriorityClass = 'High'
    $pMask = [MmNative]::PerformanceCoreMask()
    if ($pMask -ne 0) {
        $game.ProcessorAffinity = [IntPtr]$pMask
        Log ('Sinmai pinned to P-cores, mask 0x{0:X}' -f $pMask)
    }
    if ([MmNative]::DisableThrottling($game.Handle)) { Log 'Sinmai power throttling off' }

    # amdaemon is spawned by inject a moment later
    for ($i = 0; $i -lt 20; $i++) {
        $d = Get-OurProcesses @('amdaemon.exe') | Select-Object -First 1
        if ($d) {
            $dp = Get-Process -Id $d.ProcessId
            $dp.PriorityClass = 'AboveNormal'
            [void][MmNative]::DisableThrottling($dp.Handle)
            Log "amdaemon pid $($d.ProcessId) -> AboveNormal, throttling off"
            break
        }
        Start-Sleep -Milliseconds 500
    }

    $game.WaitForExit()
    Log "Sinmai exited with code $($game.ExitCode)"
}
catch {
    Log "ERROR: $($_.Exception.Message)"
}
finally {
    # --- 5. cleanup ---------------------------------------------------------------
    Stop-OurProcesses @('amdaemon.exe', 'inject.exe')
    if ($gpuLocked) { & schtasks.exe /Run /TN 'maimai GPU unlock' | Out-Null; Log 'GPU clock unlocked' }
    if ($overlayChanged) {
        [void][MmNative]::PowerSetActiveOverlayScheme([ref]$savedOverlay)
        Log "power mode restored ($savedOverlay)"
    }
}
