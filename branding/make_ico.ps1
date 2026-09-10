Add-Type -AssemblyName System.Drawing

$srcPath = "C:\Users\Nate\Projects\UsbNetBridge\branding\app-icon-source.png"
$icoPath = "C:\Users\Nate\Projects\UsbNetBridge\windows\UsbNetBridge.Client\app.ico"
$src = [System.Drawing.Image]::FromFile($srcPath)

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = New-Object System.Collections.Generic.List[object]

foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear([System.Drawing.Color]::Transparent)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
  $g.DrawImage($src, 0, 0, $s, $s)
  $g.Dispose()
  $pngMs = New-Object System.IO.MemoryStream
  $bmp.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  $images.Add([pscustomobject]@{ Size = $s; Bytes = $pngMs.ToArray() }) | Out-Null
  $pngMs.Dispose()
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$images.Count)

$offset = 6 + (16 * $images.Count)
foreach ($img in $images) {
  $s = [int]$img.Size
  $w = 0
  $h = 0
  if ($s -lt 256) { $w = $s; $h = $s }
  $bw.Write([byte]$w)
  $bw.Write([byte]$h)
  $bw.Write([byte]0)
  $bw.Write([byte]0)
  $bw.Write([uint16]1)
  $bw.Write([uint16]32)
  $bw.Write([uint32]$img.Bytes.Length)
  $bw.Write([uint32]$offset)
  $offset += $img.Bytes.Length
}
foreach ($img in $images) {
  $bw.Write([byte[]]$img.Bytes)
}
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose()
$ms.Dispose()
$src.Dispose()

Write-Host "Wrote $icoPath ($((Get-Item $icoPath).Length) bytes)"
