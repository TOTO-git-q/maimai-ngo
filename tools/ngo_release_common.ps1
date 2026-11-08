# Shared safety/preflight helpers. ASCII source; Chinese is decoded at runtime.
Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

function Say([string]$English, [string]$ChineseBase64) {
    Write-Host $English
    Write-Host ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($ChineseBase64)))
}

function Safe-Path([string]$Root, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or
        $Relative.Contains(':') -or $Relative -match '(^|[\\/])\.\.?([\\/]|$)') { throw "Unsafe manifest path: $Relative" }
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $path = [IO.Path]::GetFullPath((Join-Path $base $Relative))
    if (-not $path.StartsWith($base + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes root: $Relative" }
    $cursor = $path
    while ($cursor.Length -ge $base.Length) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Reparse point in operation path: $cursor"
            }
        }
        $parent = Split-Path -Parent $cursor
        if ($parent -eq $cursor -or [string]::IsNullOrEmpty($parent)) { break }
        $cursor = $parent
    }
    return $path
}

function Check-Hash([string]$Path, [string]$Expected) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required file missing: $Path" }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Expected) { throw "SHA256 mismatch: $Path" }
}

function Initialize-Release([string]$Package, [string]$Here, [bool]$NeedPayload) {
    if (-not (Test-Path -LiteralPath (Join-Path $Package 'Sinmai_Data') -PathType Container)) {
        throw "Not a game Package folder: $Package"
    }
    $target = (Resolve-Path -LiteralPath $Package).Path.TrimEnd('\')
    $dist = (Resolve-Path -LiteralPath $Here).Path.TrimEnd('\')
    if ($dist -eq $target -or $dist.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $target.StartsWith($dist + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Keep the release folder outside the game Package folder.'
    }
    foreach ($p in @(Get-Process -Name Sinmai -ErrorAction SilentlyContinue)) {
        $processPath = $null
        try { $processPath = $p.Path } catch { }
        if (-not $processPath) { throw 'Cannot verify a running Sinmai process path. Close the game first.' }
        if ($processPath.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The game in this Package is running. Close it first.'
        }
    }
    $m = Get-Content -LiteralPath (Join-Path $Here 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($m.schema_version -ne 2) { throw 'Unsupported release manifest schema.' }
    $payload = @{}; $stock = @{}; $affected = @{}; $legacy = @{}
    foreach ($f in $m.stock_files) {
        if ($stock.ContainsKey($f.path)) { throw "Duplicate stock path: $($f.path)" }
        $stock[$f.path] = $f.sha256; $affected[$f.path] = $true
        $null = Safe-Path $target $f.path
        Check-Hash (Safe-Path (Join-Path $Here 'original') $f.path) $f.sha256
    }
    foreach ($f in $m.files) {
        if ($payload.ContainsKey($f.path)) { throw "Duplicate payload path: $($f.path)" }
        $payload[$f.path] = $f; $affected[$f.path] = $true
        $null = Safe-Path $target $f.path
        if ($f.stock_sha256) {
            if (-not $stock.ContainsKey($f.path) -or $stock[$f.path] -ne $f.stock_sha256) { throw 'Inconsistent stock manifest.' }
        } elseif ($stock.ContainsKey($f.path)) { throw 'Added path conflicts with stock manifest.' }
        if ($NeedPayload) { Check-Hash (Safe-Path (Join-Path $Here 'files') $f.path) $f.sha256 }
    }
    foreach ($v in $m.previous_versions) {
        foreach ($rel in $v.added_files) {
            if ($stock.ContainsKey($rel)) { throw "Legacy added path conflicts with stock: $rel" }
            $null = Safe-Path $target $rel
            $legacy[$rel] = $true; $affected[$rel] = $true
        }
    }
    foreach ($rel in $affected.Keys) {
        $path = Safe-Path $target $rel
        if ((Test-Path -LiteralPath $path) -and -not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Expected file: $path" }
    }
    if (-not $payload.ContainsKey($m.settings_path)) { throw 'Settings path missing from payload.' }
    return @{ Target = $target; Manifest = $m; Payload = $payload; Stock = $stock; Legacy = $legacy; Affected = $affected }
}

function Copy-ReleaseFile([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Merge-Settings([string]$Defaults, [string]$Existing) {
    $old = @{}
    foreach ($line in [IO.File]::ReadAllLines($Existing, [Text.Encoding]::UTF8)) {
        if ($line -match '^\s*([^#;=\s][^=]*?)\s*=\s*(.*?)\s*$') { $old[$matches[1].Trim()] = $matches[2] }
    }
    $seen = @{}; $lines = New-Object 'System.Collections.Generic.List[string]'
    foreach ($line in [IO.File]::ReadAllLines($Defaults, [Text.Encoding]::UTF8)) {
        if ($line -match '^\s*([^#;=\s][^=]*?)\s*=\s*(.*?)\s*$') {
            $key = $matches[1].Trim(); $value = $matches[2]; $seen[$key] = $true
            if ($old.ContainsKey($key) -and $old[$key] -cne $value) { $line = $key + ' = ' + $old[$key] }
        }
        $lines.Add($line)
    }
    $unknown = @($old.Keys | Where-Object { -not $seen.ContainsKey($_) } | Sort-Object)
    if ($unknown.Count -gt 0) {
        $lines.Add(''); $lines.Add('; Existing custom settings retained during upgrade.')
        foreach ($key in $unknown) { $lines.Add($key + ' = ' + $old[$key]) }
    }
    return ([string]::Join("`r`n", $lines) + "`r`n")
}

function New-Snapshot([string]$Here, [string]$Prefix) {
    $dir = Join-Path $Here ($Prefix + (Get-Date -Format 'yyyyMMdd_HHmmss_fff') + '_' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    return $dir
}

function Remove-EmptyParents([string]$Target, [string[]]$RelativePaths) {
    $dirs = @{}
    foreach ($rel in $RelativePaths) {
        $dir = Split-Path -Parent (Safe-Path $Target $rel)
        while ($dir.Length -gt $Target.Length) {
            $dirs[$dir] = $true; $dir = Split-Path -Parent $dir
        }
    }
    foreach ($dir in @($dirs.Keys | Sort-Object { $_.Length } -Descending)) {
        if ((Test-Path -LiteralPath $dir -PathType Container) -and @(Get-ChildItem -LiteralPath $dir -Force).Count -eq 0) {
            Remove-Item -LiteralPath $dir -Force
        }
    }
}
