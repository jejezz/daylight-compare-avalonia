using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using DaylightCompare.App.Compare;

namespace DaylightCompare.App.Tests;

/// <summary>파일 비교 창의 스크롤: 양쪽 편집기 스크롤 막대가 함께 움직이고, 개요 막대를 누르면 그 위치로 간다.</summary>
public sealed class CompareScrollTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-scroll").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    static async Task Settle(int rounds = 40)
    {
        for (var i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static string Lines(string prefix, int count) => string.Concat(Enumerable.Range(0, count).Select(i => $"{prefix} {i}\n"));

    static (TextEditor Left, TextEditor Right) Editors(Window w)
    {
        var editors = w.GetVisualDescendants().OfType<TextEditor>().ToList();
        return (editors[0], editors[1]);
    }

    static Control Overview(Window w) => w.GetVisualDescendants().OfType<Control>().Single(c => c.GetType().Name == "OverviewBar");

    static ScrollBar VerticalBar(TextEditor e) =>
        e.GetVisualDescendants().OfType<ScrollBar>().First(b => b.Orientation == Orientation.Vertical);

    [AvaloniaFact]
    public async Task 양쪽_편집기에_세로_스크롤_막대가_있고_어느_쪽을_끌어도_함께_움직인다()
    {
        var left = Write("l.txt", Lines("a", 400));
        var right = Write("r.txt", Lines("a", 400) + "extra\n");
        var w = CompareWindow.Open(null, left, right);
        await Settle();
        var (l, r) = Editors(w);
        var lb = VerticalBar(l);
        var rb = VerticalBar(r);
        Assert.True(lb.IsVisible && lb.Bounds.Width > 0, "왼쪽 스크롤 막대가 보이지 않는다");
        Assert.True(rb.IsVisible && rb.Bounds.Width > 0);

        lb.Value = 500; // 왼쪽 막대를 끌면 오른쪽도 같이
        await Settle(10);
        Assert.Equal(500, l.VerticalOffset, 1);
        Assert.Equal(l.VerticalOffset, r.VerticalOffset, 1);

        rb.Value = 900; // 오른쪽 막대를 끌면 왼쪽도 같이
        await Settle(10);
        Assert.Equal(900, r.VerticalOffset, 1);
        Assert.Equal(r.VerticalOffset, l.VerticalOffset, 1);
        using (var frame = w.CaptureRenderedFrame())
            frame!.Save(Path.Combine(TestApp.Shots, "compare-scrollbars.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        w.Close();
    }

    [AvaloniaFact]
    public async Task 개요_막대를_누르면_양쪽이_그_위치로_스크롤한다()
    {
        var left = Write("l2.txt", Lines("a", 300) + "LEFT\n" + Lines("b", 300));
        var right = Write("r2.txt", Lines("a", 300) + "RIGHT\n" + Lines("b", 300));
        var w = CompareWindow.Open(null, left, right);
        await Settle();
        var (l, r) = Editors(w);
        VerticalBar(r).Value = 0;
        await Settle(5);
        Assert.Equal(0, r.VerticalOffset, 1);

        var bar = Overview(w);
        var down = bar.TranslatePoint(new Point(bar.Bounds.Width / 2, bar.Bounds.Height * 0.9), w)!.Value;
        w.MouseDown(down, MouseButton.Left);
        w.MouseUp(down, MouseButton.Left);
        await Settle(10);
        Assert.True(r.VerticalOffset > 1000, $"오른쪽 편집기가 스크롤되지 않았다: {r.VerticalOffset}");
        Assert.Equal(r.VerticalOffset, l.VerticalOffset, 1);

        var top = bar.TranslatePoint(new Point(bar.Bounds.Width / 2, 2), w)!.Value; // 위쪽을 누르면 처음으로
        w.MouseDown(top, MouseButton.Left);
        w.MouseUp(top, MouseButton.Left);
        await Settle(10);
        Assert.True(r.VerticalOffset < 200, $"처음으로 돌아가지 않았다: {r.VerticalOffset}");
        Assert.Equal(r.VerticalOffset, l.VerticalOffset, 1);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 다음_차이로_가면_양쪽_편집기가_같은_자리를_보인다()
    {
        var left = Write("l3.txt", Lines("a", 300) + "LEFT\n" + Lines("b", 100));
        var right = Write("r3.txt", Lines("a", 300) + "RIGHT\n" + Lines("b", 100));
        var w = CompareWindow.Open(null, left, right);
        await Settle();
        var (l, r) = Editors(w);
        Assert.True(r.VerticalOffset > 1000, $"처음 차이로 스크롤되지 않았다: {r.VerticalOffset}"); // 처음 차이(301행)가 보이게
        Assert.Equal(r.VerticalOffset, l.VerticalOffset, 1);
        w.Close();
    }
}
