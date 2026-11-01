# Build ngo_patcher.exe with PyInstaller (one folder, console). ASCII only.
#   powershell -ExecutionPolicy Bypass -File build_exe.ps1 -Python <python.exe> [-Work <scratch dir>] [-Out <folder>]
# The Python needs: UnityPy, TypeTreeGeneratorAPI, Pillow, texture2ddecoder, etcpak, astc_encoder, pyinstaller.
# numpy is left out on purpose: only the developer 'compare' command uses it (run that from source).
# Result: <Out>\ngo_patcher\ngo_patcher.exe (+ _internal). Players need no Python.
param(
    [string]$Python = 'python',
    [string]$Work = (Join-Path $env:TEMP 'ngo_pyi'),
    [string]$Out = (Join-Path $PSScriptRoot 'dist')
)
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'ngo_patcher.py'
New-Item -ItemType Directory -Force -Path $Work | Out-Null
# keep the PyInstaller cache next to the work folder (not on C:)
$env:PYINSTALLER_CONFIG_DIR = Join-Path $Work 'config'
$pyiArgs = @('-m', 'PyInstaller', '--noconfirm', '--clean', '--onedir', '--console', '--name', 'ngo_patcher',
    '--distpath', (Join-Path $Work 'dist'), '--workpath', (Join-Path $Work 'build'), '--specpath', $Work,
    '--collect-all', 'UnityPy', '--collect-all', 'TypeTreeGeneratorAPI',
    '--collect-binaries', 'etcpak', '--collect-binaries', 'texture2ddecoder', '--collect-binaries', 'astc_encoder',
    '--copy-metadata', 'UnityPy', '--copy-metadata', 'TypeTreeGeneratorAPI', '--copy-metadata', 'pillow',
    '--copy-metadata', 'texture2ddecoder', '--copy-metadata', 'etcpak',
    '--copy-metadata', 'astc-encoder-py', '--copy-metadata', 'lz4', '--copy-metadata', 'brotli', '--copy-metadata', 'attrs',
    '--exclude-module', 'tkinter', '--exclude-module', 'matplotlib', '--exclude-module', 'scipy',
    '--exclude-module', 'pandas', '--exclude-module', 'IPython', '--exclude-module', 'pyfmodex',
    '--exclude-module', 'numpy', '--exclude-module', 'pyarrow', '--exclude-module', 'cryptography',
    '--exclude-module', 'pydantic', '--exclude-module', 'pydantic_core', '--exclude-module', 'PIL.AvifImagePlugin',
    '--exclude-module', 'requests', '--exclude-module', 'aiohttp', '--exclude-module', 'jinja2',
    '--exclude-module', 'paramiko', '--exclude-module', 'invoke', '--exclude-module', 'pyreadline3',
    $src)
& $Python @pyiArgs
if ($LASTEXITCODE -ne 0) { throw 'PyInstaller failed' }
$built = Join-Path $Work 'dist\ngo_patcher'
# FMOD is proprietary and only used for audio export: never ship its DLL
Get-ChildItem $built -Recurse -Filter 'fmod*.dll' | ForEach-Object { Remove-Item $_.FullName -Force; Write-Host "removed $($_.Name)" }
$dst = Join-Path $Out 'ngo_patcher'
if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
Copy-Item $built $dst -Recurse
Write-Host "built $dst"
