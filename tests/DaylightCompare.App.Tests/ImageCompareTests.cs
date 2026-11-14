using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using DaylightCompare.App.Compare;
using DaylightCompare.Domain.Imaging;
using DaylightCompare.Domain.Policy;
using DaylightCompare.Domain.Settings;

namespace DaylightCompare.App.Tests;

/// <summary>그림 비교 (6단계): 정책이 그림으로 정한 파일은 같은 자리의 탭이 그림 비교로 바뀌고, 보기 방식·허용 오차·맞춤·차이 이동·세션이 동작한다.</summary>
public sealed class ImageCompareTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-image").FullName;

    public ImageCompareTests() => Reset();

    public void Dispose()
    {
        Reset();
        Directory.Delete(_dir, recursive: true);
    }

    static void Reset() => App.Settings.Update(s => s with { Sessions = [], LastWindows = [], CompareHistory = [] });

    static async Task Settle(int rounds = 60)
    {
        for (var i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static string Shot(Window w, string name)
    {
        var dir = Environment.GetEnvironmentVariable("DCMP_SHOTS");
        if (string.IsNullOrEmpty(dir)) return "";
        Directory.CreateDirectory(dir);
        Dispatcher.UIThread.RunJobs();
        var path = Path.Combine(dir, name + ".png");
        w.CaptureRenderedFrame()?.Save(path);
        return path;
    }

    /// <summary>색을 함수로 정한 PNG를 쓴다 (B, G, R, A 직선 알파).</summary>
    string Png(string name, int w, int h, Func<int, int, (byte B, byte G, byte R, byte A)> color)
    {
        var path = Path.Combine(_dir, name);
        using var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = bmp.Lock())
        {
            var row = new byte[w * 4];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var (b, g, r, a) = color(x, y);
                    (row[x * 4], row[x * 4 + 1], row[x * 4 + 2], row[x * 4 + 3]) = (b, g, r, a);
                }
                Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
            }
        }
        bmp.Save(path);
        return path;
    }

    static (byte, byte, byte, byte) Gradient(int x, int y) => ((byte)(x * 3), (byte)(y * 3), (byte)((x + y) * 2), 255);

    static ImageCompareView Image(CompareHost host) => Assert.IsType<ImageCompareView>(host.ActiveTab);

    [AvaloniaFact]
    public void 그림을_풀면_픽셀과_투명도가_그대로_나온다()
    {
        var path = Png("alpha.png", 3, 2, (x, y) => x == 1 && y == 0 ? ((byte)50, (byte)100, (byte)200, (byte)128) : ((byte)10, (byte)20, (byte)30, (byte)255));
        var (pixels, bitmap) = ImageFiles.Decode(File.ReadAllBytes(path));
        Assert.Equal((3, 2), (pixels.Width, pixels.Height));
        Assert.Equal(new PixelSize(3, 2), bitmap.PixelSize);
        var p = pixels.At(0, 0)!.Value;
        Assert.Equal(((byte)10, (byte)20, (byte)30, (byte)255), p);
        var half = pixels.At(1, 0)!.Value;
        Assert.Equal(128, half.A);
        // 직선 알파로 풀었는지: 투명도가 색에 곱해져 있지 않다 (반올림 오차만 허용)
        Assert.InRange(half.R, 198, 202);
        Assert.InRange(half.G, 98, 102);
        Assert.InRange(half.B, 48, 52);
        bitmap.Dispose();
    }

    [AvaloniaFact]
    public async Task png는_그림_비교로_열리고_같은_그림은_같다고_한다()
    {
        var a = Png("a.png", 40, 30, Gradient);
        var b = Png("b.png", 40, 30, Gradient);
        var host = CompareView.Open(null, a, b);
        await Settle();
        var view = Image(host);
        Assert.Single(host.Tabs);
        Assert.True(view.Result!.IsIdentical);
        Assert.Equal(1200, view.Result.TotalPixels);
        Assert.Equal(ImageViewMode.SideBySide, view.Mode);
        Shot(host, "image-identical");
        host.Close();
    }

    [AvaloniaFact]
    public async Task 다른_픽셀을_세고_차이_묶음으로_이동하고_모든_보기_방식이_그려진다()
    {
        var a = Png("c.png", 120, 80, Gradient);
        var b = Png("d.png", 120, 80, (x, y) => x is >= 10 and < 20 && y is >= 10 and < 20 || x is >= 100 and < 105 && y is >= 60 and < 70 ? ((byte)255, (byte)255, (byte)255, (byte)255) : Gradient(x, y));
        var host = CompareView.Open(null, a, b);
        await Settle();
        var view = Image(host);
        Assert.Equal(2, view.Result!.Regions.Count);
        Assert.Equal(150, view.Result.DifferentPixels);
        Assert.Equal(0, view.RegionIndex); // 처음 차이로 시작
        view.GoDifference(forward: true);
        Assert.Equal(1, view.RegionIndex);
        view.GoDifference(forward: true); // 끝에서 멈춘다
        Assert.Equal(1, view.RegionIndex);
        view.GoDifference(forward: false);
        Assert.Equal(0, view.RegionIndex);
        Shot(host, "image-side-by-side");

        foreach (var mode in new[] { ImageViewMode.Difference, ImageViewMode.Overlay, ImageViewMode.Blink, ImageViewMode.SideBySide })
        {
            view.SetMode(mode);
            await Settle(20);
            Assert.Equal(mode, view.Mode);
            Shot(host, "image-" + mode);
        }
        host.Close();
    }

    [AvaloniaFact]
    public async Task 허용_오차_안의_차이는_같게_보고_바꾸면_다시_비교한다()
    {
        var a = Png("e.png", 20, 20, (x, y) => ((byte)100, (byte)100, (byte)100, (byte)255));
        var b = Png("f.png", 20, 20, (x, y) => ((byte)104, (byte)100, (byte)100, (byte)255)); // 파랑 +4
        var host = CompareView.Open(null, a, b);
        await Settle();
        var view = Image(host);
        Assert.Equal(400, view.Result!.DifferentPixels);
        await view.SetToleranceAsync(4);
        await Settle();
        Assert.True(view.Result!.IsIdentical);
        Assert.Equal(4, view.Capture().Tolerance);
        host.Close();
    }

    [AvaloniaFact]
    public async Task 크기가_다른_그림은_맞춤_방식에_따라_놓인다()
    {
        var a = Png("g.png", 20, 20, (x, y) => ((byte)10, (byte)10, (byte)10, (byte)255));
        var b = Png("h.png", 40, 30, (x, y) => ((byte)10, (byte)10, (byte)10, (byte)255));
        var host = CompareView.Open(null, a, b);
        await Settle();
        var view = Image(host);
        Assert.Equal((40, 30), (view.Result!.Layout.Width, view.Result.Layout.Height));
        Assert.Equal(40 * 30 - 20 * 20, view.Result.DifferentPixels); // 오른쪽에만 있는 칸
        await view.SetAlignAsync(ImageAlignKind.Fit);
        await Settle();
        Assert.Equal((20, 20), (view.Result.Layout.Width, view.Result.Layout.Height));
        Assert.True(view.Result.IsIdentical); // 단색이라 늘이면 같다
        Assert.Equal("fit", view.Capture().Align);
        host.Close();
    }

    [AvaloniaFact]
    public async Task 세션에_보기_방식과_맞춤_허용_오차가_저장되고_복원된다()
    {
        var a = Png("i.png", 16, 16, Gradient);
        var b = Png("j.png", 16, 16, Gradient);
        var host = CompareView.Open(null, a, b);
        await Settle();
        var view = Image(host);
        view.SetMode(ImageViewMode.Overlay);
        await view.SetAlignAsync(ImageAlignKind.Center);
        await view.SetToleranceAsync(7);
        var tab = view.Capture();
        Assert.Equal((SessionTab.Image, "overlay", "center", 7), (tab.Kind, tab.ImageMode, tab.Align, tab.Tolerance));
        host.Close();

        var restored = CompareHost.OpenSession(null, new SessionWindow { Tabs = [tab], Active = 0 });
        await Settle();
        var again = Image(restored!);
        Assert.Equal((ImageViewMode.Overlay, ImageAlignKind.Center, 7), (again.Mode, again.Align, again.Tolerance));
        Assert.Equal((a, b), (again.LeftPath, again.RightPath));
        restored!.Close();
    }

    [AvaloniaFact]
    public async Task 그림이_아닌_파일은_이유를_알리고_멈추지_않는다()
    {
        var good = Png("k.png", 8, 8, Gradient);
        var bad = Path.Combine(_dir, "bad.png");
        File.WriteAllText(bad, "이건 그림이 아닙니다");
        var host = CompareView.Open(null, good, bad);
        await Settle();
        var view = Image(host);
        Assert.Null(view.Result);
        host.Close();
    }

    [AvaloniaFact]
    public async Task 같은_짝을_다시_열면_그_탭으로_간다()
    {
        var a = Png("l.png", 8, 8, Gradient);
        var b = Png("m.png", 8, 8, Gradient);
        var host = CompareView.Open(null, a, b);
        await Settle();
        host.OpenFileTab(a, b);
        Assert.Single(host.Tabs);
        host.Close();
    }
}
