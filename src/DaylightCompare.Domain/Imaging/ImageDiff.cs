using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Imaging;

/// <summary>한 장의 그림: 픽셀당 4바이트 B, G, R, A (직선 알파 — 투명도가 색에 곱해져 있지 않다). UI·디코더와 무관한 순수 데이터.</summary>
public sealed record PixelImage(int Width, int Height, byte[] Bgra)
{
    public int Stride => Width * 4;

    /// <summary>(x, y) 픽셀. 그림 밖이면 null.</summary>
    public (byte B, byte G, byte R, byte A)? At(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return null;
        var i = y * Stride + x * 4;
        return (Bgra[i], Bgra[i + 1], Bgra[i + 2], Bgra[i + 3]);
    }
}

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public PixelRect Union(PixelRect o)
    {
        if (IsEmpty) return o;
        if (o.IsEmpty) return this;
        var x = Math.Min(X, o.X);
        var y = Math.Min(Y, o.Y);
        return new PixelRect(x, y, Math.Max(Right, o.Right) - x, Math.Max(Bottom, o.Bottom) - y);
    }
}

/// <summary>
/// 두 그림을 한 도화지에 놓는 법 (크기가 달라도 비교·겹치기가 같은 좌표를 쓴다). 도화지 크기와 각 그림이 놓이는 자리.
/// 한쪽에만 그림이 있는 도화지 칸은 "다름"으로 센다.
/// </summary>
public sealed record ImageLayout(int Width, int Height, PixelRect Left, PixelRect Right)
{
    public long Pixels => (long)Width * Height;

    public static ImageLayout Compute(int leftW, int leftH, int rightW, int rightH, ImageAlignKind align)
    {
        switch (align)
        {
            case ImageAlignKind.Fit:
                // 오른쪽을 왼쪽 크기에 맞춰 늘이거나 줄인다 (가로세로를 따로 맞춘다).
                var all = new PixelRect(0, 0, leftW, leftH);
                return new ImageLayout(leftW, leftH, all, all);
            case ImageAlignKind.Center:
                var w = Math.Max(leftW, rightW);
                var h = Math.Max(leftH, rightH);
                return new ImageLayout(w, h, new PixelRect((w - leftW) / 2, (h - leftH) / 2, leftW, leftH), new PixelRect((w - rightW) / 2, (h - rightH) / 2, rightW, rightH));
            default:
                return new ImageLayout(Math.Max(leftW, rightW), Math.Max(leftH, rightH), new PixelRect(0, 0, leftW, leftH), new PixelRect(0, 0, rightW, rightH));
        }
    }

    /// <summary>도화지 좌표 (x, y)에 놓인 <paramref name="image"/>의 픽셀 (그 그림이 거기 없으면 null). 늘이거나 줄인 그림은 가장 가까운 픽셀.</summary>
    public static (byte B, byte G, byte R, byte A)? Sample(PixelImage image, PixelRect rect, int x, int y)
    {
        if (!rect.Contains(x, y)) return null;
        var sx = rect.Width == image.Width ? x - rect.X : (int)((long)(x - rect.X) * image.Width / rect.Width);
        var sy = rect.Height == image.Height ? y - rect.Y : (int)((long)(y - rect.Y) * image.Height / rect.Height);
        return image.At(sx, sy);
    }
}

/// <param name="Tolerance">채널(R·G·B·A) 하나가 이만큼(0~255)까지 달라도 같은 색으로 본다.</param>
/// <param name="IgnoreAlpha">투명도 채널은 비교하지 않는다.</param>
public sealed record ImageDiffOptions(int Tolerance, bool IgnoreAlpha, ImageAlignKind Align)
{
    public static readonly ImageDiffOptions Default = new(0, false, ImageAlignKind.TopLeft);
}

public sealed class ImageDiffResult(ImageLayout layout, byte[] mask, long different, IReadOnlyList<PixelRect> regions)
{
    public ImageLayout Layout => layout;

