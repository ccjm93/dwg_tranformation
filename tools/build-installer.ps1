param(
    [string]$Configuration = "Release",
    [string]$CoordinateLibraryPath = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$bundleRoot = Join-Path $repoRoot "deploy\LayerExporter.bundle"
$payloadRoot = Join-Path $repoRoot "installer\payload"
$stagingRoot = Join-Path $repoRoot "installer\staging"
$defaultCoordinateLibraryPath = Join-Path $repoRoot "src\LayerExporter\Assets\CSLibrary.xml"
if ([string]::IsNullOrWhiteSpace($CoordinateLibraryPath)) {
    $CoordinateLibraryPath = $defaultCoordinateLibraryPath
}
$payloadZip = Join-Path $payloadRoot "LayerExporter.bundle.zip"
$installerProject = Join-Path $repoRoot "installer\LayerExporter.Installer.csproj"
$distRoot = Join-Path $repoRoot "installer\dist"
$dotnet = "dotnet"
$hasSdk = $false
try { $hasSdk = [bool](& $dotnet --list-sdks 2>$null) } catch {}
if (-not $hasSdk) {
    $localDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
    if (-not (Test-Path -LiteralPath $localDotnet)) { throw ".NET SDK 10을 찾을 수 없습니다." }
    $dotnet = $localDotnet
}

if (-not (Test-Path -LiteralPath $CoordinateLibraryPath)) {
    throw "좌표계 라이브러리를 찾을 수 없습니다: $CoordinateLibraryPath"
}

& (Join-Path $PSScriptRoot "build.ps1") -All -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

foreach ($band in "2018", "2025", "2027") {
    Copy-Item -LiteralPath $CoordinateLibraryPath -Destination (Join-Path $bundleRoot "Contents\$band\CSLibrary.xml") -Force
    Copy-Item -LiteralPath $CoordinateLibraryPath -Destination (Join-Path $repoRoot "deploy\bin\$band\CSLibrary.xml") -Force
}

if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
Copy-Item -LiteralPath $bundleRoot -Destination $stagingRoot -Recurse -Force
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
if (Test-Path -LiteralPath $payloadZip) { Remove-Item -LiteralPath $payloadZip -Force }

Push-Location $stagingRoot
try {
    Compress-Archive -LiteralPath "LayerExporter.bundle" -DestinationPath $payloadZip -CompressionLevel Optimal
}
finally {
    Pop-Location
}

& $dotnet publish $installerProject -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -o $distRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$setupExe = Join-Path $distRoot "LayerExporter-Setup.exe"
if (-not (Test-Path -LiteralPath $setupExe)) { throw "설치 파일을 만들지 못했습니다: $setupExe" }
Write-Host "패키징 완료: $setupExe"
