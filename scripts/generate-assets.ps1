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
    $OutputFolder = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\src\WinFFmpeg.App\Assets'
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

    # Four tiles in the arrangement of the Windows logo.
    [single]$gap = $s * 0.06
    [single]$tile = ($s - $gap) / 2
    [single]$d = $tile * 0.24   # corner diameter
    $tiles = New-Object System.Drawing.Drawing2D.GraphicsPath
    foreach ($row in 0, 1) {
        foreach ($column in 0, 1) {
            [single]$tx = $x + $column * ($tile + $gap)
            [single]$ty = $y + $row * ($tile + $gap)
            $tiles.StartFigure()
            $tiles.AddArc($tx, $ty, $d, $d, 180, 90)
            $tiles.AddArc($tx + $tile - $d, $ty, $d, $d, 270, 90)
            $tiles.AddArc($tx + $tile - $d, $ty + $tile - $d, $d, $d, 0, 90)
            $tiles.AddArc($tx, $ty + $tile - $d, $d, $d, 90, 90)
            $tiles.CloseFigure()
        }
    }

    # ffmpeg green, a little lighter in the top left than in the bottom right.
    $from = New-Object System.Drawing.PointF $x, $y
    $to = New-Object System.Drawing.PointF ($x + $s), ($y + $s)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $from, $to,
        ([System.Drawing.Color]::FromArgb(255, 84, 184, 84)), ([System.Drawing.Color]::FromArgb(255, 40, 138, 46))
    $g.FillPath($brush, $tiles)

    # The zigzag scan order video codecs read a block in, which is also the motif of
    # the ffmpeg logo. Every size draws this same shape; there is no simplified
    # variant for small icons.
    function P([double]$fx, [double]$fy) { New-Object System.Drawing.PointF ([single]($x + $s * $fx)), ([single]($y + $s * $fy)) }

    $a = 0.2; $b = 0.5; $c = 0.8
    $points = (P $a $a), (P $b $a), (P $a $b), (P $a $c), (P $b $b), (P $c $a), (P $c $b), (P $b $c), (P $c $c)

    # Proportional to the icon, but never thinner than the line needs to stay visible at 16 pixels.
    [single]$stroke = [Math]::Max($s * 0.075, 1.4)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $stroke
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($pen, [System.Drawing.PointF[]]$points)

    $pen.Dispose(); $brush.Dispose(); $tiles.Dispose(); $g.Dispose()
    return $bitmap
}

function Save-Png([string]$Name, [int]$Width, [int]$Height, [double]$IconSize) {
    $bitmap = New-IconBitmap $Width $Height $IconSize
    $bitmap.Save((Join-Path $OutputFolder $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

# The logo on its own, for the About card and the README.
Save-Png 'Logo.png' 256 256 256

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

# The two pictures the setup wizard shows: the panel beside the welcome and finish
# pages, and the small one in the corner of the others. Inno Setup wants bitmaps
# without transparency, so the logo is drawn onto a solid background. Both are
# twice the size the wizard uses at 100%, which keeps them sharp on scaled displays.
function Save-WizardBitmap([string]$Name, [int]$Width, [int]$Height, [double]$IconSize, [double]$IconTop, [System.Drawing.Color]$Background) {
    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.Clear($Background)
    $logo = New-IconBitmap ([int]$IconSize) ([int]$IconSize) $IconSize
    $g.DrawImage($logo, [int](($Width - $IconSize) / 2), [int]$IconTop, [int]$IconSize, [int]$IconSize)
    $logo.Dispose(); $g.Dispose()
    $bitmap.Save((Join-Path $packagingFolder $Name), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bitmap.Dispose()
}

$packagingFolder = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\packaging')).Path
Save-WizardBitmap 'installer-side.bmp' 328 628 168 150 ([System.Drawing.Color]::FromArgb(255, 32, 32, 32))
Save-WizardBitmap 'installer-small.bmp' 110 110 94 8 ([System.Drawing.Color]::White)

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
