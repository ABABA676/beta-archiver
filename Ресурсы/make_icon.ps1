<#
    Генератор иконки «Беты» — ЯБЛОКО РАЗДОРА.

    Замысел: не «мультяшная рука с яблоком», а величественный знак —
    тёмный медальон, бронзовая рука (как у статуи), багровое яблоко, золото.
    Обладатель уже получил яблоко и намеревается творить великие планы.

    Идея реализации: рисуем ВЕКТОРНО в координатах 0..1 и масштамируем на каждый размер
    ОТДЕЛЬНО, потому что одна картинка на все размеры не читается:
      • ≤20 px  — тёмный круг + яблоко (без руки и без кольца: детали превращаются в грязь)
      • 24–40 px — круг + золотое кольцо + яблоко + простая бронзовая «чаша»
      • ≥48 px  — полный вариант: кольцо, большой палец, золото�� лист, свечение, искры

    Что делает:
      • Ресурсы\Бета.ico      — многослойная иконка (BMP для мелких, PNG для 128/256)
      • Ресурсы\icon-*.png    — то же отдельными файлами (для README и витрины)
      • Ресурсы\preview.png   — контрольный лист: все размеры на светлом и тёмном фоне

    Запуск:  .\make_icon.ps1
#>
[CmdletBinding()]
param([string]$OutDir)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# $PSScriptRoot в значении по умолчанию param в PS 5.1 не надёжен — определяем в теле скрипта
if (-not $OutDir) { $OutDir = $PSScriptRoot }

