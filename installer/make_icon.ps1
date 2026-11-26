# Generates assets\ClickBind.ico (16-256 px) from the same design the app draws at runtime.
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'assets'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$outFile = Join-Path $outDir 'ClickBind.ico'

function New-RoundPath([System.Drawing.RectangleF]$r, [float]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($radius * 2, [Math]::Min($r.Width, $r.Height))
    if ($d -le 0) { $d = 1 }
    $path.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $path.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $path.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$blobs = New-Object System.Collections.ArrayList

foreach ($s in $sizes) {
    $f = $s / 32.0
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $back = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
    $bp = New-RoundPath $back ([float](8 * $f))
    $bb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0, 79, 150))
    $g.FillPath($bb, $bp)

    $body = New-Object System.Drawing.RectangleF((9 * $f), (6 * $f), (14 * $f), (20 * $f))
    $mp = New-RoundPath $body ([float]($body.Width / 2))
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($white, $mp)

    $penWidth = [float][Math]::Max(1.0, 1.6 * $f)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 0, 79, 150), $penWidth)
    $g.DrawLine($pen, [float]($body.X + $body.Width / 2), [float]($body.Y + 1), [float]($body.X + $body.Width / 2), [float]($body.Y + $body.Height * 0.42))
    $g.DrawLine($pen, [float]($body.X + 1), [float]($body.Y + $body.Height * 0.42), [float]($body.Right - 1), [float]($body.Y + $body.Height * 0.42))

    $pen.Dispose()
    $white.Dispose()
    $mp.Dispose()
    $bb.Dispose()
    $bp.Dispose()
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    [void]$blobs.Add(@{ Size = $s; Bytes = $ms.ToArray() })
    $ms.Dispose()
}

$fs = [System.IO.File]::Create($outFile)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)              # reserved
$bw.Write([UInt16]1)              # type: icon
$bw.Write([UInt16]$blobs.Count)

$offset = 6 + (16 * $blobs.Count)
foreach ($b in $blobs) {
    $dim = if ($b.Size -ge 256) { 0 } else { $b.Size }
    $bw.Write([Byte]$dim)         # width
    $bw.Write([Byte]$dim)         # height
    $bw.Write([Byte]0)            # palette
    $bw.Write([Byte]0)            # reserved
    $bw.Write([UInt16]1)          # planes
    $bw.Write([UInt16]32)         # bpp
    $bw.Write([UInt32]$b.Bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $b.Bytes.Length
}
foreach ($b in $blobs) { $bw.Write($b.Bytes) }
$bw.Flush()
$bw.Close()
$fs.Close()

Write-Output "Wrote $outFile"
