# Install helper. Do not run until the exported package has been reviewed.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'stage\ngo_pair_sprites'
$target = 'D:\maimai\Package\LocalAssets\PartnerLive2D\ngo_pair\sprites'
$ini = 'D:\maimai\Package\LocalAssets\PartnerLive2D\ngo_pair\partner.ini'
$manifest = Join-Path $source 'manifest.json'
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { throw 'Export manifest not found.' }
if (-not (Test-Path -LiteralPath $ini -PathType Leaf)) { throw 'partner.ini not found.' }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fffffff'
$backup = "$ini.$stamp.bak"
Copy-Item -LiteralPath $ini -Destination $backup
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force

$lines = [System.Collections.Generic.List[string]]::new()
$found = $false
foreach ($line in [System.IO.File]::ReadAllLines($ini)) {
    if ($line -match '^\s*sprites\s*=') {
        if (-not $found) { $lines.Add('sprites = sprites/manifest.json'); $found = $true }
    } else { $lines.Add($line) }
}
if (-not $found) { $lines.Add('sprites = sprites/manifest.json') }
[System.IO.File]::WriteAllLines($ini, $lines.ToArray())
Write-Host "Installed sprite packs. partner.ini backup: $backup"