function C([int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb(255, $r, $g, $b) }
function CA([int]$a, [int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }

# --- палитра: ночь, золото, бронза, багрец ---
$script:NightTop  = C 26 29 38      # медальон сверху
$script:NightBot  = C 4 5 9         # медальон снизу
$script:Gold1     = C 242 214 138    # золото светлое
$script:Gold2     = C 150 116 32     # золото тёмное
$script:Bronze1   = C 226 190 118    # бронза светлая
$script:Bronze2   = C 96 70 26       # бронза тёмная
$script:BronzeEdge = C 44 32 10      # контур бронзы
$script:Apple1    = C 226 59 46      # яблоко сверху
$script:Apple2    = C 104 12 17      # яблоко снизу
$script:AppleEdge = C 46 6 9         # контур яблока
$script:Gloss     = CA 120 255 236 214

# --- силуэт яблока ---
function New-ApplePath {
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $p.StartFigure()
    $p.AddBezier(0.50, 0.245, 0.655, 0.145, 0.845, 0.265, 0.835, 0.495)
    $p.AddBezier(0.835, 0.495, 0.825, 0.690, 0.620, 0.780, 0.500, 0.750)
    $p.AddBezier(0.500, 0.750, 0.380, 0.780, 0.175, 0.690, 0.165, 0.495)
    $p.AddBezier(0.165, 0.495, 0.155, 0.265, 0.345, 0.145, 0.500, 0.245)
    $p.CloseFigure()
    $p
}

# --- «чаша» бронзовой руки; верхняя кромка спрятана за яблоком ---
function New-HandPath {
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $p.StartFigure()
    $p.AddBezier(0.120, 0.520, 0.105, 0.760, 0.250, 0.865, 0.500, 0.865)
    $p.AddBezier(0.500, 0.865, 0.750, 0.865, 0.895, 0.760, 0.880, 0.520)
    $p.AddBezier(0.880, 0.520, 0.870, 0.445, 0.830, 0.415, 0.780, 0.415)
    $p.AddLine(0.780, 0.415, 0.220, 0.415)
    $p.AddBezier(0.220, 0.415, 0.170, 0.415, 0.130, 0.445, 0.120, 0.520)
    $p.CloseFigure()
    $p
}

# овальная «капля» с поворотом — большой палец и лист
function New-BlobPath([single]$cx, [single]$cy, [single]$w, [single]$h, [single]$deg) {
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $p.AddEllipse(($cx - $w / 2), ($cy - $h / 2), $w, $h)
    $m = [System.Drawing.Drawing2D.Matrix]::new()
    $m.Translate([single]$cx, [single]$cy)
    $m.Rotate([single]$deg)
    $m.Translate([single](- $cx), [single](- $cy))
    $p.Transform($m)
    $p
}

# четырёхлучевая искра
function New-SparkPath([single]$cx, [single]$cy, [single]$r) {
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $q = $r * 0.26
    $p.AddPolygon([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($cx, $cy - $r),
        [System.Drawing.PointF]::new($cx + $q, $cy - $q),
        [System.Drawing.PointF]::new($cx + $r, $cy),
        [System.Drawing.PointF]::new($cx + $q, $cy + $q),
        [System.Drawing.PointF]::new($cx, $cy + $r),
        [System.Drawing.PointF]::new($cx - $q, $cy + $q),
        [System.Drawing.PointF]::new($cx - $r, $cy),
        [System.Drawing.PointF]::new($cx - $q, $cy - $q)
    ))
    $p
}

function Draw-Icon([System.Drawing.Graphics]$g, [int]$size) {
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform([single]$size, [single]$size)

    if ($size -le 20) { $detail = 'apple' }
    elseif ($size -le 40) { $detail = 'cup' }
    else { $detail = 'full' }

    $edge = switch ($detail) { 'apple' { 0.0 } 'cup' { 0.024 } default { 0.028 } }

    # --- 1. медальон: ночной круг ---
    $medal = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $medal.AddEllipse(0.02, 0.02, 0.96, 0.96)
    $night = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new(0.5, 0.02), [System.Drawing.PointF]::new(0.5, 0.98), $NightTop, $NightBot)
    $g.FillPath($night, $medal)

    # --- 2. золотое кольцо медальона ---
    if ($detail -ne 'apple') {
        $ring = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $ring.AddEllipse(0.050, 0.050, 0.900, 0.900)
        $ring.AddEllipse(0.108, 0.108, 0.784, 0.784)
        $gold = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.PointF]::new(0.1, 0.05), [System.Drawing.PointF]::new(0.9, 0.95), $Gold1, $Gold2)
        $g.FillPath($gold, $ring)
    }

    # --- 3. свечение за яблоком (только крупные) ---
    if ($size -ge 48) {
        foreach ($ring2 in @(@(0.46, 16), @(0.38, 22), @(0.30, 30))) {
            $d = [single]$ring2[0]
            $a = [int]$ring2[1]
            $col = CA $a 255 214 160          # вызов функции внутри аргументов метода недопустим — считаем заранее
            $g.FillEllipse([System.Drawing.SolidBrush]::new($col), (0.5 - $d / 2), (0.42 - $d / 2), $d, $d)
        }
    }

    # --- 4. бронзовая рука (сзади) ---
    if ($detail -ne 'apple') {
        $hand = New-HandPath
        $bronze = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.PointF]::new(0.2, 0.45), [System.Drawing.PointF]::new(0.8, 0.97), $Bronze1, $Bronze2)
        $g.FillPath($bronze, $hand)
        $g.DrawPath([System.Drawing.Pen]::new($BronzeEdge, [single]$edge), $hand)
    }

    # --- 5. яблоко раздора ---
    $apple = New-ApplePath
    $crimson = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new(0.5, 0.14), [System.Drawing.PointF]::new(0.5, 0.78), $Apple1, $Apple2)
    $g.FillPath($crimson, $apple)
    $g.DrawPath([System.Drawing.Pen]::new($AppleEdge, [single]$(if ($detail -eq 'apple') { 0.03 } else { $edge })), $apple)

    # --- 6. золотой лист и ветка ---
    $stemPen = [System.Drawing.Pen]::new($Gold2, [single]$(if ($size -le 20) { 0.07 } else { 0.05 }))
    $stemPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $stemPen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($stemPen, 0.50, 0.26, 0.545, 0.085)

    $leafPath = New-BlobPath 0.665 0.135 0.255 0.130 (-26)
    $leafFill = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new(0.56, 0.05), [System.Drawing.PointF]::new(0.78, 0.22), $Gold1, $Gold2)
    $g.FillPath($leafFill, $leafPath)
    if ($size -ge 32) { $g.DrawPath([System.Drawing.Pen]::new($BronzeEdge, [single]0.022), $leafPath) }

    # --- 7. большой палец (только крупные) ---
    if ($detail -eq 'full') {
        $thumb = New-BlobPath 0.735 0.585 0.355 0.130 (-42)
        $thumbFill = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.PointF]::new(0.60, 0.47), [System.Drawing.PointF]::new(0.90, 0.72), $Bronze1, $Bronze2)
        $g.FillPath($thumbFill, $thumb)
        $g.DrawPath([System.Drawing.Pen]::new($BronzeEdge, [single]0.028), $thumb)
    }

    # --- 8. блик и искры величия ---
    if ($size -ge 48) {
        $g.FillEllipse([System.Drawing.SolidBrush]::new($Gloss), 0.275, 0.320, 0.135, 0.095)
    }
    if ($size -ge 64) {
        $spark = [System.Drawing.SolidBrush]::new($Gold1)
        foreach ($s in @(@(0.205, 0.235, 0.055), @(0.815, 0.315, 0.040))) {
            $sp = New-SparkPath ([single]$s[0]) ([single]$s[1]) ([single]$s[2])
            $g.FillPath($spark, $sp)
        }
    }
}

