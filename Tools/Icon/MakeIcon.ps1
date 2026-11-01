# Derives the OMSI MapWorks icon from the logo (ADR 056): the logo's own white "M" of "Map" and orange "W" of "Works" with
# their dark outlines, over the orange and grey stripe of the logo, on a rounded tile in the editor's background colour.
# Writes Assets/_Project/UI/Branding/Icon.png (1024 px, the player's icon) and Tools/Icon/MapWorks.ico (16 to 256 px,
# the setup's icon). Run it again after the logo changes:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Icon/MakeIcon.ps1 [-Preview <folder>]
param([string]$Preview)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$logo = [System.Drawing.Bitmap]::FromFile((Join-Path $root 'Assets\_Project\UI\Branding\Logo.png'))

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

public static class LetterCutter
{
    // One letter of the logo on its own: its face, found by filling from a point inside it, with the dark outline around
    // it, without the faces of the letters it touches.
    public static Bitmap Cut(Bitmap logo, Rectangle area, Point seed, int outline)
    {
        var width = area.Width;
        var height = area.Height;
        var pixels = new int[width * height];
        var data = logo.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        // Row by row: a row of the locked area is as long as a row of the whole logo.
        for (var row = 0; row < height; row++)
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * width, width);
        logo.UnlockBits(data);

        var face = new bool[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            var c = Color.FromArgb(pixels[i]);
            face[i] = c.A > 128 && (c.R + c.G + c.B) / 3 > 80;
        }
        // The letter's face: everything bright that joins the seed.
        var letter = new bool[pixels.Length];
        var start = (seed.Y - area.Y) * width + seed.X - area.X;
        var queue = new Queue<int>();
        letter[start] = true;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            var x = i % width;
            var y = i / width;
            foreach (var n in new[] { x > 0 ? i - 1 : -1, x < width - 1 ? i + 1 : -1, y > 0 ? i - width : -1, y < height - 1 ? i + width : -1 })
                if (n >= 0 && face[n] && !letter[n])
                {
                    letter[n] = true;
                    queue.Enqueue(n);
                }
        }
        // Distance from the nearest face, in steps: an outline pixel belongs to the letter it lies closest to, so the
        // outlines of neighbouring letters stay out.
        var distance = new int[pixels.Length];
        var ours = new bool[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            distance[i] = face[i] ? 0 : int.MaxValue;
            ours[i] = letter[i];
            if (face[i])
                queue.Enqueue(i);
        }
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            var x = i % width;
            var y = i / width;
            if (distance[i] >= outline)
                continue;
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;
                    var n = ny * width + nx;
                    if (distance[n] == int.MaxValue)
                    {
                        distance[n] = distance[i] + 1;
                        ours[n] = ours[i];
                        queue.Enqueue(n);
                    }
                }
        }
        var result = new int[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
            result[i] = distance[i] <= outline && ours[i] ? pixels[i] : 0;
        var cut = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var target = cut.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (var row = 0; row < height; row++)
            System.Runtime.InteropServices.Marshal.Copy(result, row * width, target.Scan0 + row * target.Stride, width);
        cut.UnlockBits(target);
        return cut;
    }

    // The smallest rectangle around the pixels that show.
    public static Rectangle Bounds(Bitmap image)
    {
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
                if (image.GetPixel(x, y).A > 8)
                {
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                }
        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }
}
'@

# Where the parts lie in Logo.png (1190 x 365 px): the areas around the letters, a point inside each, the stripe.
$letterM = [LetterCutter]::Cut($logo, (New-Object System.Drawing.Rectangle 8, 50, 222, 216), (New-Object System.Drawing.Point 50, 200), 12)
$letterW = [LetterCutter]::Cut($logo, (New-Object System.Drawing.Rectangle 440, 66, 290, 200), (New-Object System.Drawing.Point 480, 120), 12)
# The stripe is drawn as shapes in the orange of the logo's stripe, so it stays sharp when small.
$orange = $logo.GetPixel(100, 285)
$outlineColor = [System.Drawing.ColorTranslator]::FromHtml('#0B0E12')

