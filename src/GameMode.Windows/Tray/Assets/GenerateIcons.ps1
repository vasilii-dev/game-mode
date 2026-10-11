param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
foreach ($state in @('active', 'waiting', 'off')) {
    $frames = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($size in $iconSizes) {
        $scale = 4
        $canvasSize = $size * $scale
        $bitmap = [System.Drawing.Bitmap]::new($canvasSize, $canvasSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $unit = $canvasSize / 64.0
        $primary = if ($state -eq 'active') { [System.Drawing.Color]::FromArgb(92, 139, 255) } elseif ($state -eq 'off') { [System.Drawing.Color]::FromArgb(218, 105, 128) } else { [System.Drawing.Color]::FromArgb(130, 142, 165) }
        $secondary = if ($state -eq 'active') { [System.Drawing.Color]::FromArgb(155, 76, 248) } elseif ($state -eq 'off') { [System.Drawing.Color]::FromArgb(148, 68, 93) } else { [System.Drawing.Color]::FromArgb(78, 88, 111) }
        if ($state -eq 'active') {
            for ($ring = 10; $ring -ge 1; $ring--) {
                $glow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(5 + [int](8 * (10 - $ring) / 10), 125, 102, 255))
                $graphics.FillEllipse($glow, [single]((10 - $ring / 2) * $unit), [single]((10 - $ring / 2) * $unit), [single]((44 + $ring) * $unit), [single]((44 + $ring) * $unit))
                $glow.Dispose()
            }
        }
        $rectangle = [System.Drawing.RectangleF]::new([single](8 * $unit), [single](8 * $unit), [single](48 * $unit), [single](48 * $unit))
        $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new($rectangle, $primary, $secondary, [single]45)
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $radius = [single](17 * $unit)
        $path.AddArc($rectangle.Left, $rectangle.Top, $radius, $radius, 180, 90)
        $path.AddArc($rectangle.Right - $radius, $rectangle.Top, $radius, $radius, 270, 90)
        $path.AddArc($rectangle.Right - $radius, $rectangle.Bottom - $radius, $radius, $radius, 0, 90)
        $path.AddArc($rectangle.Left, $rectangle.Bottom - $radius, $radius, $radius, 90, 90)
        $path.CloseFigure()
        $graphics.FillPath($gradient, $path)
        $outline = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(90, 232, 238, 255), [single]($unit))
        $graphics.DrawPath($outline, $path)
        $power = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(250, 247, 248, 255), [single](4.2 * $unit))
        $power.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $power.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawArc($power, [single](19 * $unit), [single](20 * $unit), [single](26 * $unit), [single](26 * $unit), [single](-48), [single]276)
        $graphics.DrawLine($power, [single](32 * $unit), [single](18 * $unit), [single](32 * $unit), [single](31 * $unit))
        if ($state -eq 'off') {
            $slashShadow = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 62, 27, 44), [single](7 * $unit))
            $slash = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 255, 195, 202), [single](3.6 * $unit))
            $slashShadow.StartCap = $slashShadow.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $slash.StartCap = $slash.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $graphics.DrawLine($slashShadow, [single](17 * $unit), [single](47 * $unit), [single](48 * $unit), [single](16 * $unit))
            $graphics.DrawLine($slash, [single](17 * $unit), [single](47 * $unit), [single](48 * $unit), [single](16 * $unit))
            $slashShadow.Dispose(); $slash.Dispose()
        }
        $resized = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $destination = [System.Drawing.Graphics]::FromImage($resized)
        $destination.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $destination.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $destination.DrawImage($bitmap, 0, 0, $size, $size)
        $stream = [System.IO.MemoryStream]::new()
        $resized.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames.Add($stream.ToArray())
        if ($size -eq 256) { $resized.Save((Join-Path $OutputDirectory "state-$state.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
        $stream.Dispose(); $destination.Dispose(); $resized.Dispose(); $power.Dispose(); $outline.Dispose(); $path.Dispose(); $gradient.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    }
    $file = [System.IO.File]::Create((Join-Path $OutputDirectory "state-$state.ico"))
    $writer = [System.IO.BinaryWriter]::new($file)
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$iconSizes.Count)
    $offset = 6 + 16 * $iconSizes.Count
    for ($index = 0; $index -lt $iconSizes.Count; $index++) {
        $dimension = if ($iconSizes[$index] -eq 256) { 0 } else { $iconSizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
    $writer.Dispose(); $file.Dispose()
}
Write-Output 'Generated three multi-resolution icon sets (16–256 px).'
