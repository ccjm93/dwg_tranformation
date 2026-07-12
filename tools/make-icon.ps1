# DwgToSHP 리본 버튼 아이콘 생성기
# GIS 스타일 "레이어 스택" + 내보내기(다운로드) 배지를 그려 32/16px PNG로 저장한다.
# 512px로 렌더한 뒤 고품질 다운스케일하여 작은 크기에서도 선명하게 만든다.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$outDir = Join-Path $PSScriptRoot '..\src\LayerExporter\Assets'
New-Item -ItemType Directory -Force $outDir | Out-Null

$H = 512
$s = $H / 32.0   # 32단위 설계 좌표 → 렌더 좌표 스케일

$bmp = New-Object System.Drawing.Bitmap($H, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)

function P($x, $y) { New-Object System.Drawing.PointF(([float]($x * $s)), ([float]($y * $s))) }
function Rhombus($cx, $cy, $hw, $hh) {
    [System.Drawing.PointF[]]@( (P $cx ($cy - $hh)), (P ($cx + $hw) $cy), (P $cx ($cy + $hh)), (P ($cx - $hw) $cy) )
}
function ColorRGB($r, $g2, $b) { [System.Drawing.Color]::FromArgb(255, $r, $g2, $b) }

$outline = New-Object System.Drawing.Pen((ColorRGB 234 242 248), ([float](1.4 * $s)))
$outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

# 레이어 스택 (뒤 → 앞 순서로 그림, 앞쪽이 가장 밝은 파랑)
$cx = 14; $hw = 11; $hh = 5.5
$layers = @(
    @{ cy = 22; col = (ColorRGB 21 79 114) },   # 뒤(가장 어두움)
    @{ cy = 17; col = (ColorRGB 36 113 163) },   # 중간
    @{ cy = 12; col = (ColorRGB 52 152 219) }    # 앞(밝음)
)
foreach ($L in $layers) {
    $pts = Rhombus $cx $L.cy $hw $hh
    $g.FillPolygon((New-Object System.Drawing.SolidBrush($L.col)), $pts)
    $g.DrawPolygon($outline, $pts)
}

# 내보내기 배지: 초록 원 + 흰 아래 화살표 (우하단)
$bx = 24; $by = 24; $r = 7
$badgeBox = New-Object System.Drawing.RectangleF( `
    ([float](($bx - $r) * $s)), ([float](($by - $r) * $s)), ([float](2 * $r * $s)), ([float](2 * $r * $s)))
$g.FillEllipse((New-Object System.Drawing.SolidBrush((ColorRGB 39 174 96))), $badgeBox)
$g.DrawEllipse((New-Object System.Drawing.Pen((ColorRGB 255 255 255), ([float](1.2 * $s)))), $badgeBox)

$white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
# 화살표 기둥
$stem = New-Object System.Drawing.RectangleF( `
    ([float](($bx - 1.1) * $s)), ([float](($by - 3.3) * $s)), ([float](2.2 * $s)), ([float](3.6 * $s)))
$g.FillRectangle($white, $stem)
# 화살표 머리 (아래로)
$head = [System.Drawing.PointF[]]@( (P ($bx - 3.4) ($by + 0.2)), (P ($bx + 3.4) ($by + 0.2)), (P $bx ($by + 4.2)) )
$g.FillPolygon($white, $head)

$g.Dispose()

function Save-Scaled($src, $T, $path) {
    $out = New-Object System.Drawing.Bitmap($T, $T, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g2 = [System.Drawing.Graphics]::FromImage($out)
    $g2.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g2.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g2.Clear([System.Drawing.Color]::Transparent)
    $g2.DrawImage($src, (New-Object System.Drawing.Rectangle(0, 0, $T, $T)))
    $g2.Dispose()
    $out.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $out.Dispose()
    Write-Host "생성: $path ($T x $T)"
}

Save-Scaled $bmp 32 (Join-Path $outDir 'icon32.png')
Save-Scaled $bmp 16 (Join-Path $outDir 'icon16.png')
$bmp.Save((Join-Path $outDir 'icon-preview-512.png'), [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "미리보기: $(Join-Path $outDir 'icon-preview-512.png')"
$bmp.Dispose()
