# ASCII only on purpose (Windows PowerShell 5.1 reads non-BOM scripts as ANSI/GBK).
# Samples the running game every few seconds into playlogs\monitor_<game start>.csv until it exits: process memory,
# CPU load and the NVIDIA GPU state. Read together with the MelonLoader log by tools\ngo_play_report.py.
# Started hidden by launcher.ps1; can also be started by hand while the game runs: play-monitor.ps1 [-GamePid N]
param([int]$GamePid = 0, [int]$Every = 5)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'Package\Sinmai.exe'
if ($GamePid -eq 0) {
    $found = Get-CimInstance Win32_Process -Filter "Name='Sinmai.exe'" | Where-Object { $_.ExecutablePath -eq $exe } | Select-Object -First 1
    if (-not $found) { exit 1 }
    $GamePid = $found.ProcessId
}
$p = Get-Process -Id $GamePid
$dir = Join-Path $PSScriptRoot 'playlogs'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$out = Join-Path $dir ('monitor_' + $p.StartTime.ToString('yyyyMMdd_HHmmss') + '.csv')
if (Test-Path $out) { exit 0 }   # one sampler per game run
'time,private_mb,working_set_mb,cpu_pct,gpu_mhz,gpu_util_pct,gpu_temp_c,gpu_pstate,gpu_slowdown' | Set-Content -Path $out -Encoding ascii
$smi = 'C:\Windows\System32\nvidia-smi.exe'
$hasSmi = Test-Path $smi
$cores = [Environment]::ProcessorCount
$lastCpu = $p.TotalProcessorTime
$lastAt = Get-Date
while ($true) {
    Start-Sleep -Seconds $Every
    $p.Refresh()
    if ($p.HasExited) { break }
    $now = Get-Date
    $cpu = $p.TotalProcessorTime
    $pct = 100.0 * ($cpu - $lastCpu).TotalSeconds / [Math]::Max(0.001, ($now - $lastAt).TotalSeconds) / $cores
    $lastCpu = $cpu; $lastAt = $now
    $gpu = ',,,,'
    if ($hasSmi) {
        $g = & $smi '--query-gpu=clocks.gr,utilization.gpu,temperature.gpu,pstate,clocks_event_reasons.active' '--format=csv,noheader,nounits' 2>$null | Select-Object -First 1
        if ($g) { $gpu = ($g -split ',\s*') -join ',' }
    }
    ('{0},{1},{2},{3:0.0},{4}' -f $now.ToString('HH:mm:ss'), [int]($p.PrivateMemorySize64 / 1MB), [int]($p.WorkingSet64 / 1MB), $pct, $gpu) |
        Add-Content -Path $out -Encoding ascii
}
