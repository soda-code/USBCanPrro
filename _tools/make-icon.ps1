Add-Type -AssemblyName System.Drawing

$sizes = @(256, 128, 64, 48, 32, 16)
$root = Split-Path $PSScriptRoot -Parent
$outPath = Join-Path $root "CanUSB/app.ico"

function New-RoundedPath([System.Drawing.RectangleF]$rect, [single]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [single]($size * 0.035)
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)
    $radius = [single]($size * 0.22)

    $path = New-RoundedPath $rect $radius

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 59, 130, 246),
        [System.Drawing.Color]::FromArgb(255, 29, 78, 216),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillPath($brush, $path)

    # 字母 C
    $fontSize = [single]($size * 0.60)
    $font = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textRect = New-Object System.Drawing.RectangleF 0, ($size * 0.02), $size, $size
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.DrawString("C", $font, $white, $textRect, $sf)

    # 右下角总线小点
    if ($size -ge 32) {
        $dot = [single]($size * 0.055)
        $dotBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 255, 255, 255))
        for ($i = 0; $i -lt 3; $i++) {
            $x = $rect.Right - ($i + 1) * ($dot * 2.6) - $dot
            $y = $rect.Bottom - $dot * 2.6
            $g.FillEllipse($dotBrush, [single]$x, [single]$y, [single]$dot, [single]$dot)
        }
        $dotBrush.Dispose()
    }

    $white.Dispose()
    $sf.Dispose()
    $font.Dispose()
    $brush.Dispose()
    $path.Dispose()
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    # 逗号包裹避免 PowerShell 把 byte[] 展开成逐个字节
    return , $ms.ToArray()
}

# 生成各尺寸 PNG 数据
$pngs = @()
foreach ($s in $sizes) { $pngs += , (New-IconPng $s) }

# 组装 ICO
$fs = [System.IO.File]::Create($outPath)
$bw = New-Object System.IO.BinaryWriter($fs)

$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}

foreach ($png in $pngs) { $bw.Write($png) }

$bw.Flush()
$bw.Close()
$fs.Close()

Write-Host ("icon written: " + $outPath + " (" + (Get-Item $outPath).Length + " bytes)")

# 导出 256 预览
$preview = New-IconPng 256
[System.IO.File]::WriteAllBytes((Join-Path $root "icon-preview.png"), $preview)
Write-Host "preview written"
