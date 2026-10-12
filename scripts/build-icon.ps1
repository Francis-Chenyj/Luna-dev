$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectPath = Split-Path -Parent $PSScriptRoot
$frames = @()
# Match native Windows DPI sizes; antialias only the rounded background.
foreach ($size in @(16,20,24,30,32,36,40,48,60,64,72,96,128,256)) {
    $bitmap = [System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $canvas = [System.Drawing.Graphics]::FromImage($bitmap)
    $canvas.Clear([System.Drawing.Color]::Transparent)
    $canvas.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $diameter = [single]($size * 28.0 / 48)
    $edge = [single]($size - $diameter)
    $rounded = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $rounded.AddArc(0,0,$diameter,$diameter,180,90)
    $rounded.AddArc($edge,0,$diameter,$diameter,270,90)
    $rounded.AddArc($edge,$edge,$diameter,$diameter,0,90)
    $rounded.AddArc(0,$edge,$diameter,$diameter,90,90)
    $rounded.CloseFigure()
    $green = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#bdff62'))
    $canvas.FillPath($green,$rounded)
    # No resampling: small L strokes land on whole pixels, without a blur fringe.
    $canvas.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $x = [int][Math]::Round($size * 15.0 / 48)
    $y = [int][Math]::Round($size * 11.0 / 48)
    $right = [int][Math]::Round($size * 35.0 / 48)
    $bottom = [int][Math]::Round($size * 37.0 / 48)
    $stem = [int][Math]::Round($size * 8.0 / 48)
    $bar = [int][Math]::Round($size * 7.0 / 48)
    $canvas.FillRectangle([System.Drawing.Brushes]::Black,$x,$y,$stem,($bottom-$y))
    $canvas.FillRectangle([System.Drawing.Brushes]::Black,$x,($bottom-$bar),($right-$x),$bar)
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    if ($size -eq 32 -or $size -eq 48 -or $size -eq 256) {
        [IO.File]::WriteAllBytes((Join-Path $projectPath "web\app-$size.png"),$stream.ToArray())
    }
    $frames += ,@{Size=$size;Bytes=$stream.ToArray()}
    $stream.Dispose();$green.Dispose();$rounded.Dispose();$canvas.Dispose();$bitmap.Dispose()
}
$iconPath = Join-Path $projectPath 'web\app.ico'
$file = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if($frame.Size -eq 256){0}else{$frame.Size}
        $writer.Write([byte]$dimension);$writer.Write([byte]$dimension)
        $writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length);$writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose();$file.Dispose() }
# Invalidate only Luna's shortcut/taskbar resource, not the global Windows cache.
Copy-Item -LiteralPath $iconPath -Destination (Join-Path $projectPath 'web\app-1.4.1.ico') -Force
Write-Output 'Built pixel-aligned green L icons (14 native sizes, 16-256 px).'
