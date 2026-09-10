# Regenerates Windows ICO + Android mipmaps from branding/app-icon-source.png
# Crops black letterboxing, fits into a square without stretching, and flood-fills
# outer near-black to transparent so rounded corners are actually transparent.

Add-Type -AssemblyName System.Drawing

$srcPath = "C:\Users\Nate\Projects\UsbNetBridge\branding\app-icon-source.png"
$icoPath = "C:\Users\Nate\Projects\UsbNetBridge\windows\UsbNetBridge.Client\app.ico"
$androidRes = "C:\Users\Nate\Projects\UsbNetBridge\android\app\src\main\res"

function Test-NearBlack([System.Drawing.Color]$c, [int]$threshold = 22) {
  if ($c.A -lt 16) { return $true }
  return ($c.R -le $threshold -and $c.G -le $threshold -and $c.B -le $threshold)
}

function Get-ContentBounds([System.Drawing.Bitmap]$bmp) {
  $minX = $bmp.Width; $minY = $bmp.Height; $maxX = -1; $maxY = -1
  for ($y = 0; $y -lt $bmp.Height; $y++) {
    for ($x = 0; $x -lt $bmp.Width; $x++) {
      $c = $bmp.GetPixel($x, $y)
      if (-not (Test-NearBlack $c)) {
        if ($x -lt $minX) { $minX = $x }
        if ($y -lt $minY) { $minY = $y }
        if ($x -gt $maxX) { $maxX = $x }
        if ($y -gt $maxY) { $maxY = $y }
      }
    }
  }
  if ($maxX -lt 0) {
    return [System.Drawing.Rectangle]::new(0, 0, $bmp.Width, $bmp.Height)
  }
  $pad = 4
  $minX = [Math]::Max(0, $minX - $pad)
  $minY = [Math]::Max(0, $minY - $pad)
  $maxX = [Math]::Min($bmp.Width - 1, $maxX + $pad)
  $maxY = [Math]::Min($bmp.Height - 1, $maxY + $pad)
  return [System.Drawing.Rectangle]::new($minX, $minY, $maxX - $minX + 1, $maxY - $minY + 1)
}

function Clear-OuterBlackToTransparent([System.Drawing.Bitmap]$bmp, [int]$threshold = 28) {
  # Flood-fill from every edge pixel so only the exterior black (outside the
  # chrome frame) becomes transparent — dark art inside the frame stays opaque.
  $w = $bmp.Width
  $h = $bmp.Height
  $visited = New-Object 'bool[,]' $w, $h
  $queue = [System.Collections.Generic.Queue[int]]::new()

  function Enqueue-Bg([int]$x, [int]$y) {
    if ($x -lt 0 -or $y -lt 0 -or $x -ge $w -or $y -ge $h) { return }
    if ($visited[$x, $y]) { return }
    $c = $bmp.GetPixel($x, $y)
    if (-not (Test-NearBlack $c $threshold)) { return }
    $visited[$x, $y] = $true
    $queue.Enqueue(($y * $w) + $x)
  }

  for ($x = 0; $x -lt $w; $x++) {
    Enqueue-Bg $x 0
    Enqueue-Bg $x ($h - 1)
  }
  for ($y = 0; $y -lt $h; $y++) {
    Enqueue-Bg 0 $y
    Enqueue-Bg ($w - 1) $y
  }

  $cleared = 0
  $transparent = [System.Drawing.Color]::Transparent
  while ($queue.Count -gt 0) {
    $i = $queue.Dequeue()
    $x = $i % $w
    $y = [int][Math]::Floor($i / $w)
    $bmp.SetPixel($x, $y, $transparent)
    $cleared++
    Enqueue-Bg ($x + 1) $y
    Enqueue-Bg ($x - 1) $y
    Enqueue-Bg $x ($y + 1)
    Enqueue-Bg $x ($y - 1)
  }

  # Soften anti-aliased black fringe next to newly transparent pixels
  $fringe = New-Object System.Collections.Generic.List[int]
  for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
      $c = $bmp.GetPixel($x, $y)
      if ($c.A -eq 0) { continue }
      if ($c.R -gt 40 -or $c.G -gt 40 -or $c.B -gt 40) { continue }
      $nearClear = $false
      foreach ($d in @(@(1,0),@(-1,0),@(0,1),@(0,-1))) {
        $nx = $x + $d[0]; $ny = $y + $d[1]
        if ($nx -lt 0 -or $ny -lt 0 -or $nx -ge $w -or $ny -ge $h) { continue }
        if ($bmp.GetPixel($nx, $ny).A -eq 0) { $nearClear = $true; break }
      }
      if ($nearClear) { $fringe.Add(($y * $w) + $x) }
    }
  }
  foreach ($i in $fringe) {
    $bmp.SetPixel(($i % $w), ([int][Math]::Floor($i / $w)), $transparent)
    $cleared++
  }

  Write-Host "Cleared $cleared outer pixels to transparent"
}