    /// <summary>도화지 픽셀마다 1바이트: 다르면 1, 같으면 0 (행 우선, 가로 <see cref="ImageLayout.Width"/>).</summary>
    public byte[] Mask => mask;

    public long DifferentPixels => different;
    public long TotalPixels => layout.Pixels;
    public double Percent => TotalPixels == 0 ? 0 : 100.0 * different / TotalPixels;
    public bool IsIdentical => different == 0;

    /// <summary>다른 픽셀이 모여 있는 곳들 (위에서 아래, 왼쪽에서 오른쪽 순) — 차이 이동에 쓴다.</summary>
    public IReadOnlyList<PixelRect> Regions => regions;

    /// <summary>다른 비율이 <paramref name="maxPercent"/> 이하면 "같다"고 본다 (정책의 <c>maxDiffPercent</c>).</summary>
    public bool IsWithin(double maxPercent) => Percent <= maxPercent;

    public bool IsDifferent(int x, int y) => (uint)x < (uint)layout.Width && (uint)y < (uint)layout.Height && mask[y * layout.Width + x] != 0;
}

/// <summary>두 그림을 픽셀 단위로 비교한다 (Beyond Compare의 Picture Compare — Difference 보기와 허용 오차).</summary>
public static class ImageDiff
{
    /// <summary>도화지가 이보다 크면 비교하지 않는다 (마스크가 픽셀당 1바이트라 메모리를 지킨다).</summary>
    public const long MaxCanvasPixels = 150_000_000;

    /// <summary>묶음을 찾는 칸 크기 (픽셀). 이 칸보다 가까운 차이는 한 묶음이다.</summary>
    const int Tile = 24;

    public static ImageDiffResult Compare(PixelImage left, PixelImage right, ImageDiffOptions options, CancellationToken ct = default)
    {
        var layout = ImageLayout.Compute(left.Width, left.Height, right.Width, right.Height, options.Align);
        if (layout.Pixels > MaxCanvasPixels) throw new ArgumentException("canvas too large", nameof(left));
        var w = layout.Width;
        var h = layout.Height;
        var mask = new byte[layout.Pixels];
        var rowCounts = new long[h];
        var tolerance = Math.Clamp(options.Tolerance, 0, 255);
        var ignoreAlpha = options.IgnoreAlpha;
        var lr = layout.Left;
        var rr = layout.Right;
        var lPlain = lr.Width == left.Width && lr.Height == left.Height;
        var rPlain = rr.Width == right.Width && rr.Height == right.Height;

        Parallel.For(0, h, new ParallelOptions { CancellationToken = ct }, y =>
        {
            long count = 0;
            var row = y * w;
            var lInRow = y >= lr.Y && y < lr.Bottom;
            var rInRow = y >= rr.Y && y < rr.Bottom;
            for (var x = 0; x < w; x++)
            {
                var inL = lInRow && x >= lr.X && x < lr.Right;
                var inR = rInRow && x >= rr.X && x < rr.Right;
                bool differs;
                if (inL && inR)
                {
                    if (lPlain && rPlain)
                        differs = Differs(left.Bgra, (y - lr.Y) * left.Stride + (x - lr.X) * 4, right.Bgra, (y - rr.Y) * right.Stride + (x - rr.X) * 4, tolerance, ignoreAlpha);
                    else
                    {
                        // 늘이거나 줄인 그림이 끼면 느린 길로 (Fit)
                        var a = ImageLayout.Sample(left, lr, x, y)!.Value;
                        var b = ImageLayout.Sample(right, rr, x, y)!.Value;
                        differs = Differs(a, b, tolerance, ignoreAlpha);
                    }
                }
                else differs = inL || inR; // 한쪽에만 있는 칸
                if (differs) { mask[row + x] = 1; count++; }
            }
            rowCounts[y] = count;
        });
        return new ImageDiffResult(layout, mask, rowCounts.Sum(), FindRegions(mask, w, h, rowCounts));
    }

