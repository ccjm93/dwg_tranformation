param(
    [string]$Civil3DVersion = "2026",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\LayerExporter\LayerExporter.csproj"

$tfm = if ($Civil3DVersion -eq "2027") { "net10.0-windows" } else { "net8.0-windows" }

dotnet build $project -c $Configuration -p:Civil3DVersion=$Civil3DVersion
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$outDir = Join-Path $repoRoot "src\LayerExporter\bin\x64\$Configuration\$tfm"
if (-not (Test-Path $outDir)) {
    $outDir = Join-Path $repoRoot "src\LayerExporter\bin\$Configuration\$tfm"
}
if (-not (Test-Path $outDir)) {
    Write-Error "빌드 출력 폴더를 찾을 수 없습니다: $outDir"
}

# 1) NETLOAD용 배포 폴더 (빌드 출력과 분리해 DLL 잠금 문제를 우회)
$deployDir = Join-Path $repoRoot "deploy\bin\$Civil3DVersion"
New-Item -ItemType Directory -Force $deployDir | Out-Null
Copy-Item -Path (Join-Path $outDir "*") -Destination $deployDir -Recurse -Force

# 2) autoloader 번들 (.bundle) 구조로 복사
$bundleDir = Join-Path $repoRoot "deploy\LayerExporter.bundle\Contents\$Civil3DVersion"
New-Item -ItemType Directory -Force $bundleDir | Out-Null
Copy-Item -Path (Join-Path $outDir "*") -Destination $bundleDir -Recurse -Force

Write-Host ""
Write-Host "배포 완료:"
Write-Host "  NETLOAD용: $(Join-Path $deployDir 'LayerExporter.dll')"
Write-Host "  번들: $bundleDir"
Write-Host ""
Write-Host "자동 로드를 원하면 deploy\LayerExporter.bundle 폴더 전체를"
Write-Host "  %AppData%\Autodesk\ApplicationPlugins\ 아래에 복사하세요."
