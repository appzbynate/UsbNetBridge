# Regenerates Google Play visual assets from branding + in-app copy/art.
# Run: powershell -File docs/play-assets/build-play-assets.ps1

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$Out = $PSScriptRoot
$ShotDir = Join-Path $Out "screenshots"
$VideoDir = Join-Path $Out "video"
New-Item -ItemType Directory -Force -Path $ShotDir, $VideoDir | Out-Null

function Load-Bmp([string]$Rel) {
    $path = Join-Path $Root $Rel
    $img = [System.Drawing.Image]::FromFile($path)
    $clone = New-Object System.Drawing.Bitmap $img
    $img.Dispose()
    return $clone
}

function New-Bmp([int]$W, [int]$H, [System.Drawing.Imaging.PixelFormat]$Fmt) {
    $b = New-Object System.Drawing.Bitmap $W, $H, $Fmt
    $b.SetResolution(72, 72)
    return $b
}

function Get-G([System.Drawing.Bitmap]$Bmp) {
    $g = [System.Drawing.Graphics]::FromImage($Bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    return $g
}

function Hex([string]$H) { [System.Drawing.ColorTranslator]::FromHtml($H) }

function RoundPath([float]$X, [float]$Y, [float]$W, [float]$H, [float]$R) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($R * 2, [Math]::Min($W, $H))
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Fill-Round([System.Drawing.Graphics]$G, [System.Drawing.Brush]$Brush, [float]$X, [float]$Y, [float]$W, [float]$H, [float]$R) {
    $p = RoundPath $X $Y $W $H $R
    $G.FillPath($Brush, $p)
    $p.Dispose()
}

function Stroke-Round([System.Drawing.Graphics]$G, [System.Drawing.Pen]$Pen, [float]$X, [float]$Y, [float]$W, [float]$H, [float]$R) {
    $p = RoundPath $X $Y $W $H $R
    $G.DrawPath($Pen, $p)
    $p.Dispose()
}

function Cover([System.Drawing.Graphics]$G, [System.Drawing.Image]$Img, [int]$Dw, [int]$Dh) {
    $scale = [Math]::Max($Dw / [double]$Img.Width, $Dh / [double]$Img.Height)
    $sw = [Math]::Min($Img.Width, [int][Math]::Round($Dw / $scale))
    $sh = [Math]::Min($Img.Height, [int][Math]::Round($Dh / $scale))
    $sx = [Math]::Max(0, [int](($Img.Width - $sw) / 2))
    $sy = [Math]::Max(0, [int](($Img.Height - $sh) / 2))
    $dest = New-Object System.Drawing.Rectangle 0, 0, $Dw, $Dh
    $src = New-Object System.Drawing.Rectangle $sx, $sy, $sw, $sh
    $G.DrawImage($Img, $dest, $src, [System.Drawing.GraphicsUnit]::Pixel)
}

function Fit([System.Drawing.Graphics]$G, [System.Drawing.Image]$Img, [float]$X, [float]$Y, [float]$MaxW, [float]$MaxH) {
    $scale = [Math]::Min($MaxW / [double]$Img.Width, $MaxH / [double]$Img.Height)
    $w = [float]($Img.Width * $scale)
    $h = [float]($Img.Height * $scale)
    $G.DrawImage($Img, $X, $Y, $w, $h)
    return $h
}

function CenterText([System.Drawing.Graphics]$G, [string]$Text, [System.Drawing.Font]$Font, [System.Drawing.Brush]$Brush, [float]$X, [float]$Y, [float]$W, [float]$H) {
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $G.DrawString($Text, $Font, $Brush, (New-Object System.Drawing.RectangleF $X, $Y, $W, $H), $sf)
    $sf.Dispose()
}

function WrapText([System.Drawing.Graphics]$G, [string]$Text, [System.Drawing.Font]$Font, [System.Drawing.Brush]$Brush, [float]$X, [float]$Y, [float]$W, [float]$MaxH) {
    $sf = New-Object System.Drawing.StringFormat
    $sf.Trimming = [System.Drawing.StringTrimming]::EllipsisWord
    $rect = New-Object System.Drawing.RectangleF $X, $Y, $W, $MaxH
    $G.DrawString($Text, $Font, $Brush, $rect, $sf)
    $size = $G.MeasureString($Text, $Font, [int]$W)
    $sf.Dispose()
    return [Math]::Min($size.Height, $MaxH)
}

# --- 512x512 high-res icon (Play: 32-bit PNG) ---
$iconSrc = Load-Bmp "branding\app-icon-source.png"
$side = [Math]::Min($iconSrc.Width, $iconSrc.Height)
$cx = [int](($iconSrc.Width - $side) / 2)
$cy = [int](($iconSrc.Height - $side) / 2)
$cropped = $iconSrc.Clone((New-Object System.Drawing.Rectangle $cx, $cy, $side, $side), $iconSrc.PixelFormat)
$cropped.MakeTransparent([System.Drawing.Color]::Black)
$icon = New-Bmp 512 512 ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$ig = Get-G $icon
$ig.Clear([System.Drawing.Color]::Transparent)
$ig.DrawImage($cropped, 0, 0, 512, 512)
$ig.Dispose()
$iconPath = Join-Path $Out "icon-512.png"
$icon.Save($iconPath, [System.Drawing.Imaging.ImageFormat]::Png)
$icon.Dispose(); $cropped.Dispose(); $iconSrc.Dispose()

# --- Feature graphic 1024x500 (no alpha) ---
$banner = Load-Bmp "windows\UsbNetBridge.Client\connecting-banner.png"
$logo = Load-Bmp "android\app\src\main\res\drawable\title_logo.png"
$iconSmall = [System.Drawing.Image]::FromFile($iconPath)
$feat = New-Bmp 1024 500 ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$fg = Get-G $feat
Cover $fg $banner 1024 500
$veil = New-Object System.Drawing.SolidBrush (Hex "#C80C1630")
$fg.FillRectangle($veil, 0, 0, 620, 500)
$veil.Dispose()
[void](Fit $fg $logo 36 88 540 110)
$tagFont = New-Object System.Drawing.Font "Segoe UI Semibold", 18, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$tagBrush = New-Object System.Drawing.SolidBrush (Hex "#F5FAFF")
[void](WrapText $fg "Share a USB mouse or controller from this device to your Windows PC." $tagFont $tagBrush 40 220 540 90)
$subFont = New-Object System.Drawing.Font "Segoe UI", 15, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$subBrush = New-Object System.Drawing.SolidBrush (Hex "#38D2E8")
$fg.DrawString("Same Wi-Fi, hotspot, or VPN.", $subFont, $subBrush, 40, 320)
$fg.DrawImage($iconSmall, 760, 110, 220, 220)
$feat.Save((Join-Path $Out "feature-graphic-1024x500.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$fg.Dispose(); $feat.Dispose(); $tagFont.Dispose(); $tagBrush.Dispose(); $subFont.Dispose(); $subBrush.Dispose()
$banner.Dispose(); $logo.Dispose(); $iconSmall.Dispose()

# Phone/tablet screenshots come from annotate-screenshots.ps1 (real captures).
# --- Title card + promo videos from those shots ---

$p1 = Join-Path $ShotDir "phone-01-android.png"
$p2 = Join-Path $ShotDir "phone-02-windows.png"
# --- Title card for video ---
$banner2 = Load-Bmp "windows\UsbNetBridge.Client\connecting-banner.png"
$logo2 = Load-Bmp "android\app\src\main\res\drawable\title_logo.png"
$title = New-Bmp 1920 1080 ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$tg = Get-G $title
Cover $tg $banner2 1920 1080
$dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 12, 22, 48))
$tg.FillRectangle($dark, 0, 0, 1920, 1080)
$dark.Dispose()
[void](Fit $tg $logo2 160 380 1100 180)
$tf = New-Object System.Drawing.Font "Segoe UI Semibold", 36, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$tb = New-Object System.Drawing.SolidBrush (Hex "#F5FAFF")
$tg.DrawString("Share a USB mouse or controller to your Windows PC.", $tf, $tb, 168, 600)
$tf.Dispose(); $tb.Dispose()
$titlePath = Join-Path $VideoDir "title-card-16x9.png"
$title.Save($titlePath, [System.Drawing.Imaging.ImageFormat]::Png)
$tg.Dispose(); $title.Dispose(); $banner2.Dispose(); $logo2.Dispose()

Write-Host "Images written. Encoding videos..."

Get-Command ffmpeg -ErrorAction Stop | Out-Null
$p1 = Join-Path $ShotDir "phone-01-android.png"
$p2 = Join-Path $ShotDir "phone-02-windows.png"
$featP = Join-Path $Out "feature-graphic-1024x500.png"
$v16 = Join-Path $VideoDir "promo-16x9.mp4"
$v9 = Join-Path $VideoDir "promo-9x16.mp4"

$pad16 = "scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=0x0C1630,setsar=1,fps=30,format=yuv420p"
$fade = "fade=t=in:st=0:d=0.4,fade=t=out:st=3.5:d=0.45"

& ffmpeg -y -hide_banner -loglevel error `
    -loop 1 -t 4 -i $titlePath `
    -loop 1 -t 4 -i $featP `
    -loop 1 -t 4 -i $p1 `
    -loop 1 -t 4 -i $p2 `
    -filter_complex "[0]$pad16,$fade[v0];[1]$pad16,$fade[v1];[2]$pad16,$fade[v2];[3]$pad16,$fade[v3];[v0][v1][v2][v3]concat=n=4:v=1:a=0[v]" `
    -map "[v]" -c:v libx264 -crf 22 -preset medium -pix_fmt yuv420p -movflags +faststart $v16
if ($LASTEXITCODE -ne 0) { throw "ffmpeg 16:9 failed" }

$pad9 = "scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2:color=0x0C1630,setsar=1,fps=30,format=yuv420p"
$fade9 = "fade=t=in:st=0:d=0.35,fade=t=out:st=3.05:d=0.4"
& ffmpeg -y -hide_banner -loglevel error `
    -loop 1 -t 3.5 -i $p1 `
    -loop 1 -t 3.5 -i $p2 `
    -filter_complex "[0]$pad9,$fade9[v0];[1]$pad9,$fade9[v1];[v0][v1]concat=n=2:v=1:a=0[v]" `
    -map "[v]" -c:v libx264 -crf 22 -preset medium -pix_fmt yuv420p -movflags +faststart $v9
if ($LASTEXITCODE -ne 0) { throw "ffmpeg 9:16 failed" }

Write-Host "Done."
Get-ChildItem $Out, $ShotDir, $VideoDir -File | ForEach-Object { "{0,10}  {1}" -f $_.Length, $_.FullName }