$background = [System.Drawing.ColorTranslator]::FromHtml('#171C23')
$panel = [System.Drawing.ColorTranslator]::FromHtml('#29313D')
$border = [System.Drawing.ColorTranslator]::FromHtml('#414B59')

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Icon([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # The tile: the editor's background, a little lighter at the top, with a thin border.
    $inset = [Math]::Max(0.5, 6 * $s)
    $tile = New-RoundedPath $inset $inset ($size - 2 * $inset) ($size - 2 * $inset) (46 * $s)
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $size), $panel, $background
    $g.FillPath($fill, $tile)
    $pen = New-Object System.Drawing.Pen $border, ([Math]::Max(1.0, 4 * $s))
    $g.DrawPath($pen, $tile)

    # M and W side by side on the logo's baseline, their outlines just touching, above the stripe; together centred.
    $mBounds = [LetterCutter]::Bounds($letterM)
    $wBounds = [LetterCutter]::Bounds($letterW)
    $touch = 8
    $scale = 196 * $s / ($mBounds.Width + $wBounds.Width - $touch)
    $lettersHeight = [Math]::Max($mBounds.Height, $wBounds.Height) * $scale
    $stripeWidth = 168 * $s
    $stripeHeight = 11 * $s
    $gap = 10 * $s
    $top = ($size - ($lettersHeight + $gap + $stripeHeight)) / 2
    $baseline = $top + $lettersHeight
    $left = ($size - ($mBounds.Width + $wBounds.Width - $touch) * $scale) / 2
    $g.DrawImage($letterM, (New-Object System.Drawing.RectangleF $left, ($baseline - $mBounds.Height * $scale), ($mBounds.Width * $scale), ($mBounds.Height * $scale)),
        $mBounds, [System.Drawing.GraphicsUnit]::Pixel)
    $wLeft = $left + ($mBounds.Width - $touch) * $scale
    $g.DrawImage($letterW, (New-Object System.Drawing.RectangleF $wLeft, ($baseline - $wBounds.Height * $scale), ($wBounds.Width * $scale), ($wBounds.Height * $scale)),
        $wBounds, [System.Drawing.GraphicsUnit]::Pixel)
    # A long bar and two slashes leaning like the logo's, each with the logo's dark outline.
    $x = ($size - $stripeWidth) / 2
    $y = $baseline + $gap
    $lean = $stripeHeight * 0.8
    $slash = 16 * $s
    $space = 7 * $s
    $bar = $stripeWidth - 2 * ($slash + $space)
    # A list, since PowerShell would flatten nested arrays into one list of points.
    $shapes = New-Object 'System.Collections.Generic.List[System.Drawing.PointF[]]'
    $shapes.Add([System.Drawing.PointF[]]@((New-Object System.Drawing.PointF $x, $y), (New-Object System.Drawing.PointF ($x + $bar), $y),
        (New-Object System.Drawing.PointF ($x + $bar - $lean), ($y + $stripeHeight)), (New-Object System.Drawing.PointF $x, ($y + $stripeHeight))))
    foreach ($n in 1, 2) {
        $sx = $x + $bar + $space + ($n - 1) * ($slash + $space)
        $shapes.Add([System.Drawing.PointF[]]@((New-Object System.Drawing.PointF ($sx + $lean), $y), (New-Object System.Drawing.PointF ($sx + $slash), $y),
            (New-Object System.Drawing.PointF ($sx + $slash - $lean), ($y + $stripeHeight)), (New-Object System.Drawing.PointF $sx, ($y + $stripeHeight))))
    }
    $edge = New-Object System.Drawing.Pen $outlineColor, ([Math]::Max(1.0, 5 * $s))
    $edge.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $brush = New-Object System.Drawing.SolidBrush $orange
    foreach ($shape in $shapes) { $g.DrawPolygon($edge, $shape) }
    foreach ($shape in $shapes) { $g.FillPolygon($brush, $shape) }
    $g.Dispose()
    return $bitmap
}

function Get-PngBytes($bitmap) {
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $stream.ToArray()
}

# The icon file: a header, one directory entry per size, then the images as PNG (Windows Vista and later read them).
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = @()
foreach ($size in $sizes) { $images += , (Get-PngBytes (New-Icon $size)) }
$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $ico
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$images[$i].Length); $writer.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'MapWorks.ico'), $ico.ToArray())

$large = New-Icon 1024
$large.Save((Join-Path $root 'Assets\_Project\UI\Branding\Icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)

if ($Preview) {
    # All sizes side by side on light and dark ground, as Explorer and the taskbar show them.
    $sheet = New-Object System.Drawing.Bitmap 900, 620
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::FromArgb(243, 243, 243))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(32, 32, 32))), 0, 310, 900, 310)
    foreach ($band in 0, 310) {
        $x = 16
        foreach ($size in $sizes) {
            $g.DrawImage((New-Icon $size), $x, ($band + 16 + 256 - $size))
            $x += $size + 16
        }
    }
    $sheet.Save((Join-Path $Preview 'icon-sheet.png'), [System.Drawing.Imaging.ImageFormat]::Png)
}
'Icon written: ' + $sizes.Count + ' sizes'