function New-IconBitmap([int]$size) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    Draw-Icon $g $size
    $g.Dispose()
    $bmp
}

# --- упаковка кадра в .ico ---
function Get-BmpEntry([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $data = $bmp.LockBits([System.Drawing.Rectangle]::new(0, 0, $w, $h),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $px = [byte[]]::new($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $px, 0, $px.Length)
    $bmp.UnlockBits($data)

    $ms = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($ms)
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2))
    $bw.Write([uint16]1); $bw.Write([uint16]32)   # biPlanes и biBitCount — по 2 байта; если писать int, заголовок расползается на 4 байта и .ico битый
    $bw.Write([int]0)                              # biCompression = BI_RGB
    $bw.Write([int]($stride * $h))                # biSizeImage
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $h - 1; $y -ge 0; $y--) {
        $row = $y * $stride
        for ($x = 0; $x -lt $w; $x++) {
            $i = $row + $x * 4
            $bw.Write($px[$i]); $bw.Write($px[$i + 1]); $bw.Write($px[$i + 2]); $bw.Write($px[$i + 3])
        }
    }
    $maskStride = [int]([Math]::Ceiling($w / 32.0) * 4)
    $bw.Write([byte[]]::new($maskStride * $h))
    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    , $bytes
}

function Get-PngEntry([System.Drawing.Bitmap]$bmp) {
    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $data = if ($s -ge 128) { Get-PngEntry $bmp } else { Get-BmpEntry $bmp }
    $frames += [pscustomobject]@{ Size = $s; Data = $data; Bitmap = $bmp }
}

$ico = [System.IO.MemoryStream]::new()
$w = [System.IO.BinaryWriter]::new($ico)
# ICONDIR = 6 байт: reserved, type, count — все три по 2 байта. Если писать int, файл «плывёт» на 6 байт.
$w.Write([uint16]0)            # reserved
$w.Write([uint16]1)            # type = 1 (икона)
$w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim)     # ширина и высота (0 = 256)
    $w.Write([byte]0); $w.Write([byte]0)            # палитра и резерв
    $w.Write([uint16]1); $w.Write([uint16]32)       # planes и bitCount — по 2 байта, иначе запись кадра расползается
    $w.Write([int]$f.Data.Length); $w.Write([int]$offset)
    $offset += $f.Data.Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f.Data) }
$w.Flush()

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$icoPath = Join-Path $OutDir 'Бета.ico'
[System.IO.File]::WriteAllBytes($icoPath, $ico.ToArray())
$w.Dispose(); $ico.Dispose()

foreach ($s in 16, 32, 48, 128, 256) {
    $bmp = ($frames | Where-Object { $_.Size -eq $s }).Bitmap
    [System.IO.File]::WriteAllBytes((Join-Path $OutDir "icon-$s.png"), (Get-PngEntry $bmp))
}

# контрольный лист: размеры на светлом и тёмном фоне
$cell = 150
$sheet = [System.Drawing.Bitmap]::new($sizes.Count * $cell, ($cell * 2 + 34), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$gs = [System.Drawing.Graphics]::FromImage($sheet)
$gs.Clear([System.Drawing.Color]::FromArgb(255, 236, 236, 232))
$gs.FillRectangle([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 18, 18, 22)), 0, $cell + 17, $sheet.Width, $cell)
$font = [System.Drawing.Font]::new('Segoe UI', 9)
$brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 90, 95, 110))
$brushLight = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 200, 205, 215))
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $bmp = $frames[$i].Bitmap
    $x = $i * $cell + [int](($cell - $bmp.Width) / 2)
    $gs.DrawString("$($sizes[$i]) px", $font, $brush, ($i * $cell + 8), 3)
    $gs.DrawImage($bmp, $x, [int](($cell - $bmp.Height) / 2))
    $gs.DrawString("$($sizes[$i]) px", $font, $brushLight, ($i * $cell + 8), ($cell + 22))
    $gs.DrawImage($bmp, $x, ($cell + 17) + [int](($cell - $bmp.Height) / 2))
}
$gs.Dispose()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'preview.png'), (Get-PngEntry $sheet))
$sheet.Dispose()

Write-Host "иконка: $icoPath  ($([math]::Round((Get-Item $icoPath).Length / 1KB, 1)) КБ; кадры: $($sizes -join ', '))"
Write-Host "превью: $(Join-Path $OutDir 'preview.png')"
