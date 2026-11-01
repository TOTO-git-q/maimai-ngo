"""Pack the whole maimai x NEEDY GIRL OVERDOSE re-skin (textures patched into the game data + the PartnerLive2D mod and
its LocalAssets) into one installable folder and zip, with the stock files for uninstall taken from the original
SDEZ 1.70 install archive.
- files/<Package path>: the files as installed now;
- original/<Package path>: the stock file for every file that existed in the stock game (from the archive);
- manifest.json: path, sha256 now, sha256 stock (null = a file the skin adds);
- install.bat/.ps1, uninstall.bat/.ps1 (ASCII only: Windows reads .ps1/.bat in the ANSI code page), 说明.txt.
Never packed: per-machine and private files (segatools.ini with the keychip, DEVICE/aime.txt with the card,
AquaMai.toml, config_*.json, appdata, amfs, logs, photos, caches) and test leftovers.
usage: python ngo_pack_release.py <out dir>"""
import hashlib, json, os, re, shutil, sys, time, zipfile

PKG = r"D:\maimai\Package"
ZIP = r"D:\maimoller内容\Maimoller专用整合\MaiMai_Magical懒人包\SDEZ1.70_nobga.zip"
OUT = sys.argv[1]
NAME = "maimai_NGO_skin_" + time.strftime("%Y%m%d")
ROOT = os.path.join(OUT, NAME)
SINCE = time.mktime((2026, 10, 19, 0, 0, 0, 0, 0, -1))   # the game was installed 2026-10-19; stock files are older

# what belongs to the skin
INCLUDE_TREES = [r"LocalAssets\PartnerLive2D"]
INCLUDE_FILES = [r"Sinmai_Data\resources.assets", r"Mods\PartnerLive2D.dll"]
INCLUDE_CHANGED_UNDER = [r"Sinmai_Data\StreamingAssets"]
SKIP = [r"LocalAssets\PartnerLive2D\theme_ngo_test",            # unused test theme (settings: #theme)
        r"LocalAssets\PartnerLive2D\settings.ini.before-ngo-skin",
        r"LocalAssets\PartnerLive2D\ngo_tint.txt.off",
        r"LocalAssets\PartnerLive2D\account_edit.applied.ini"]   # this account's one-shot edit, already applied


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest()


def skipped(rel): return any(rel == s or rel.startswith(s + "\\") for s in SKIP) or rel.lower().endswith((".log", ".tmp"))


rels = []
for t in INCLUDE_TREES:
    for dp, dn, fn in os.walk(os.path.join(PKG, t)):
        for f in fn:
            rel = os.path.relpath(os.path.join(dp, f), PKG)
            if not skipped(rel): rels.append(rel)
rels += INCLUDE_FILES
for t in INCLUDE_CHANGED_UNDER:
    for dp, dn, fn in os.walk(os.path.join(PKG, t)):
        for f in fn:
            p = os.path.join(dp, f)
            if os.path.getmtime(p) > SINCE and not f.endswith((".orig", ".new")) and ".before-" not in f:
                rels.append(os.path.relpath(p, PKG))
rels = sorted(set(rels))

if os.path.exists(ROOT): shutil.rmtree(ROOT)
os.makedirs(ROOT)
z = zipfile.ZipFile(ZIP)
stock_names = set(z.namelist())
manifest = []
for rel in rels:
    src = os.path.join(PKG, rel)
    dst = os.path.join(ROOT, "files", rel); os.makedirs(os.path.dirname(dst), exist_ok=True)
    if rel == r"LocalAssets\PartnerLive2D\settings.ini":     # drop the test-only per-frame timing log
        txt = open(src, encoding="utf-8").read()
        open(dst, "w", encoding="utf-8").write(re.sub(r"(?m)^frameLog\s*=.*\n?", "", txt))
    else:
        shutil.copy2(src, dst)
    zn = "Package/" + rel.replace("\\", "/")
    stock = None
    if zn in stock_names:
        o = os.path.join(ROOT, "original", rel); os.makedirs(os.path.dirname(o), exist_ok=True)
        with z.open(zn) as fi, open(o, "wb") as fo: shutil.copyfileobj(fi, fo, 1 << 20)
        stock = sha(o)
    manifest.append({"path": rel, "sha256": sha(dst), "stock_sha256": stock})
