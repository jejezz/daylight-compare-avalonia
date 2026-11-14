using DaylightCompare.Domain.Imaging;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Tests;

public sealed class ImageDiffTests
{
    static PixelImage Solid(int w, int h, byte b = 10, byte g = 20, byte r = 30, byte a = 255)
    {
        var data = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++) { data[i * 4] = b; data[i * 4 + 1] = g; data[i * 4 + 2] = r; data[i * 4 + 3] = a; }
        return new PixelImage(w, h, data);
    }

    static PixelImage With(PixelImage src, int x, int y, byte b, byte g, byte r, byte a = 255)
    {
        var data = (byte[])src.Bgra.Clone();
        var i = y * src.Stride + x * 4;
        (data[i], data[i + 1], data[i + 2], data[i + 3]) = (b, g, r, a);
        return src with { Bgra = data };
    }

    [Fact]
    public void 같은_그림은_차이가_없다()
    {
        var r = ImageDiff.Compare(Solid(8, 6), Solid(8, 6), ImageDiffOptions.Default);
        Assert.True(r.IsIdentical);
        Assert.Equal(48, r.TotalPixels);
        Assert.Empty(r.Regions);
        Assert.Equal(0, r.Percent);
    }

    [Fact]
    public void 한_픽셀만_달라도_세고_위치를_안다()
    {
        var l = Solid(8, 6);
        var r = ImageDiff.Compare(l, With(l, 3, 4, 0, 0, 0), ImageDiffOptions.Default);
        Assert.Equal(1, r.DifferentPixels);
        Assert.True(r.IsDifferent(3, 4));
        Assert.False(r.IsDifferent(4, 4));
        Assert.Equal([new PixelRect(3, 4, 1, 1)], r.Regions);
        Assert.Equal(100.0 / 48, r.Percent, 6);
    }

    [Fact]
    public void 허용_오차_안의_차이는_같은_색으로_본다()
    {
        var l = Solid(4, 4);
        var near = With(l, 1, 1, 14, 20, 30); // 파랑 +4
        Assert.Equal(1, ImageDiff.Compare(l, near, new ImageDiffOptions(3, false, ImageAlignKind.TopLeft)).DifferentPixels);
        Assert.Equal(0, ImageDiff.Compare(l, near, new ImageDiffOptions(4, false, ImageAlignKind.TopLeft)).DifferentPixels);
    }

    [Fact]
    public void 투명도_무시는_알파만_다른_픽셀을_같게_본다()
    {
        var l = Solid(4, 4);
        var alpha = With(l, 2, 2, 10, 20, 30, 128);
        Assert.Equal(1, ImageDiff.Compare(l, alpha, ImageDiffOptions.Default).DifferentPixels);
        Assert.Equal(0, ImageDiff.Compare(l, alpha, new ImageDiffOptions(0, true, ImageAlignKind.TopLeft)).DifferentPixels);
    }

    [Fact]
    public void 둘_다_완전히_투명한_픽셀은_색이_달라도_같다()
    {
        var l = With(Solid(4, 4), 0, 0, 1, 2, 3, 0);
        var r = With(Solid(4, 4), 0, 0, 200, 100, 50, 0);
        Assert.True(ImageDiff.Compare(l, r, ImageDiffOptions.Default).IsIdentical);
    }

    [Fact]
    public void 크기가_다르면_왼쪽_위에_맞추고_한쪽에만_있는_칸은_다르다()
    {
        var r = ImageDiff.Compare(Solid(4, 4), Solid(6, 4), ImageDiffOptions.Default);
        Assert.Equal((6, 4), (r.Layout.Width, r.Layout.Height));
        Assert.Equal(8, r.DifferentPixels); // 오른쪽에만 있는 2열 × 4행
        Assert.True(r.IsDifferent(5, 0));
        Assert.False(r.IsDifferent(3, 3));
    }

    [Fact]
    public void 가운데_맞춤은_양쪽에_같은_여백을_둔다()
    {
        var layout = ImageLayout.Compute(4, 4, 8, 6, ImageAlignKind.Center);
        Assert.Equal((8, 6), (layout.Width, layout.Height));
        Assert.Equal(new PixelRect(2, 1, 4, 4), layout.Left);
        Assert.Equal(new PixelRect(0, 0, 8, 6), layout.Right);
    }

    [Fact]
    public void 맞춤은_오른쪽을_왼쪽_크기로_늘여_비교한다()
    {
        // 2x2 단색을 4x4 단색과 비교: 늘이면 같은 그림
        var r = ImageDiff.Compare(Solid(4, 4), Solid(2, 2), new ImageDiffOptions(0, false, ImageAlignKind.Fit));
        Assert.Equal((4, 4), (r.Layout.Width, r.Layout.Height));
        Assert.True(r.IsIdentical);
        // 늘린 그림의 한 픽셀이 다르면 2x2 칸 전체(4픽셀)가 다르다
        var diff = ImageDiff.Compare(Solid(4, 4), With(Solid(2, 2), 1, 1, 0, 0, 0), new ImageDiffOptions(0, false, ImageAlignKind.Fit));
        Assert.Equal(4, diff.DifferentPixels);
        Assert.Equal([new PixelRect(2, 2, 2, 2)], diff.Regions);
    }

    [Fact]
    public void 멀리_떨어진_차이는_따로_묶고_가까운_차이는_하나로_묶는다()
    {
        var l = Solid(200, 100);
        var r = With(With(With(l, 5, 5, 0, 0, 0), 8, 9, 0, 0, 0), 150, 80, 0, 0, 0);
        var diff = ImageDiff.Compare(l, r, ImageDiffOptions.Default);
        Assert.Equal(3, diff.DifferentPixels);
        Assert.Equal([new PixelRect(5, 5, 4, 5), new PixelRect(150, 80, 1, 1)], diff.Regions);
    }

    [Fact]
    public void 묶음_이동은_끝에서_멈춘다()
    {
        IReadOnlyList<PixelRect> regions = [new(0, 0, 1, 1), new(5, 5, 1, 1)];
        Assert.Equal(0, ImageDiff.NextRegion(regions, -1, forward: true));
        Assert.Equal(1, ImageDiff.NextRegion(regions, 0, forward: true));
        Assert.Null(ImageDiff.NextRegion(regions, 1, forward: true));
        Assert.Equal(1, ImageDiff.NextRegion(regions, -1, forward: false));
        Assert.Null(ImageDiff.NextRegion(regions, 0, forward: false));
        Assert.Null(ImageDiff.NextRegion([], -1, forward: true));
    }

    [Fact]
    public void 다른_비율이_정책의_한도_이하면_같다고_본다()
    {
        var l = Solid(10, 10);
        var r = ImageDiff.Compare(l, With(l, 0, 0, 0, 0, 0), ImageDiffOptions.Default);
        Assert.Equal(1.0, r.Percent, 6);
        Assert.True(r.IsWithin(1.0));
        Assert.False(r.IsWithin(0.5));
    }

    [Fact]
    public void 픽셀_읽기는_늘인_자리도_가장_가까운_픽셀을_준다()
    {
        var img = With(Solid(2, 2), 1, 1, 1, 2, 3);
        var rect = new PixelRect(0, 0, 4, 4);
        Assert.Equal(((byte)1, (byte)2, (byte)3, (byte)255), ImageLayout.Sample(img, rect, 3, 3));
        Assert.Null(ImageLayout.Sample(img, rect, 4, 0));
    }
}
