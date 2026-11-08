# Direct full install/upgrade. ASCII only. Usage: install.bat [Package folder]
param([string]$Package = 'D:\maimai\Package')
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here 'release_common.ps1')
try {
    $ctx = Initialize-Release $Package $here $true
    $target = $ctx.Target
    $settings = Safe-Path $target $ctx.Manifest.settings_path
    $merged = $null
    if (Test-Path -LiteralPath $settings -PathType Leaf) {
        $merged = Merge-Settings (Safe-Path (Join-Path $here 'files') $ctx.Manifest.settings_path) $settings
    }
    $backup = New-Snapshot $here 'backup_before_install_'
    $existed = @{}; $removed = 0; $restored = 0
    # Finish all backups before the first game write. Snapshots are never stock.
    foreach ($rel in @($ctx.Affected.Keys | Sort-Object)) {
        $dst = Safe-Path $target $rel
        $existed[$rel] = Test-Path -LiteralPath $dst -PathType Leaf
        if ($existed[$rel]) { Copy-ReleaseFile $dst (Safe-Path $backup $rel) }
    }
    @{ package = $target; release = $ctx.Manifest.name; existed = $existed; purpose = 'Pre-install snapshot; stock is original/' } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $backup 'snapshot.json') -Encoding UTF8
    try {
        foreach ($rel in @($ctx.Affected.Keys | Sort-Object)) {
            $dst = Safe-Path $target $rel
            if ($ctx.Payload.ContainsKey($rel)) {
                Copy-ReleaseFile (Safe-Path (Join-Path $here 'files') $rel) $dst
                if ($rel -eq $ctx.Manifest.settings_path -and $null -ne $merged) {
                    [IO.File]::WriteAllText($dst, $merged, (New-Object Text.UTF8Encoding($false)))
                }
            } elseif ($ctx.Stock.ContainsKey($rel)) {
                Copy-ReleaseFile (Safe-Path (Join-Path $here 'original') $rel) $dst
                $restored++
            } elseif (Test-Path -LiteralPath $dst -PathType Leaf) {
                Remove-Item -LiteralPath $dst -Force
                $removed++
            }
        }
    } catch {
        $failure = $_
        foreach ($rel in $existed.Keys) {
            $dst = Safe-Path $target $rel
            if ($existed[$rel]) { Copy-ReleaseFile (Safe-Path $backup $rel) $dst }
            elseif (Test-Path -LiteralPath $dst -PathType Leaf) { Remove-Item -LiteralPath $dst -Force }
        }
        throw "Install failed; pre-install files restored from $backup. $failure"
    }
    Remove-EmptyParents $target @($ctx.Affected.Keys)
    Say ("Installed {0} files; removed {1} obsolete files; restored {2} retired stock files. Settings retained. Backup: {3}" -f $ctx.Payload.Count, $removed, $restored, $backup) '5a6J6KOF5a6M5oiQ77yb5bey5L+d55WZ546p5a626YWN572u77yM5bm25aSH5Lu95riF55CG5pen5paH5Lu244CC'
} catch {
    Say ("Install refused/failed: " + $_) '5a6J6KOF5bey5YGc5q2i77yM6K+35qOA5p+l5LiK5paH6ZSZ6K+v44CC'
    exit 1
}
