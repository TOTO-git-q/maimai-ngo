# ASCII only. Samples every process's CPU use once a second (below-normal priority) into a CSV, to line up the
# game's [stall] log lines (wall clock) with whatever else was busy at that second.
# usage: powershell -File ngo_proc_sampler.ps1 <out.csv> [minutes=20] [-GamePid N]
# With -GamePid it runs until that process exits (launcher.ps1 starts a copy, proc-sampler.ps1 in the game root, this
# way for every real session: playlogs\procs_<game start>.csv, read by tools\ngo_play_report.py).
# Columns: time (HH:mm:ss.fff), total_cpu_pct (all cores), then the 6 busiest processes as name:pct.
param([string]$Out, [double]$Minutes = 20, [int]$GamePid = 0)
$ErrorActionPreference = 'SilentlyContinue'
[System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'
$game = $null
if ($GamePid -ne 0) { $game = Get-Process -Id $GamePid; if (-not $game) { exit 1 }; $Minutes = 24 * 60 }
New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
$cores = [Environment]::ProcessorCount
$prev = @{}
foreach ($p in [System.Diagnostics.Process]::GetProcesses()) { try { $prev[$p.Id] = $p.TotalProcessorTime.TotalMilliseconds } catch {} }
$last = [DateTime]::Now
'time,total_cpu_pct,top' | Set-Content -Path $Out -Encoding ascii
$end = $last.AddMinutes($Minutes)
while ([DateTime]::Now -lt $end) {
    Start-Sleep -Milliseconds 1000
    if ($game) { $game.Refresh(); if ($game.HasExited) { break } }
    $now = [DateTime]::Now
    $span = ($now - $last).TotalMilliseconds * $cores
    $rows = @(); $cur = @{}; $total = 0.0
    foreach ($p in [System.Diagnostics.Process]::GetProcesses()) {
        try {
            $ms = $p.TotalProcessorTime.TotalMilliseconds; $cur[$p.Id] = $ms
            if ($prev.ContainsKey($p.Id)) {
                $d = $ms - $prev[$p.Id]
                if ($d -gt 0) { $total += $d; $rows += New-Object psobject -Property @{ n = $p.ProcessName; d = $d } }
            }
        } catch {}
    }
    $top = ($rows | Sort-Object d -Descending | Select-Object -First 6 | ForEach-Object { '{0}:{1:0.0}' -f $_.n, (100.0 * $_.d / $span) }) -join ' '
    ('{0},{1:0.0},{2}' -f $now.ToString('HH:mm:ss.fff'), (100.0 * $total / $span), $top) | Add-Content -Path $Out -Encoding ascii
    $prev = $cur; $last = $now
}
