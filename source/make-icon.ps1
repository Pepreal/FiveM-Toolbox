# Draws the program icon (a speed gauge on a rounded blue square) and writes app.ico next to this script.
# Nothing to install: it uses System.Drawing from Windows. Run:  powershell -ExecutionPolicy Bypass -File make-icon.ps1
Add-Type -AssemblyName System.Drawing

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
    $hex = { param($h) [System.Drawing.ColorTranslator]::FromHtml($h) }

    # flat near-black rounded square, no gradient and no outline (FiveM's own icon is flat black with one bold bright shape)
    $m = $s * 0.03
    $path = New-RoundedRect $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.20)
    $bgColor = & $hex '#0c0d0f'
    $bgBrush = New-Object System.Drawing.SolidBrush $bgColor
    $g.FillPath($bgBrush, $path)

    # the gauge is built from sharp straight segments and leans forward, like FiveM's slanted mark
    $cx = $s * 0.5; $cy = $s * 0.60
    $ro = $s * 0.40; $ri = $s * 0.215
    $lean = 0.20
    $pt = {
        param([double]$angle, [double]$radius)
        $a = $angle * [Math]::PI / 180
        $x = $cx + [Math]::Cos($a) * $radius
        $y = $cy + [Math]::Sin($a) * $radius
        New-Object System.Drawing.PointF ([float]($x - $lean * ($y - $cy))), ([float]$y)
    }
    $n = if ($s -le 32) { 4 } else { 6 }
    $sweep = 240.0
    $gap = if ($s -le 32) { 7.0 } else { 5.0 }
    $lit = [Math]::Round($n * 0.68)
    $litBrush = New-Object System.Drawing.SolidBrush (& $hex '#1177b5')
    $dimBrush = New-Object System.Drawing.SolidBrush (& $hex '#0e3b59')
    for ($i = 0; $i -lt $n; $i++) {
        $a0 = 150 + $i * ($sweep / $n) + $gap / 2
        $a1 = 150 + ($i + 1) * ($sweep / $n) - $gap / 2
        $quad = [System.Drawing.PointF[]]@((& $pt $a0 $ro), (& $pt $a1 $ro), (& $pt $a1 $ri), (& $pt $a0 $ri))
        $g.FillPolygon($(if ($i -lt $lit) { $litBrush } else { $dimBrush }), $quad)
    }

    # the diagonal cut that FiveM's logo has: a thin band in the background colour (bigger sizes only)
    if ($s -ge 48) {
        $w = $s * 0.05
        $cut = New-Object System.Drawing.SolidBrush $bgColor
        $pts = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF ([float]($s * 0.29)), ([float]($s * 0.95))),
            (New-Object System.Drawing.PointF ([float]($s * 0.29 + $w * 1.7)), ([float]($s * 0.95))),
            (New-Object System.Drawing.PointF ([float]($s * 0.73 + $w * 1.7)), ([float]($s * 0.08))),
            (New-Object System.Drawing.PointF ([float]($s * 0.73)), ([float]($s * 0.08))))
        $g.FillPolygon($cut, $pts)
    }
    # a sharp needle and a small hub, in a lighter blue
    $needleDeg = 150 + $sweep * 0.74
    $half = [Math]::Max(1.5, $s * 0.055)
    $tip = & $pt $needleDeg ($ro * 0.93)
    $b1 = & $pt ($needleDeg + 90) $half
    $b2 = & $pt ($needleDeg - 90) $half
    $needleBrush = New-Object System.Drawing.SolidBrush (& $hex '#6fd0ff')
    $g.FillPolygon($needleBrush, [System.Drawing.PointF[]]@($tip, $b1, $b2))
    $hubPts = @()
    for ($k = 0; $k -lt 20; $k++) { $hubPts += (& $pt ($k * 18) ($s * 0.07)) }
    $g.FillPolygon($needleBrush, [System.Drawing.PointF[]]$hubPts)

    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$sheet = New-Object System.Drawing.Bitmap 520, 280
$sg = [System.Drawing.Graphics]::FromImage($sheet); $sg.Clear([System.Drawing.Color]::FromArgb(255, 150, 150, 150)); $sx = 8
$pngs = @()
foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    if ($s -eq 256) { $bmp.Save((Join-Path $PSScriptRoot 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    if ($s -le 64) { $sg.DrawImage($bmp, $sx, 8); $sx += $s + 10 }
    $bmp.Dispose()
}

# ICO file: header, one directory entry per size, then the PNG pictures.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'app.ico'), $out.ToArray())
$sg.Dispose(); $sheet.Save((Join-Path $PSScriptRoot 'icon-sizes.png'), [System.Drawing.Imaging.ImageFormat]::Png)
"Wrote app.ico ($($sizes -join ', ') px) and icon-preview.png"
