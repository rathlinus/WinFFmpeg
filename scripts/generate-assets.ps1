<#
.SYNOPSIS
    Draws the app icon and writes every size the package manifest needs.

.DESCRIPTION
    The generated files are checked in; run this only to change the icon.
    Needs Windows PowerShell (System.Drawing).
#>
[CmdletBinding()]
param(
    [string]$OutputFolder
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not $OutputFolder) {
    $OutputFolder = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\src\FFConvert.App\Assets'
}

New-Item -ItemType Directory -Force $OutputFolder | Out-Null
$OutputFolder = (Resolve-Path $OutputFolder).Path

# Draws the icon, $IconSize pixels square, centred on a transparent canvas.
function New-IconBitmap([int]$Width, [int]$Height, [double]$IconSize) {
    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    [single]$x = ($Width - $IconSize) / 2
    [single]$y = ($Height - $IconSize) / 2
    [single]$s = $IconSize
    [single]$d = $s * 0.46   # corner diameter

    $plate = New-Object System.Drawing.Drawing2D.GraphicsPath
    $plate.AddArc($x, $y, $d, $d, 180, 90)
    $plate.AddArc($x + $s - $d, $y, $d, $d, 270, 90)
    $plate.AddArc($x + $s - $d, $y + $s - $d, $d, $d, 0, 90)
    $plate.AddArc($x, $y + $s - $d, $d, $d, 90, 90)
    $plate.CloseFigure()

    $from = New-Object System.Drawing.PointF $x, $y
    $to = New-Object System.Drawing.PointF ($x + $s), ($y + $s)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $from, $to,
        ([System.Drawing.Color]::FromArgb(255, 46, 144, 255)), ([System.Drawing.Color]::FromArgb(255, 112, 72, 232))
    $g.FillPath($brush, $plate)

    # Two opposing arrows: one thing turning into another.
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single]($s * 0.09))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    function P([double]$fx, [double]$fy) { New-Object System.Drawing.PointF ([single]($x + $s * $fx)), ([single]($y + $s * $fy)) }

    $g.DrawLine($pen, (P 0.26 0.37), (P 0.74 0.37))
    $g.DrawLines($pen, [System.Drawing.PointF[]]@((P 0.62 0.25), (P 0.74 0.37), (P 0.62 0.49)))
    $g.DrawLine($pen, (P 0.74 0.63), (P 0.26 0.63))
    $g.DrawLines($pen, [System.Drawing.PointF[]]@((P 0.38 0.51), (P 0.26 0.63), (P 0.38 0.75)))

    $pen.Dispose(); $brush.Dispose(); $plate.Dispose(); $g.Dispose()
    return $bitmap
}

function Save-Png([string]$Name, [int]$Width, [int]$Height, [double]$IconSize) {
    $bitmap = New-IconBitmap $Width $Height $IconSize
    $bitmap.Save((Join-Path $OutputFolder $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

# Tiles and logos. The icon fills small squares and sits in the middle of larger tiles.
Save-Png 'StoreLogo.png' 50 50 50
Save-Png 'Square44x44Logo.scale-200.png' 88 88 88
Save-Png 'Square150x150Logo.scale-200.png' 300 300 150
Save-Png 'Wide310x150Logo.scale-200.png' 620 300 150
Save-Png 'SplashScreen.scale-200.png' 1240 600 200

# Taskbar, Start and Explorer use these exact sizes. "unplated" stops Windows
# from drawing the icon on an accent-coloured square.
foreach ($size in 16, 24, 32, 48, 256) {
    Save-Png "Square44x44Logo.targetsize-$size.png" $size $size $size
    Save-Png "Square44x44Logo.targetsize-${size}_altform-unplated.png" $size $size $size
    Save-Png "Square44x44Logo.targetsize-${size}_altform-lightunplated.png" $size $size $size
}

# AppIcon.ico: PNG-compressed entries, which Windows has supported since Vista.
$sizes = 16, 24, 32, 48, 64, 256
$images = foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size $size $size
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $stream.ToArray()
}

$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $ico
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutputFolder 'AppIcon.ico'), $ico.ToArray())
$writer.Dispose()

Write-Host "Wrote assets to $OutputFolder"
