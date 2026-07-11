param(
    # 대상 Civil 3D / AutoCAD 버전 (2018~2027). 같은 밴드는 하나의 바이너리를 공유한다.
    [string]$Civil3DVersion = "2026",
    [string]$Configuration = "Release",
    # 세 밴드(2018–2024 / 2025–2026 / 2027)를 모두 빌드한다
    [switch]$All
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\LayerExporter\LayerExporter.csproj"

# .NET SDK 탐지: PATH의 dotnet에 SDK가 없으면 사용자 폴더 설치본(%LOCALAPPDATA%\Microsoft\dotnet)을 사용한다
$dotnet = "dotnet"
$hasSdk = $false
try { $hasSdk = [bool](& $dotnet --list-sdks 2>$null) } catch {}
if (-not $hasSdk) {
    $localDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
    if ((Test-Path $localDotnet) -and (& $localDotnet --list-sdks 2>$null)) {
        $dotnet = $localDotnet
        $env:DOTNET_ROOT = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
        Write-Host "PATH에 .NET SDK가 없어 $localDotnet 을 사용합니다." -ForegroundColor Yellow
    }
    else {
        throw ".NET SDK 10을 찾을 수 없습니다. https://dot.net/download 에서 설치하세요."
    }
}

# 버전 → 밴드/TFM 매핑 (Directory.Build.props와 동일한 규칙)
function Get-Band([int]$version) {
    if ($version -lt 2018) { throw "지원하지 않는 버전입니다: $version (2018 이상만 지원)" }
    if ($version -le 2024) { return [pscustomobject]@{ Band = "2018"; Tfm = "net48"; Label = "2018-2024 (.NET Framework 4.8)" } }
    if ($version -le 2026) { return [pscustomobject]@{ Band = "2025"; Tfm = "net8.0-windows"; Label = "2025-2026 (.NET 8)" } }
    return [pscustomobject]@{ Band = "2027"; Tfm = "net10.0-windows"; Label = "2027 (.NET 10)" }
}

$targets = if ($All) { @(2018, 2025, 2027) } else { @([int]$Civil3DVersion) }

foreach ($version in $targets) {
    $band = Get-Band $version
    Write-Host "=== Civil 3D/AutoCAD $($band.Label) 빌드 ===" -ForegroundColor Cyan

    & $dotnet build $project -c $Configuration -p:Civil3DVersion=$version
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $outDir = Join-Path $repoRoot "src\LayerExporter\bin\x64\$Configuration\$($band.Tfm)"
    if (-not (Test-Path $outDir)) {
        $outDir = Join-Path $repoRoot "src\LayerExporter\bin\$Configuration\$($band.Tfm)"
    }
    if (-not (Test-Path $outDir)) {
        Write-Error "빌드 출력 폴더를 찾을 수 없습니다: $outDir"
    }

    # 1) NETLOAD용 배포 폴더 (빌드 출력과 분리해 DLL 잠금 문제를 우회)
    $deployDir = Join-Path $repoRoot "deploy\bin\$($band.Band)"
    New-Item -ItemType Directory -Force $deployDir | Out-Null
    Copy-Item -Path (Join-Path $outDir "*") -Destination $deployDir -Recurse -Force

    # 2) autoloader 번들 (.bundle) 구조로 복사
    $bundleDir = Join-Path $repoRoot "deploy\LayerExporter.bundle\Contents\$($band.Band)"
    New-Item -ItemType Directory -Force $bundleDir | Out-Null
    Copy-Item -Path (Join-Path $outDir "*") -Destination $bundleDir -Recurse -Force

    Write-Host ""
    Write-Host "배포 완료 ($($band.Label)):"
    Write-Host "  NETLOAD용: $(Join-Path $deployDir 'LayerExporter.dll')"
    Write-Host "  번들: $bundleDir"
    Write-Host ""
}

Write-Host "자동 로드를 원하면 deploy\LayerExporter.bundle 폴더 전체를"
Write-Host "  %AppData%\Autodesk\ApplicationPlugins\ 아래에 복사하세요."
