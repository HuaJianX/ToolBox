# Generates the application icon (src\ToolBox.App\app.ico).
#
# Design: rounded square with a blue gradient and a white two-way arrow ("swap" = convert).
# Written ASCII-only on purpose: a .ps1 with non-ASCII text gets mis-decoded as the system
# ANSI codepage on a Chinese Windows install and fails to parse.
#
# The ICO is assembled by hand: ICONDIR + ICONDIRENTRY[] + one uncompressed 32bpp DIB per size.
# No System.Drawing needed, so this works on any PowerShell 7 install.

[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

if (-not $OutputPath) {
    $OutputPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\ToolBox.App\app.ico'
}

# --- geometry helpers -------------------------------------------------------

# Inside a rounded rectangle spanning (0,0)-(w,h) with corner radius r ?
function Test-RoundedRect([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    if ($x -lt 0 -or $y -lt 0 -or $x -gt $w -or $y -gt $h) { return $false }
    $cx = [Math]::Min([Math]::Max($x, $r), $w - $r)
    $cy = [Math]::Min([Math]::Max($y, $r), $h - $r)
    $dx = $x - $cx
    $dy = $y - $cy
    return (($dx * $dx) + ($dy * $dy)) -le ($r * $r)
}

# Inside a horizontal arrow whose shaft is centred on $centreY.
# $pointRight = false mirrors the shape, so the same math draws the return arrow.
function Test-Arrow([double]$x, [double]$y, [double]$w, [double]$h, [double]$centreY, [bool]$pointRight) {
    if (-not $pointRight) { $x = $w - $x }

    $half = 0.058 * $h
    if ($x -ge 0.20 * $w -and $x -le 0.60 * $w -and [Math]::Abs($y - $centreY) -le $half) { return $true }

    if ($x -gt 0.60 * $w -and $x -le 0.80 * $w) {
        $t = ($x - (0.60 * $w)) / (0.20 * $w)
        $taper = (1.0 - $t) * 0.125 * $h
        if ([Math]::Abs($y - $centreY) -le $taper) { return $true }
    }

    return $false
}

# --- one DIB image for a given edge length ----------------------------------

function New-IconImage([int]$size) {
    # Supersample small sizes harder, they need it more.
    $ss = if ($size -le 48) { 3 } else { 2 }
    $sampleCount = $ss * $ss

    $radius = 0.22 * $size
    $arrowUpperY = 0.355 * $size
    $arrowLowerY = 0.645 * $size

    $pixels = New-Object 'byte[]' ($size * $size * 4)

    for ($py = 0; $py -lt $size; $py++) {
        for ($px = 0; $px -lt $size; $px++) {
            $rSum = 0.0
            $gSum = 0.0
            $bSum = 0.0
            $covered = 0

            for ($sy = 0; $sy -lt $ss; $sy++) {
                $yy = $py + (($sy + 0.5) / $ss)
                for ($sx = 0; $sx -lt $ss; $sx++) {
                    $xx = $px + (($sx + 0.5) / $ss)

                    if (-not (Test-RoundedRect $xx $yy $size $size $radius)) { continue }
                    $covered++

                    if ((Test-Arrow $xx $yy $size $size $arrowUpperY $true) -or
                        (Test-Arrow $xx $yy $size $size $arrowLowerY $false)) {
                        $rSum += 255.0; $gSum += 255.0; $bSum += 255.0
                        continue
                    }

                    # vertical gradient: #2A6FE0 (top) -> #0D47A1 (bottom)
                    $t = $yy / $size
                    $rSum += 0x2A + ((0x0D - 0x2A) * $t)
                    $gSum += 0x6F + ((0x47 - 0x6F) * $t)
                    $bSum += 0xE0 + ((0xA1 - 0xE0) * $t)
                }
            }

            if ($covered -eq 0) { continue }

            $alpha = [int][Math]::Round(255.0 * $covered / $sampleCount)
            $red = [int][Math]::Round($rSum / $covered)
            $green = [int][Math]::Round($gSum / $covered)
            $blue = [int][Math]::Round($bSum / $covered)

            # DIB rows are stored bottom-up, and pixels are BGRA.
            $index = ((($size - 1 - $py) * $size) + $px) * 4
            $pixels[$index] = [byte]$blue
            $pixels[$index + 1] = [byte]$green
            $pixels[$index + 2] = [byte]$red
            $pixels[$index + 3] = [byte]$alpha
        }
    }

    # AND mask: 1bpp, rows padded to 4 bytes. All zero = "use the alpha channel".
    $maskStride = [int]([Math]::Floor(($size + 31) / 32) * 4)
    $mask = New-Object 'byte[]' ($maskStride * $size)

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)

    # BITMAPINFOHEADER (height is doubled to cover the XOR + AND masks)
    $writer.Write([uint32]40)
    $writer.Write([int32]$size)
    $writer.Write([int32]($size * 2))
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]0)
    $writer.Write([uint32]($size * $size * 4))
    $writer.Write([int32]0)
    $writer.Write([int32]0)
    $writer.Write([uint32]0)
    $writer.Write([uint32]0)
    $writer.Write($pixels)
    $writer.Write($mask)
    $writer.Flush()

    # Leading comma stops PowerShell from unrolling the byte array into the pipeline.
    return , $stream.ToArray()
}

# --- assemble the .ico ------------------------------------------------------

$sizes = @(16, 32, 48, 64, 128, 256)
$images = New-Object 'System.Collections.Generic.List[byte[]]'

foreach ($size in $sizes) {
    $images.Add((New-IconImage $size))
}

$directory = Split-Path $OutputPath -Parent
if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }

$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)

$writer.Write([uint16]0)                 # reserved
$writer.Write([uint16]1)                 # type: 1 = icon
$writer.Write([uint16]$images.Count)     # image count

$offset = 6 + (16 * $images.Count)
for ($i = 0; $i -lt $images.Count; $i++) {
    $size = $sizes[$i]
    $data = $images[$i]
    $dimension = if ($size -ge 256) { 0 } else { $size }   # 0 means 256

    $writer.Write([byte]$dimension)      # width
    $writer.Write([byte]$dimension)      # height
    $writer.Write([byte]0)               # palette colour count
    $writer.Write([byte]0)               # reserved
    $writer.Write([uint16]1)             # colour planes
    $writer.Write([uint16]32)            # bits per pixel
    $writer.Write([uint32]$data.Length)  # bytes of image data
    $writer.Write([uint32]$offset)       # file offset of image data
    $offset += $data.Length
}

foreach ($data in $images) { $writer.Write($data) }
$writer.Flush()

[System.IO.File]::WriteAllBytes($OutputPath, $stream.ToArray())

$file = Get-Item $OutputPath
Write-Host "Wrote $($file.FullName)"
Write-Host "  sizes : $($sizes -join ', ')"
Write-Host "  bytes : $($file.Length)"