json.dump({"name": NAME, "game": "SDEZ 1.70 (maimai DX)", "files": manifest}, open(os.path.join(ROOT, "manifest.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

INSTALL = r'''# Install the maimai x NEEDY GIRL OVERDOSE skin. ASCII only. Usage: install.bat [<Package folder>]
param([string]$Package = 'D:\maimai\Package')
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not (Test-Path (Join-Path $Package 'Sinmai_Data'))) { throw "Not a game Package folder: $Package" }
$full = (Resolve-Path $Package).Path.TrimEnd('\') + '\'
if (Get-Process -Name Sinmai -ErrorAction SilentlyContinue | Where-Object { $_.Threads.Count -gt 0 -and $_.Path -and $_.Path.StartsWith($full, 'OrdinalIgnoreCase') }) { throw 'The game in this Package is running. Close it first.' }
$m = Get-Content -Raw -Encoding UTF8 (Join-Path $here 'manifest.json') | ConvertFrom-Json
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$bak = Join-Path $here ('backup_before_install_' + $stamp)
$n = 0
foreach ($f in $m.files) {
  $dst = Join-Path $Package $f.path
  if (Test-Path $dst) {
    $b = Join-Path $bak $f.path; New-Item -ItemType Directory -Force (Split-Path $b) | Out-Null; Copy-Item $dst $b -Force
  }
  New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
  Copy-Item (Join-Path $here ('files\' + $f.path)) $dst -Force
  $n++
}
Write-Host "Installed $n files. Previous files saved in $bak"
'''
UNINSTALL = r'''# Remove the skin: put the stock files back, delete the files the skin added. ASCII only.
param([string]$Package = 'D:\maimai\Package')
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not (Test-Path (Join-Path $Package 'Sinmai_Data'))) { throw "Not a game Package folder: $Package" }
$full = (Resolve-Path $Package).Path.TrimEnd('\') + '\'
if (Get-Process -Name Sinmai -ErrorAction SilentlyContinue | Where-Object { $_.Threads.Count -gt 0 -and $_.Path -and $_.Path.StartsWith($full, 'OrdinalIgnoreCase') }) { throw 'The game in this Package is running. Close it first.' }
$m = Get-Content -Raw -Encoding UTF8 (Join-Path $here 'manifest.json') | ConvertFrom-Json
$r = 0; $d = 0
foreach ($f in $m.files) {
  $dst = Join-Path $Package $f.path
  if ($f.stock_sha256) { Copy-Item (Join-Path $here ('original\' + $f.path)) $dst -Force; $r++ }
  elseif (Test-Path $dst) { Remove-Item $dst -Force; $d++ }
}
Write-Host "Restored $r stock files, removed $d added files."
'''
BAT = '@echo off\r\npowershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0{0}.ps1" %*\r\npause\r\n'
for name, body in (("install", INSTALL), ("uninstall", UNINSTALL)):
    open(os.path.join(ROOT, name + ".ps1"), "w", encoding="ascii", newline="\r\n").write(body)
    open(os.path.join(ROOT, name + ".bat"), "w", encoding="ascii", newline="").write(BAT.format(name))

added = sum(1 for f in manifest if f["stock_sha256"] is None)
README = """maimai DX × NEEDY GIRL OVERDOSE 换皮（{date}）

适用：SDEZ 1.70（maimai DX）+ MelonLoader，需要已经装好 PartnerLive2D 依赖的 MelonLoader 环境。
共 {n} 个文件：替换游戏原有文件 {rep} 个，新增文件 {add} 个。

安装
  1. 关掉游戏。
  2. 双击 install.bat（默认装到 D:\\maimai\\Package；装到别处：install.bat "X:\\路径\\Package"）。
  3. 安装前会把要被覆盖的文件备份到本文件夹的 backup_before_install_<时间>\\。

卸载
  双击 uninstall.bat：把原版文件放回去，删掉换皮新增的文件。

包里有什么
  files\\     换皮后的文件（游戏数据 resources.assets、角色卡图、曲目分类配色、PartnerLive2D 模组和它的素材）
  original\\  对应的原版文件（从 SDEZ1.70 安装包里取出），卸载用
  manifest.json  每个文件的 SHA256（换皮版和原版）

没有打包（各台机器自己的设置和隐私）
  segatools.ini（keychip）、DEVICE\\aime.txt（卡号）、AquaMai.toml、config_*.json、存档/日志/照片。
  玩家名、头像、旅行伙伴等账号设置在 MuNET 服务器上，换机器不受影响。

注意
  包里含游戏和 NEEDY GIRL OVERDOSE 的原始素材，仅供自己的机台使用，请勿公开分发。
""".format(date=time.strftime("%Y-%m-%d"), n=len(manifest), rep=len(manifest) - added, add=added)
open(os.path.join(ROOT, "说明.txt"), "w", encoding="utf-8-sig").write(README)
print(NAME, len(manifest), "files,", added, "added")
