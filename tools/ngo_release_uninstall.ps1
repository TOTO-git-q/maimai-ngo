# Restore verified stock, regardless of which historical release came first.
param([string]$Package = 'D:\maimai\Package')
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here 'release_common.ps1')
try {
    $ctx = Initialize-Release $Package $here $false
    $settings = Safe-Path $ctx.Target $ctx.Manifest.settings_path
    if (Test-Path -LiteralPath $settings -PathType Leaf) {
        $backup = New-Snapshot $here 'backup_before_uninstall_'
        Copy-ReleaseFile $settings (Safe-Path $backup $ctx.Manifest.settings_path)
    }
    $restored = 0; $removed = 0
    foreach ($rel in @($ctx.Affected.Keys | Sort-Object)) {
        $dst = Safe-Path $ctx.Target $rel
        if ($ctx.Stock.ContainsKey($rel)) {
            Copy-ReleaseFile (Safe-Path (Join-Path $here 'original') $rel) $dst
            $restored++
        } elseif (Test-Path -LiteralPath $dst -PathType Leaf) {
            Remove-Item -LiteralPath $dst -Force
            $removed++
        }
    }
    Remove-EmptyParents $ctx.Target @($ctx.Affected.Keys)
    Say ("Restored {0} stock files; removed {1} added files. Existing settings backed up beside this release." -f $restored, $removed) '5oGi5aSN5Y6f54mI5a6M5oiQ77yb5bey56e76Zmk5paw5aKe5paH5Lu277yM5Y246L295YmN6YWN572u5bey5aSH5Lu944CC'
} catch {
    Say ("Uninstall refused/failed: " + $_) '5Y246L295bey5YGc5q2i77yM6K+35qOA5p+l5LiK5paH6ZSZ6K+v44CC'
    exit 1
}
