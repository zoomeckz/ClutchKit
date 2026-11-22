# Draws the ClutchKit icon: red rounded square + three white lines.
#   -Style tally   : three vertical lines (default)
#   -Style bars    : three rising bars
#   -Style stack   : three horizontal lines
# Writes <Out>.ico (classic 32-bit bitmaps for small sizes, PNG for 256) and <Out>.png (256px).
param([string]$Out, [string]$Style = 'tally')
Add-Type -AssemblyName System.Drawing

function Draw-Icon([int]$s, [string]$style) {
    $bmp = New-Object Drawing.Bitmap($s, $s, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([Drawing.Color]::Transparent)
    $k = $s / 100.0
    $pad = if ($s -le 20) { 0.5 } else { 2 * $k }
    $r = 24 * $k; $w = $s - 2 * $pad
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($pad, $pad, 2*$r, 2*$r, 180, 90); $path.AddArc($pad + $w - 2*$r, $pad, 2*$r, 2*$r, 270, 90)
    $path.AddArc($pad + $w - 2*$r, $pad + $w - 2*$r, 2*$r, 2*$r, 0, 90); $path.AddArc($pad, $pad + $w - 2*$r, 2*$r, 2*$r, 90, 90); $path.CloseFigure()
    $rect = New-Object Drawing.RectangleF($pad, $pad, $w, $w)
    $grad = New-Object Drawing.Drawing2D.LinearGradientBrush($rect, [Drawing.Color]::FromArgb(255, 245, 78, 84), [Drawing.Color]::FromArgb(255, 176, 22, 48), 90.0)
    $g.FillPath($grad, $path)

    # three lines as rounded bars: x, y, width, height in 0..100 units
    switch ($style) {
        'bars'  { $bars = @(@(22, 56, 14, 22), @(43, 40, 14, 38), @(64, 24, 14, 54)) }
        'stack' { $bars = @(@(24, 26, 52, 13), @(24, 44, 52, 13), @(24, 62, 36, 13)) }
        default { $bars = @(@(25, 24, 13, 52), @(43.5, 24, 13, 52), @(62, 24, 13, 52)) }
    }
    if ($s -le 20) {   # tiny sizes: snap to whole pixels so the lines stay crisp
        $g.SmoothingMode = 'None'
    }
    foreach ($b in $bars) {
        $x = $b[0] * $k; $y = $b[1] * $k; $bw = $b[2] * $k; $bh = $b[3] * $k
        if ($s -le 20) { $x = [Math]::Round($x); $y = [Math]::Round($y); $bw = [Math]::Max(2, [Math]::Round($bw)); $bh = [Math]::Round($bh) }
        $rr = [Math]::Min($bw, $bh) / 2
        $bp = New-Object Drawing.Drawing2D.GraphicsPath
        if ($s -le 20 -or $rr -lt 1) { $bp.AddRectangle((New-Object Drawing.RectangleF($x, $y, $bw, $bh))) }
        else {
            $bp.AddArc($x, $y, 2*$rr, 2*$rr, 180, 90); $bp.AddArc($x + $bw - 2*$rr, $y, 2*$rr, 2*$rr, 270, 90)
            $bp.AddArc($x + $bw - 2*$rr, $y + $bh - 2*$rr, 2*$rr, 2*$rr, 0, 90); $bp.AddArc($x, $y + $bh - 2*$rr, 2*$rr, 2*$rr, 90, 90); $bp.CloseFigure()
        }
        $g.FillPath([Drawing.Brushes]::White, $bp)
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$entries = @()
foreach ($s in $sizes) {
    $bmp = Draw-Icon $s $Style
    $ms = New-Object IO.MemoryStream
    if ($s -eq 256) { $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Save("$Out.png", [Drawing.Imaging.ImageFormat]::Png) }
    else {
        $w2 = New-Object IO.BinaryWriter($ms)
        $w2.Write([UInt32]40); $w2.Write([Int32]$s); $w2.Write([Int32]($s * 2)); $w2.Write([UInt16]1); $w2.Write([UInt16]32)
        $w2.Write([UInt32]0); $w2.Write([UInt32]0); $w2.Write([Int32]0); $w2.Write([Int32]0); $w2.Write([UInt32]0); $w2.Write([UInt32]0)
        for ($y = $s - 1; $y -ge 0; $y--) { for ($x2 = 0; $x2 -lt $s; $x2++) { $c = $bmp.GetPixel($x2, $y); $w2.Write([Byte]$c.B); $w2.Write([Byte]$c.G); $w2.Write([Byte]$c.R); $w2.Write([Byte]$c.A) } }
        $w2.Write((New-Object 'Byte[]' ([int]([Math]::Ceiling($s / 32.0) * 4) * $s)))
        $w2.Flush()
    }
    $bmp.Dispose()
    $entries += , @($s, $ms.ToArray())
}
$fs = [IO.File]::Create("$Out.ico"); $bw = New-Object IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $dim = if ($e[0] -ge 256) { 0 } else { $e[0] }
    $bw.Write([Byte]$dim); $bw.Write([Byte]$dim); $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$e[1].Length); $bw.Write([UInt32]$offset)
    $offset += $e[1].Length
}
foreach ($e in $entries) { $bw.Write($e[1]) }
$bw.Close()
"icon written: $Out.ico ($Style)"
