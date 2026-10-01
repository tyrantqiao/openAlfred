# 生成应用图标 assets/app.ico：蓝底圆角 + 白色放大镜（与托盘 DrawingImage 同款）
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot "..\assets\app.ico"
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

function New-Frame([int]$size) {
    # 两参构造默认即 Format32bppArgb，避免 PS 重载绑定到 (int,int,Graphics)
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 32.0   # 设计网格为 32x32
    $blue = [System.Drawing.Color]::FromArgb(255, 0, 122, 255)
    $brush = New-Object System.Drawing.SolidBrush($blue)
    $r = 8 * $s
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($size - 2 * $r - 1, 0, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($size - 2 * $r - 1, $size - 2 * $r - 1, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc(0, $size - 2 * $r - 1, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $g.FillPath($brush, $path)

    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [Math]::Max(1.5, 2.6 * $s))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    # 镜圈：中心(14,14) 半径6 → 包围盒 (8,8,12,12)
    $g.DrawEllipse($pen, 8 * $s, 8 * $s, 12 * $s, 12 * $s)
    # 手柄：(18.5,18.5) → (24,24)
    $g.DrawLine($pen, 18.5 * $s, 18.5 * $s, 24 * $s, 24 * $s)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = @(16, 24, 32, 48, 256)
$frames = foreach ($n in $sizes) { New-Frame $n }

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)          # reserved
$bw.Write([uint16]1)          # type: icon
$bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $n = $sizes[$i]
    $data = $frames[$i]
    $bw.Write([byte]($(if ($n -ge 256) { 0 } else { $n })))  # width
    $bw.Write([byte]($(if ($n -ge 256) { 0 } else { $n })))  # height
    $bw.Write([byte]0)        # 调色板色数
    $bw.Write([byte]0)        # reserved
    $bw.Write([uint16]1)      # planes
    $bw.Write([uint16]32)     # bit count
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $frames) { $bw.Write($data) }
$bw.Close()

Write-Output "written: $out ($((Get-Item $out).Length) bytes)"
