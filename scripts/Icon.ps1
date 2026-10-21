# Draws the Sidetone Control for HyperX artwork (headset outline with sound waves on a dark rounded square).
Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size, [bool]$rounded = $true) {
    $bmp = New-Object Drawing.Bitmap $size, $size
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    Draw-Glyph $g $size 0 0 $rounded
    $g.Dispose()
    return $bmp
}

function Draw-Glyph($g, [float]$size, [float]$ox, [float]$oy, [bool]$background) {
    $k = $size / 64.0
    if ($background) {
        $bg = New-Object Drawing.Drawing2D.GraphicsPath
        $r = 14 * $k; $w = $size - 1
        $bg.AddArc($ox, $oy, 2 * $r, 2 * $r, 180, 90); $bg.AddArc($ox + $w - 2 * $r, $oy, 2 * $r, 2 * $r, 270, 90)
        $bg.AddArc($ox + $w - 2 * $r, $oy + $w - 2 * $r, 2 * $r, 2 * $r, 0, 90); $bg.AddArc($ox, $oy + $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
        $bg.CloseFigure()
        $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 28, 30, 36))), $bg)
    }
    $pen = New-Object Drawing.Pen ([Drawing.Color]::White), ([Math]::Max(1.5, 5 * $k))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawArc($pen, $ox + 12 * $k, $oy + 12 * $k, 40 * $k, 40 * $k, 180, 180)
    $red = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 226, 35, 45))
    $g.FillRectangle($red, $ox + 9 * $k, $oy + 30 * $k, 10 * $k, 18 * $k)
    $g.FillRectangle($red, $ox + 45 * $k, $oy + 30 * $k, 10 * $k, 18 * $k)
    if ($size -ge 32) {
        $wave = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(200, 255, 255, 255)), ([Math]::Max(1, 3 * $k))
        $wave.StartCap = 'Round'; $wave.EndCap = 'Round'
        $g.DrawArc($wave, $ox + 24 * $k, $oy + 32 * $k, 16 * $k, 16 * $k, 300, 120)
        $g.DrawArc($wave, $ox + 24 * $k, $oy + 32 * $k, 16 * $k, 16 * $k, 120, 120)
    }
}

function Save-Png($bmp, [string]$path) {
    $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# Multi-size .ico with PNG-compressed entries.
function Save-Ico([string]$path) {
    $sizes = 16, 24, 32, 48, 64, 256
    $pngs = foreach ($s in $sizes) {
        $bmp = New-IconBitmap $s
        $ms = New-Object IO.MemoryStream
        $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        , $ms.ToArray()
    }
    $out = New-Object IO.MemoryStream
    $bw = New-Object IO.BinaryWriter $out
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write([byte[]]$p) }
    [IO.File]::WriteAllBytes($path, $out.ToArray())
}
