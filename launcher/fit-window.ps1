# Fit the game window to the controller screen (non-primary display) at its real pixel size. ASCII only.
# Started by launcher.ps1 in its own process so per-monitor DPI awareness is set before any screen query.
#   fit-window.ps1 -GamePid <pid>
param([int]$GamePid)
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Fit {
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
    [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@
[void][Fit]::SetProcessDpiAwarenessContext([IntPtr]-4)   # per-monitor v2
Add-Type -AssemblyName System.Windows.Forms
$screen = [System.Windows.Forms.Screen]::AllScreens | Where-Object { -not $_.Primary } | Select-Object -First 1
if (-not $screen) { exit }
$b = $screen.Bounds
$focused = $false
# For the whole session: the game resizes its window while loading, and the taskbar on that screen (RetroBar)
# raises itself; keep the window at the screen's pixels and at the top of the topmost band without taking focus.
while ($true) {
    $p = Get-Process -Id $GamePid -ErrorAction SilentlyContinue
    if (-not $p) { exit }
    $h = $p.MainWindowHandle
    if ($h -ne [IntPtr]::Zero) {
        $r = New-Object Fit+R
        [void][Fit]::GetWindowRect($h, [ref]$r)
        if ($r.L -ne $b.X -or $r.T -ne $b.Y -or ($r.Rt - $r.L) -ne $b.Width -or ($r.B - $r.T) -ne $b.Height) {
            # only when it is off: a resize makes the game rebuild its swap chain, which shows as a hitch
            # HWND_TOPMOST, SWP_SHOWWINDOW | SWP_NOACTIVATE
            [void][Fit]::SetWindowPos($h, [IntPtr](-1), $b.X, $b.Y, $b.Width, $b.Height, 0x0040 -bor 0x0010)
        } else {
            # z-order only (SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE): keeps the taskbar under the game
            [void][Fit]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0002 -bor 0x0001 -bor 0x0010)
        }
        if (-not $focused) { [void][Fit]::SetForegroundWindow($h); $focused = $true }
    }
    Start-Sleep -Seconds 2
}
