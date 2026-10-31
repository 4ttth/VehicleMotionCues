# Regenerates Assets/icon.png and Assets/app.ico (PNG-compressed ICO) with System.Drawing.
param([string]$OutDir = (Join-Path $PSScriptRoot '..\src\VehicleMotionCues\Assets'))

Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # Rounded dark tile
    $r = 56 * $s
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r, $r, 180, 90)
    $path.AddArc($size - $r - 1, 0, $r, $r, 270, 90)
    $path.AddArc($size - $r - 1, $size - $r - 1, $r, $r, 0, 90)
    $path.AddArc(0, $size - $r - 1, $r, $r, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 24, 28, 38))), $path)

    # Screen in the middle
    $screenBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 47, 111, 214))
    $g.FillRectangle($screenBrush, [float](88 * $s), [float](64 * $s), [float](80 * $s), [float](128 * $s))

    # Edge dots, offset as if the car is turning
    $dot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $d = 26 * $s
    foreach ($x in @(30, 200)) {
        foreach ($y in @(52, 115, 178)) {
            $g.FillEllipse($dot, [float]($x * $s), [float]($y * $s), [float]$d, [float]$d)
        }
    }
    $g.Dispose()
    return $bmp
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$big = New-IconBitmap 256
$big.Save((Join-Path $OutDir 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)

$sizes = 256, 48, 32, 16
$images = foreach ($sz in $sizes) { , (Get-PngBytes (New-IconBitmap $sz)) }

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $images[$i].Length
    $w.Write([byte]($sz % 256)); $w.Write([byte]($sz % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'app.ico'), $out.ToArray())
Write-Output "Wrote icon.png and app.ico to $OutDir"
