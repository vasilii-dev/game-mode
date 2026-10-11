param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$brandDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\Assets\Brand'))
$iconSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$artwork = @{ 'state-active' = 'enabled.png'; 'state-waiting' = 'waiting.png'; 'state-off' = 'off.png'; 'app' = 'Icon.png' }
foreach ($iconName in $artwork.Keys) {
    $source = [System.Drawing.Bitmap]::new((Join-Path $brandDirectory $artwork[$iconName]))
    $frames = [System.Collections.Generic.List[byte[]]]::new()
    try {
        foreach ($size in $iconSizes) {
            $resized = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $graphics = [System.Drawing.Graphics]::FromImage($resized)
            $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
            $stream = [IO.MemoryStream]::new()
            try {
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
                $rectangle = [System.Drawing.Rectangle]::new(0, 0, $size, $size)
                $graphics.DrawImage($source, $rectangle, 0, 0, $source.Width, $source.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
                $resized.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $frames.Add($stream.ToArray())
                if ($size -eq 256) { $resized.Save((Join-Path $OutputDirectory "$iconName.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
            } finally { $stream.Dispose(); $attributes.Dispose(); $graphics.Dispose(); $resized.Dispose() }
        }
        $file = [IO.File]::Create((Join-Path $OutputDirectory "$iconName.ico"))
        $writer = [IO.BinaryWriter]::new($file)
        try {
            $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$iconSizes.Count)
            $offset = 6 + 16 * $iconSizes.Count
            for ($index = 0; $index -lt $iconSizes.Count; $index++) {
                $dimension = if ($iconSizes[$index] -eq 256) { 0 } else { $iconSizes[$index] }
                $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
                $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
                $offset += $frames[$index].Length
            }
            foreach ($frame in $frames) { $writer.Write($frame) }
        } finally { $writer.Dispose(); $file.Dispose() }
    } finally { $source.Dispose() }
}
Write-Output 'Exported the supplied artwork into four Windows ICO files, 16–256 px. Original PNGs are unchanged.'
