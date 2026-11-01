# Build (and optionally install) the PartnerLive2D mod. ASCII only.
#   powershell -ExecutionPolicy Bypass -File build.ps1            build to .\bin
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Install   build + copy into the game
param(
    [switch]$Install,
    [string]$Package = 'D:\maimai\Package'
)
$ErrorActionPreference = 'Stop'

# keep every SDK/NuGet cache on E:
$env:DOTNET_CLI_HOME = 'E:\tools\dotnet-home'
$env:NUGET_PACKAGES = 'E:\tools\nuget-packages'
$env:NUGET_HTTP_CACHE_PATH = 'E:\tools\nuget-http-cache'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'

$here = $PSScriptRoot
$core = 'E:\tools\live2d\CubismSdkForNative-5-r.5\Core\dll\windows\x86_64\Live2DCubismCore.dll'

& 'E:\tools\dotnet\dotnet.exe' build (Join-Path $here 'PartnerLive2D.csproj') -c Release -o (Join-Path $here 'bin') -p:GamePackage=$Package
if ($LASTEXITCODE -ne 0) { throw "build failed" }

# Build the regression harness through the same controlled build entry point.
& 'E:\tools\dotnet\dotnet.exe' build (Join-Path $here 'tests\Headless\Headless.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "headless build failed" }

if ($Install) {
    Copy-Item (Join-Path $here 'bin\PartnerLive2D.dll') (Join-Path $Package 'Mods\PartnerLive2D.dll') -Force
    Copy-Item $core (Join-Path $Package 'Live2DCubismCore.dll') -Force
    $deploy = Join-Path $here 'deploy'
    Get-ChildItem $deploy -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($deploy.Length + 1)
        $dst = Join-Path $Package $rel
        New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
        Copy-Item $_.FullName $dst -Force
    }
    Write-Host "installed into $Package"
}
