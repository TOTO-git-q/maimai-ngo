"""Pack the stutter / memory-leak patch for an installed maimai x NEEDY GIRL OVERDOSE skin: the fixed PartnerLive2D.dll
with install / uninstall scripts (the replaced DLL is kept next to it for uninstall), the optional GPU clock lock helpers
and the notes. Nothing per-machine or private goes in (no segatools.ini, aime.txt, AquaMai.toml, settings, logs).
usage: python ngo_pack_patch.py <out dir> <notes .txt (UTF-8)>"""
import hashlib, json, os, shutil, sys, time, zipfile

PKG = r"D:\maimai\Package"
OUT, NOTES = sys.argv[1], sys.argv[2]
NAME = "maimai_NGO_patch_" + time.strftime("%Y%m%d")
ROOT = os.path.join(OUT, NAME)
FILES = [r"Mods\PartnerLive2D.dll"]
HERE = os.path.dirname(os.path.abspath(__file__))
EXTRAS = {r"extras\gpu-lock\setup-gpu-lock.bat": r"D:\maimai\gpu-lock\setup-gpu-lock.bat",
          r"extras\gpu-lock\remove-gpu-lock.bat": r"D:\maimai\gpu-lock\remove-gpu-lock.bat",
          r"extras\gpu-lock\gpu-lock-on.bat": os.path.join(HERE, "patch_extras", "gpu-lock-on.bat"),
          r"extras\gpu-lock\gpu-lock-off.bat": os.path.join(HERE, "patch_extras", "gpu-lock-off.bat")}


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest()


if os.path.exists(ROOT): shutil.rmtree(ROOT)
os.makedirs(ROOT)
manifest = []
for rel in FILES:
    dst = os.path.join(ROOT, "files", rel); os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copy2(os.path.join(PKG, rel), dst)
    manifest.append({"path": rel, "sha256": sha(dst)})
for rel, src in EXTRAS.items():
    dst = os.path.join(ROOT, rel); os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copy2(src, dst)
json.dump({"name": NAME, "game": "SDEZ 1.70 (maimai DX)", "files": manifest}, open(os.path.join(ROOT, "manifest.json"), "w", encoding="utf-8"), indent=1)

INSTALL = r'''# Install the NGO skin stutter / memory-leak patch. ASCII only. Usage: install.bat [<Package folder>]
param([string]$Package = 'D:\maimai\Package')
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$m = Get-Content -Raw -Path (Join-Path $here 'manifest.json') | ConvertFrom-Json
if (-not (Test-Path (Join-Path $Package 'Sinmai.exe'))) { Write-Host "Sinmai.exe not found in $Package"; exit 1 }
$pkgFull = (Resolve-Path $Package).Path.TrimEnd('\')
foreach ($proc in @(Get-Process -Name Sinmai -ErrorAction SilentlyContinue)) {
    $exe = $null; try { $exe = $proc.Path } catch {}
    if ($exe -and ((Split-Path -Parent $exe).TrimEnd('\') -ieq $pkgFull)) { Write-Host 'Close the game first.'; exit 1 }
}
foreach ($f in $m.files) {
    $dst = Join-Path $Package $f.path
    $bak = $dst + '.before-' + $m.name
    if ((Test-Path $dst) -and -not (Test-Path $bak)) { Copy-Item -LiteralPath $dst -Destination $bak }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null
    Copy-Item -LiteralPath (Join-Path (Join-Path $here 'files') $f.path) -Destination $dst -Force
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $dst).Hash.ToLower() -ne $f.sha256) { Write-Host "copy check failed: $($f.path)"; exit 1 }
    Write-Host "installed $($f.path)"
}
Write-Host 'Done.'
'''
UNINSTALL = r'''# Undo the NGO skin stutter / memory-leak patch (puts back the files it replaced). ASCII only.
param([string]$Package = 'D:\maimai\Package')
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$m = Get-Content -Raw -Path (Join-Path $here 'manifest.json') | ConvertFrom-Json
$pkgFull = (Resolve-Path $Package).Path.TrimEnd('\')
foreach ($proc in @(Get-Process -Name Sinmai -ErrorAction SilentlyContinue)) {
    $exe = $null; try { $exe = $proc.Path } catch {}
    if ($exe -and ((Split-Path -Parent $exe).TrimEnd('\') -ieq $pkgFull)) { Write-Host 'Close the game first.'; exit 1 }
}
foreach ($f in $m.files) {
    $dst = Join-Path $Package $f.path
    $bak = $dst + '.before-' + $m.name
    if (Test-Path $bak) { Move-Item -LiteralPath $bak -Destination $dst -Force; Write-Host "restored $($f.path)" }
    else { Write-Host "no backup for $($f.path), left as is" }
}
Write-Host 'Done.'
'''
BAT = '@echo off\r\npowershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0{0}.ps1" %*\r\npause\r\n'
for name, body in (("install", INSTALL), ("uninstall", UNINSTALL)):
    open(os.path.join(ROOT, name + ".ps1"), "w", encoding="ascii", newline="\r\n").write(body)
    open(os.path.join(ROOT, name + ".bat"), "w", encoding="ascii", newline="").write(BAT.format(name))
notes = open(NOTES, encoding="utf-8").read()
open(os.path.join(ROOT, "说明.txt"), "w", encoding="utf-8-sig", newline="\r\n").write(notes)

zp = ROOT + ".zip"
if os.path.exists(zp): os.remove(zp)
with zipfile.ZipFile(zp, "w", zipfile.ZIP_DEFLATED) as z:
    for dp, dn, fn in os.walk(ROOT):
        for f in fn:
            p = os.path.join(dp, f); z.write(p, os.path.join(NAME, os.path.relpath(p, ROOT)))
print(zp, os.path.getsize(zp) // 1024, "KB")
