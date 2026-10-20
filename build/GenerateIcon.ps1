param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\src\MegaSnip\Assets\MegaSnip.ico")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-RoundedRectPath {
    param(
        [System.Drawing.RectangleF]$Rect,
        [float]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $arc = [System.Drawing.RectangleF]::new($Rect.X, $Rect.Y, $diameter, $diameter)
    $path.AddArc($arc, 180, 90)
    $arc.X = $Rect.Right - $diameter
    $path.AddArc($arc, 270, 90)
    $arc.Y = $Rect.Bottom - $diameter
    $path.AddArc($arc, 0, 90)
    $arc.X = $Rect.X
    $path.AddArc($arc, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $inset = [float][Math]::Max(0.5, $Size * 0.02)
        $rect = [System.Drawing.RectangleF]::new(
            $inset,
            $inset,
            $Size - ($inset * 2),
            $Size - ($inset * 2))
        $radius = [float]($Size * 0.22)
        $backgroundPath = New-RoundedRectPath -Rect $rect -Radius $radius

        try {
            $startColor = [System.Drawing.Color]::FromArgb(255, 28, 102, 214)
            $endColor = [System.Drawing.Color]::FromArgb(255, 8, 55, 128)
            $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
                $rect,
                $startColor,
                $endColor,
                45.0)
            $graphics.FillPath($brush, $backgroundPath)
            $brush.Dispose()
        }
        finally {
            $backgroundPath.Dispose()
        }

        $margin = [float]($Size * 0.22)
        $left = $margin
        $top = $margin
        $right = $Size - $margin
        $bottom = $Size - $margin
        $penWidth = [float][Math]::Max(1.0, $Size * 0.055)
        $pen = [System.Drawing.Pen]::new(
            [System.Drawing.Color]::FromArgb(255, 255, 255, 255),
            $penWidth)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Square
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Square
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Miter

        try {
            $corner = [float]($Size * 0.20)
            $graphics.DrawLine($pen, $left, $top + $corner, $left, $top)
            $graphics.DrawLine($pen, $left, $top, $left + $corner, $top)
            $graphics.DrawLine($pen, $right - $corner, $top, $right, $top)
            $graphics.DrawLine($pen, $right, $top, $right, $top + $corner)
            $graphics.DrawLine($pen, $left, $bottom - $corner, $left, $bottom)
            $graphics.DrawLine($pen, $left, $bottom, $left + $corner, $bottom)
            $graphics.DrawLine($pen, $right - $corner, $bottom, $right, $bottom)
            $graphics.DrawLine($pen, $right, $bottom - $corner, $right, $bottom)
        }
        finally {
            $pen.Dispose()
        }

        $lineColor = [System.Drawing.Color]::FromArgb(238, 255, 255, 255)
        $lineBrush = [System.Drawing.SolidBrush]::new($lineColor)
        try {
            $lineHeight = [float][Math]::Max(1.0, $Size * 0.055)
            $lineGap = [float][Math]::Max(1.0, $Size * 0.075)
            $lineLeft = [float]($Size * 0.36)
            $lineTop = [float]($Size * 0.38)
            $widths = @(0.30, 0.22, 0.26)

            foreach ($widthFactor in $widths) {
                $lineWidth = [float]($Size * $widthFactor)
                $graphics.FillRectangle(
                    $lineBrush,
                    $lineLeft,
                    $lineTop,
                    $lineWidth,
                    $lineHeight)
                $lineTop += $lineHeight + $lineGap
            }
        }
        finally {
            $lineBrush.Dispose()
        }

        $stream = [System.IO.MemoryStream]::new()
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = foreach ($size in $sizes) {
    [PSCustomObject]@{
        Size = $size
        Data = New-IconPng -Size $size
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$fileStream = [System.IO.File]::Open(
    $OutputPath,
    [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write)
$writer = [System.IO.BinaryWriter]::new($fileStream)

try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)

    $offset = 6 + (16 * $images.Count)
    foreach ($image in $images) {
        $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Data.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Data.Length
    }

    foreach ($image in $images) {
        $writer.Write($image.Data)
    }
}
finally {
    $writer.Dispose()
    $fileStream.Dispose()
}

Write-Host "Wrote $OutputPath"