function New-SquareIcon([System.Drawing.Image]$src, [int]$size) {
  $bmpSrc = New-Object System.Drawing.Bitmap $src
  $bounds = Get-ContentBounds $bmpSrc
  Write-Host "Content bounds: $bounds (from $($src.Width)x$($src.Height))"

  $cropped = $bmpSrc.Clone($bounds, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $bmpSrc.Dispose()

  $side = [Math]::Max($cropped.Width, $cropped.Height)
  $square = New-Object System.Drawing.Bitmap $side, $side, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g0 = [System.Drawing.Graphics]::FromImage($square)
  $g0.Clear([System.Drawing.Color]::Transparent)
  $ox = [int](($side - $cropped.Width) / 2)
  $oy = [int](($side - $cropped.Height) / 2)
  $g0.DrawImage($cropped, $ox, $oy, $cropped.Width, $cropped.Height)
  $g0.Dispose()
  $cropped.Dispose()

  Clear-OuterBlackToTransparent $square

  $out = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($out)
  $g.Clear([System.Drawing.Color]::Transparent)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
  $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
  $g.DrawImage($square, 0, 0, $size, $size)
  $g.Dispose()
  $square.Dispose()
  return $out
}

function Save-Png([System.Drawing.Bitmap]$bmp, [string]$path) {
  New-Item -ItemType Directory -Force -Path (Split-Path $path -Parent) | Out-Null
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

$src = [System.Drawing.Image]::FromFile($srcPath)
Write-Host "Source: $($src.Width)x$($src.Height)"

$master = New-SquareIcon $src 512
Save-Png $master "$androidRes\drawable\ic_launcher.png"
Save-Png $master "$androidRes\drawable\ic_launcher_foreground.png"

foreach ($pair in @(
  @{D='mipmap-mdpi'; S=48},
  @{D='mipmap-hdpi'; S=72},
  @{D='mipmap-xhdpi'; S=96},
  @{D='mipmap-xxhdpi'; S=144},
  @{D='mipmap-xxxhdpi'; S=192}
)) {
  $sized = New-Object System.Drawing.Bitmap $pair.S, $pair.S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($sized)
  $g.Clear([System.Drawing.Color]::Transparent)
  $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.DrawImage($master, 0, 0, $pair.S, $pair.S)
  $g.Dispose()
  $dir = Join-Path $androidRes $pair.D
  Save-Png $sized (Join-Path $dir "ic_launcher.png")
  Save-Png $sized (Join-Path $dir "ic_launcher_round.png")
  Save-Png $sized (Join-Path $dir "ic_launcher_foreground.png")
  $sized.Dispose()
}

# Adaptive icons with transparent plate so rounded corners show through
$anydpi = Join-Path $androidRes "mipmap-anydpi-v26"
New-Item -ItemType Directory -Force -Path $anydpi | Out-Null
$adaptiveXml = @'
<?xml version="1.0" encoding="utf-8"?>
<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
    <background android:drawable="@android:color/transparent"/>
    <foreground android:drawable="@drawable/ic_launcher_foreground"/>
</adaptive-icon>
'@
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $anydpi "ic_launcher.xml"), $adaptiveXml, $utf8NoBom)
[System.IO.File]::WriteAllText((Join-Path $anydpi "ic_launcher_round.xml"), $adaptiveXml, $utf8NoBom)

# Build ICO
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = New-Object System.Collections.Generic.List[object]
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear([System.Drawing.Color]::Transparent)
  $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.DrawImage($master, 0, 0, $s, $s)
  $g.Dispose()
  $pngMs = New-Object System.IO.MemoryStream
  $bmp.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  $images.Add([pscustomobject]@{ Size = $s; Bytes = $pngMs.ToArray() }) | Out-Null
  $pngMs.Dispose()
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$images.Count)
$offset = 6 + (16 * $images.Count)
foreach ($img in $images) {
  $s = [int]$img.Size
  $w = 0; $h = 0
  if ($s -lt 256) { $w = $s; $h = $s }
  $bw.Write([byte]$w); $bw.Write([byte]$h)
  $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([uint16]1); $bw.Write([uint16]32)
  $bw.Write([uint32]$img.Bytes.Length)
  $bw.Write([uint32]$offset)
  $offset += $img.Bytes.Length
}
foreach ($img in $images) { $bw.Write([byte[]]$img.Bytes) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()

$master.Dispose(); $src.Dispose()
Write-Host "Wrote $icoPath ($((Get-Item $icoPath).Length) bytes) and Android mipmaps with transparent corners."
