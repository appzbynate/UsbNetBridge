# Annotates the real Android + Windows captures for Play Console.
# Highlights the shared USB device (CX 2.4G Receiver) on both.

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$Out = $PSScriptRoot
$ShotDir = Join-Path $Out "screenshots"
$SrcDir = Join-Path $ShotDir "source"
New-Item -ItemType Directory -Force -Path $ShotDir, $SrcDir | Out-Null

$AndroidSrc = Join-Path $SrcDir "android-sharing.jpg"
$WindowsSrc = Join-Path $SrcDir "windows-connected.jpg"

function Hex([string]$H) { [System.Drawing.ColorTranslator]::FromHtml($H) }

function Load-Bmp([string]$Path) {
    $img = [System.Drawing.Image]::FromFile($Path)
    $clone = New-Object System.Drawing.Bitmap $img
    $img.Dispose()
    return $clone
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

function Spotlight {
    param(
        [System.Drawing.Bitmap]$Src,
        [float]$Nx, [float]$Ny, [float]$Nw, [float]$Nh,
        [string]$Label,
        [float]$BadgeNx, [float]$BadgeNy
    )
    $bmp = New-Object System.Drawing.Bitmap $Src.Width, $Src.Height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = Get-G $bmp
    $g.DrawImage($Src, 0, 0, $Src.Width, $Src.Height)

    $x = $Nx * $Src.Width
    $y = $Ny * $Src.Height
    $w = $Nw * $Src.Width
    $h = $Nh * $Src.Height
    $radius = [Math]::Max(14, $Src.Width * 0.03)

    $veil = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(88, 8, 14, 28))
    $g.FillRectangle($veil, 0, 0, $Src.Width, $Src.Height)
    $veil.Dispose()

    $clip = RoundPath $x $y $w $h $radius
    $g.SetClip($clip)
    $g.DrawImage($Src, 0, 0, $Src.Width, $Src.Height)
    $g.ResetClip()
    $clip.Dispose()

    for ($i = 18; $i -ge 4; $i -= 2) {
        $a = [int](16 + (18 - $i) * 6)
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb($a, 56, 210, 232)), $i
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $path = RoundPath ($x - 2) ($y - 2) ($w + 4) ($h + 4) ($radius + 2)
        $g.DrawPath($pen, $path)
        $path.Dispose(); $pen.Dispose()
    }
    $edge = New-Object System.Drawing.Pen (Hex "#38D2E8"), ([Math]::Max(3, $Src.Width / 140.0))
    $edge.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $path = RoundPath ($x - 1) ($y - 1) ($w + 2) ($h + 2) ($radius + 1)
    $g.DrawPath($edge, $path)
    $path.Dispose(); $edge.Dispose()

    $fontSize = [Math]::Max(16, $Src.Width / 18.0)
    $font = New-Object System.Drawing.Font "Segoe UI Semibold", $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $size = $g.MeasureString($Label, $font)
    $bx = $BadgeNx * $Src.Width
    $by = $BadgeNy * $Src.Height
    $padX = $fontSize * 0.7
    $padY = $fontSize * 0.28
    $bw = $size.Width + $padX * 2
    $bh = $size.Height + $padY * 2
    $fill = New-Object System.Drawing.SolidBrush (Hex "#38D2E8")
    $ink = New-Object System.Drawing.SolidBrush (Hex "#0A1428")
    $br = $bh * 0.5
    $bp = RoundPath $bx $by $bw $bh $br
    $g.FillPath($fill, $bp)
    $bp.Dispose()
    $g.DrawString($Label, $font, $ink, ($bx + $padX), ($by + $padY * 0.4))
    $font.Dispose(); $fill.Dispose(); $ink.Dispose()
    $g.Dispose()
    return $bmp
}

function FitCanvas {
    param(
        [System.Drawing.Bitmap]$Src,
        [int]$W, [int]$H,
        [string]$Path
    )
    $dst = New-Object System.Drawing.Bitmap $W, $H, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = Get-G $dst
    $g.Clear((Hex "#0C1630"))
    $root = Split-Path $Out -Parent
    $circuitPath = Join-Path $root "..\android\app\src\main\res\drawable-nodpi\bg_circuit.png"
    $circuitPath = (Resolve-Path (Join-Path $PSScriptRoot "..\..\android\app\src\main\res\drawable-nodpi\bg_circuit.png")).Path
    $circuit = Load-Bmp $circuitPath
    $scaleC = [Math]::Max($W / [double]$circuit.Width, $H / [double]$circuit.Height)
    $cw = [int]($circuit.Width * $scaleC)
    $ch = [int]($circuit.Height * $scaleC)
    $g.DrawImage($circuit, [int](($W - $cw) / 2), [int](($H - $ch) / 2), $cw, $ch)
    $circuit.Dispose()
    $scrim = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(160, 12, 22, 48))
    $g.FillRectangle($scrim, 0, 0, $W, $H)
    $scrim.Dispose()

    $scale = [Math]::Min($W / [double]$Src.Width, $H / [double]$Src.Height)
    $dw = [int]($Src.Width * $scale)
    $dh = [int]($Src.Height * $scale)
    $dx = [int](($W - $dw) / 2)
    $dy = [int](($H - $dh) / 2)
    $g.DrawImage($Src, $dx, $dy, $dw, $dh)
    $dst.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $dst.Dispose()
}

$android = Load-Bmp $AndroidSrc
# Inner well only: "CX 2.4G Receiver" (not the section header)
$aLit = Spotlight -Src $android -Nx 0.078 -Ny 0.528 -Nw 0.844 -Nh 0.112 -Label "Shared" -BadgeNx 0.50 -BadgeNy 0.445
FitCanvas $aLit 1080 1920 (Join-Path $ShotDir "phone-01-android.png")
FitCanvas $aLit 1440 2560 (Join-Path $ShotDir "tablet-7-android.png")
$aLit.Dispose(); $android.Dispose()

$windows = Load-Bmp $WindowsSrc
# Active connected row: CX 2.4G Receiver
$wLit = Spotlight -Src $windows -Nx 0.048 -Ny 0.718 -Nw 0.904 -Nh 0.082 -Label "Shared" -BadgeNx 0.58 -BadgeNy 0.618
FitCanvas $wLit 1920 1080 (Join-Path $ShotDir "phone-02-windows.png")
FitCanvas $wLit 1920 1080 (Join-Path $ShotDir "tablet-10-windows.png")
$wLit.Dispose(); $windows.Dispose()

Write-Host "Annotated screenshots written to $ShotDir"
