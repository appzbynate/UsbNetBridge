# Makes near-black pixels transparent and crops to logo content.
Add-Type -AssemblyName System.Drawing

$inPath = "C:\Users\Nate\Projects\UsbNetBridge\branding\title-source.png"
$outPath = "C:\Users\Nate\Projects\UsbNetBridge\branding\title-logo.png"
$androidOut = "C:\Users\Nate\Projects\UsbNetBridge\android\app\src\main\res\drawable\title_logo.png"
$windowsOut = "C:\Users\Nate\Projects\UsbNetBridge\windows\UsbNetBridge.Client\title-logo.png"

$src = New-Object System.Drawing.Bitmap $inPath
$bmp = New-Object System.Drawing.Bitmap $src.Width, $src.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

$minX = $src.Width; $minY = $src.Height; $maxX = -1; $maxY = -1
$hard = 18   # fully transparent below this luminance
$soft = 48   # fade between hard..soft

for ($y = 0; $y -lt $src.Height; $y++) {
  for ($x = 0; $x -lt $src.Width; $x++) {
    $c = $src.GetPixel($x, $y)
    $lum = [int]((0.2126 * $c.R) + (0.7152 * $c.G) + (0.0722 * $c.B))
    # Also treat very dark navy fringe as background if all channels low
    $maxCh = [Math]::Max($c.R, [Math]::Max($c.G, $c.B))

    $a = 255
    if ($maxCh -le $hard -and $lum -le $hard) {
      $a = 0
    } elseif ($maxCh -le $soft) {
      $a = [int](255.0 * ($maxCh - $hard) / ($soft - $hard))
      if ($a -lt 0) { $a = 0 }
      if ($a -gt 255) { $a = 255 }
    }

    if ($a -gt 8) {
      if ($x -lt $minX) { $minX = $x }
      if ($y -lt $minY) { $minY = $y }
      if ($x -gt $maxX) { $maxX = $x }
      if ($y -gt $maxY) { $maxY = $y }
    }

    $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($a, $c.R, $c.G, $c.B))
  }
}

$pad = 6
$minX = [Math]::Max(0, $minX - $pad)
$minY = [Math]::Max(0, $minY - $pad)
$maxX = [Math]::Min($src.Width - 1, $maxX + $pad)
$maxY = [Math]::Min($src.Height - 1, $maxY + $pad)
$rect = [System.Drawing.Rectangle]::new($minX, $minY, $maxX - $minX + 1, $maxY - $minY + 1)
Write-Host "Crop bounds: $rect"

$cropped = $bmp.Clone($rect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$cropped.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)

New-Item -ItemType Directory -Force -Path (Split-Path $androidOut) | Out-Null
Copy-Item $outPath $androidOut -Force
Copy-Item $outPath $windowsOut -Force

Write-Host "Wrote $outPath ($((Get-Item $outPath).Length) bytes)  $($cropped.Width)x$($cropped.Height)"
$cropped.Dispose(); $bmp.Dispose(); $src.Dispose()
