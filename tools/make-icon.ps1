param([string]$OutIco, [string]$PreviewPng)
Add-Type -AssemblyName System.Drawing

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.ScaleTransform($size / 256.0, $size / 256.0)

    # Background tile
    $tile = New-RoundRect 8 8 240 240 56
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 8)), ([System.Drawing.PointF]::new(0, 248)), ([System.Drawing.Color]::FromArgb(255, 0x26, 0x2B, 0x36)), ([System.Drawing.Color]::FromArgb(255, 0x0E, 0x10, 0x14))
    $g.FillPath($bg, $tile)
    $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0x3A, 0x41, 0x52)), 4), $tile)

    # Crescent: big disc minus offset disc
    $moon = New-Object System.Drawing.Drawing2D.GraphicsPath
    $moon.AddEllipse(40, 44, 168, 168)
    $cut = New-Object System.Drawing.Drawing2D.GraphicsPath
    $cut.AddEllipse(96, 22, 160, 160)
    $region = New-Object System.Drawing.Region $moon
    $region.Exclude($cut)
    $gold = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(40, 44)), ([System.Drawing.PointF]::new(208, 212)), ([System.Drawing.Color]::FromArgb(255, 0xFF, 0xDB, 0x7A)), ([System.Drawing.Color]::FromArgb(255, 0xE0, 0x9E, 0x2C))
    $g.FillRegion($gold, $region)

    # Dollar sign in the hollow of the crescent
    $font = New-Object System.Drawing.Font 'Segoe UI', 92, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $blue = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x7B, 0x9B, 0xFF))
    $g.DrawString('$', $font, $blue, (New-Object System.Drawing.RectangleF 118, 52, 110, 120), $fmt)

    $g.Dispose(); return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $b = Render $s; $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256 -and $PreviewPng) { $b.Save($PreviewPng, [System.Drawing.Imaging.ImageFormat]::Png) }
    $b.Dispose(); , $ms.ToArray()
}

# ICO container with PNG-compressed entries
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes($OutIco, $out.ToArray())
"wrote $OutIco ($($out.Length) bytes)"
