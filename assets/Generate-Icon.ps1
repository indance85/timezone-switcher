$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sizes = @(16,20,24,32,40,48,64,128,256)
$frames = @()
foreach ($size in $sizes) {
    $scale = 4
    $canvas = $size * $scale
    $bitmap = [Drawing.Bitmap]::new($canvas,$canvas,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([Drawing.Color]::Transparent)
    $inset = $canvas * 0.045
    $diameter = $canvas * 0.43
    $side = $canvas - 2 * $inset
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($inset,$inset,$diameter,$diameter,180,90)
    $path.AddArc($inset+$side-$diameter,$inset,$diameter,$diameter,270,90)
    $path.AddArc($inset+$side-$diameter,$inset+$side-$diameter,$diameter,$diameter,0,90)
    $path.AddArc($inset,$inset+$side-$diameter,$diameter,$diameter,90,90)
    $path.CloseFigure()
    $fill = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(0,0),[Drawing.Point]::new($canvas,$canvas),[Drawing.ColorTranslator]::FromHtml('#246B57'),[Drawing.ColorTranslator]::FromHtml('#123D36'))
    $g.FillPath($fill,$path)
    $border = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#8DD6B7'),$canvas/28)
    $g.DrawPath($border,$path)
    $clockPen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#F0FFF8'),$canvas * 0.065)
    $clockPen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $clockPen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $g.DrawEllipse($clockPen,($canvas * 0.23),($canvas * 0.23),($canvas * 0.54),($canvas * 0.54))
    $g.DrawLine($clockPen,($canvas * 0.5),($canvas * 0.34),($canvas * 0.5),($canvas * 0.5))
    $g.DrawLine($clockPen,($canvas * 0.5),($canvas * 0.5),($canvas * 0.64),($canvas * 0.55))
    $small = [Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $sg = [Drawing.Graphics]::FromImage($small)
    $sg.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $sg.DrawImage($bitmap,0,0,$size,$size)
    $stream = [IO.MemoryStream]::new()
    $small.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $frames += [pscustomobject]@{Size=$size;Data=$stream.ToArray()}
    if ($size -eq 256) { $small.Save((Join-Path $PSScriptRoot 'timezone-icon.png'),[Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $sg.Dispose(); $small.Dispose(); $clockPen.Dispose(); $border.Dispose(); $fill.Dispose(); $path.Dispose(); $g.Dispose(); $bitmap.Dispose()
}
$file = [IO.File]::Create((Join-Path $PSScriptRoot 'timezone.ico'))
$writer = [IO.BinaryWriter]::new($file)
$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dimension = if($frame.Size -eq 256){0}else{$frame.Size}
    $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
    $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frame.Data.Length);$writer.Write([uint32]$offset)
    $offset += $frame.Data.Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }
$writer.Dispose()
# Render the actual icon sizes on light and dark taskbar-like surfaces.
$preview = [Drawing.Bitmap]::new(480,180)
$g = [Drawing.Graphics]::FromImage($preview)
$g.Clear([Drawing.ColorTranslator]::FromHtml('#F3F5F7'))
$dark = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#20252B'))
$g.FillRectangle($dark,0,90,480,90)
$x = 24
foreach($size in @(16,20,24,32,48,64,128)) {
    $data = ($frames | Where-Object Size -eq $size).Data
    $stream = [IO.MemoryStream]::new([byte[]]$data)
    $img = [Drawing.Image]::FromStream($stream)
    $display = [Math]::Min($size,64)
    $g.DrawImage($img,$x,(90-$display)/2,$display,$display)
    $g.DrawImage($img,$x,90+(90-$display)/2,$display,$display)
    $x += $display + 20
    $img.Dispose();$stream.Dispose()
}
$preview.Save((Join-Path $PSScriptRoot 'tray-preview.png'),[Drawing.Imaging.ImageFormat]::Png)
$dark.Dispose();$g.Dispose();$preview.Dispose()