    static bool Differs(byte[] a, int ai, byte[] b, int bi, int tolerance, bool ignoreAlpha)
    {
        if (!ignoreAlpha && a[ai + 3] == 0 && b[bi + 3] == 0) return false; // 둘 다 투명하면 색이 달라도 같은 그림
        return Math.Abs(a[ai] - b[bi]) > tolerance || Math.Abs(a[ai + 1] - b[bi + 1]) > tolerance || Math.Abs(a[ai + 2] - b[bi + 2]) > tolerance
            || (!ignoreAlpha && Math.Abs(a[ai + 3] - b[bi + 3]) > tolerance);
    }

    static bool Differs((byte B, byte G, byte R, byte A) a, (byte B, byte G, byte R, byte A) b, int tolerance, bool ignoreAlpha)
    {
        if (!ignoreAlpha && a.A == 0 && b.A == 0) return false;
        return Math.Abs(a.B - b.B) > tolerance || Math.Abs(a.G - b.G) > tolerance || Math.Abs(a.R - b.R) > tolerance
            || (!ignoreAlpha && Math.Abs(a.A - b.A) > tolerance);
    }

    /// <summary>칸(<see cref="Tile"/>) 단위로 다른 픽셀이 든 칸을 찾고, 이웃한 칸(8방향)을 묶어 묶음마다 다른 픽셀의 바깥 사각형을 낸다.</summary>
    static List<PixelRect> FindRegions(byte[] mask, int w, int h, long[] rowCounts)
    {
        var regions = new List<PixelRect>();
        if (w == 0 || h == 0) return regions;
        var tx = (w + Tile - 1) / Tile;
        var ty = (h + Tile - 1) / Tile;
        var minX = new int[tx * ty];
        var minY = new int[tx * ty];
        var maxX = new int[tx * ty];
        var maxY = new int[tx * ty];
        Array.Fill(minX, int.MaxValue);
        Array.Fill(minY, int.MaxValue);
        Array.Fill(maxX, -1);
        Array.Fill(maxY, -1);
        for (var y = 0; y < h; y++)
        {
            if (rowCounts[y] == 0) continue;
            var row = y * w;
            var tyi = y / Tile * tx;
            for (var x = 0; x < w; x++)
            {
                if (mask[row + x] == 0) continue;
                var t = tyi + x / Tile;
                if (x < minX[t]) minX[t] = x;
                if (x > maxX[t]) maxX[t] = x;
                if (y < minY[t]) minY[t] = y;
                if (y > maxY[t]) maxY[t] = y;
            }
        }
        var seen = new bool[tx * ty];
        var stack = new Stack<int>();
        for (var start = 0; start < seen.Length; start++)
        {
            if (seen[start] || maxX[start] < 0) continue;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                x0 = Math.Min(x0, minX[t]); y0 = Math.Min(y0, minY[t]);
                x1 = Math.Max(x1, maxX[t]); y1 = Math.Max(y1, maxY[t]);
                int cx = t % tx, cy = t / tx;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= tx || ny >= ty) continue;
                        var n = ny * tx + nx;
                        if (seen[n] || maxX[n] < 0) continue;
                        seen[n] = true;
                        stack.Push(n);
                    }
            }
            regions.Add(new PixelRect(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
        }
        regions.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        return regions;
    }

    /// <summary><paramref name="current"/>(없으면 -1) 다음·이전 묶음 번호 (끝에서 멈춘다). 묶음이 없으면 null.</summary>
    public static int? NextRegion(IReadOnlyList<PixelRect> regions, int current, bool forward)
    {
        if (regions.Count == 0) return null;
        var next = forward ? current + 1 : (current < 0 ? regions.Count - 1 : current - 1);
        return next >= 0 && next < regions.Count ? next : null;
    }
}
