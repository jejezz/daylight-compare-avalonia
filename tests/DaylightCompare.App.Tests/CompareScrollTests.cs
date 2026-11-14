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

/// <summary>
/// 파일 비교 창의 스크롤: 세로 스크롤 막대는 오른쪽 개요 막대 하나뿐이다(차이 위치 지도 + 보이는 범위 손잡이).
/// 손잡이를 끌거나 빈 자리를 누르면 양쪽 편집기가 함께 스크롤되고, 휠은 편집기에서도 개요 막대에서도 된다.
/// </summary>
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
            await Task.Delay(15);
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

    static ScrollViewer ScrollerOf(TextEditor e) => e.GetVisualDescendants().OfType<ScrollViewer>().First();

    /// <summary>개요 막대 안의 보이는 범위(손잡이) 위치·높이 — 오른쪽 편집기의 스크롤 상태에서 계산한다.</summary>
    static (double Top, double Height) Thumb(Control bar, TextEditor r) =>
        (bar.Bounds.Height * r.VerticalOffset / r.ExtentHeight, bar.Bounds.Height * r.ViewportHeight / r.ExtentHeight);

    static Point In(Window w, Control bar, double y) => bar.TranslatePoint(new Point(bar.Bounds.Width / 2, y), w)!.Value;

    [AvaloniaFact]
    public async Task 세로_스크롤_막대는_개요_막대_하나뿐이다()
    {
        var w = CompareView.Open(null, Write("l.txt", Lines("a", 400)), Write("r.txt", Lines("a", 400) + "extra\n"));
        await Settle();
        var (l, r) = Editors(w);
        foreach (var editor in new[] { l, r })
        {
            var bar = editor.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(b => b.Orientation == Orientation.Vertical);
            Assert.True(bar is null || !bar.IsVisible || bar.Bounds.Width == 0, "편집기에 세로 스크롤 막대가 보인다");
        }
        Assert.True(Overview(w).Bounds.Width > 0);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 손잡이를_끌면_양쪽이_함께_스크롤하고_캐럿은_그대로다()
    {
        var w = CompareView.Open(null, Write("l2.txt", Lines("a", 300) + "LEFT\n" + Lines("b", 300)), Write("r2.txt", Lines("a", 300) + "RIGHT\n" + Lines("b", 300)));
        await Settle();
        var (l, r) = Editors(w);
        ScrollerOf(r).Offset = new Vector(0, 0);
        await Settle(5);
        var caret = r.TextArea.Caret.Line;
        var bar = Overview(w);
        var (top, height) = Thumb(bar, r);

        var start = In(w, bar, top + height / 2); // 손잡이 가운데를 잡고
        w.MouseDown(start, MouseButton.Left);
        var end = In(w, bar, top + height / 2 + bar.Bounds.Height * 0.5); // 막대 절반만큼 끌면
        w.MouseMove(end, RawInputModifiers.LeftMouseButton);
        w.MouseUp(end, MouseButton.Left);
        await Settle(10);

        var expected = r.ExtentHeight * 0.5;
        Assert.InRange(r.VerticalOffset, expected * 0.85, expected * 1.15);
        Assert.Equal(r.VerticalOffset, l.VerticalOffset, 1);
        Assert.Equal(caret, r.TextArea.Caret.Line); // 스크롤 막대처럼 보기만 옮긴다
        w.Close();
    }

    [AvaloniaFact]
    public async Task 빈_자리를_누르면_그_자리로_가고_위쪽을_누르면_처음으로_간다()
    {
        var w = CompareView.Open(null, Write("l3.txt", Lines("a", 300) + "LEFT\n" + Lines("b", 300)), Write("r3.txt", Lines("a", 300) + "RIGHT\n" + Lines("b", 300)));
        await Settle();
        var (l, r) = Editors(w);
        ScrollerOf(r).Offset = new Vector(0, 0);
        await Settle(5);
        var bar = Overview(w);

        var down = In(w, bar, bar.Bounds.Height * 0.9);
        w.MouseDown(down, MouseButton.Left);
        w.MouseUp(down, MouseButton.Left);
        await Settle(10);
        Assert.True(r.VerticalOffset > r.ExtentHeight * 0.6, $"그 자리로 가지 않았다: {r.VerticalOffset} / {r.ExtentHeight}");
        Assert.Equal(r.VerticalOffset, l.VerticalOffset, 1);

        var up = In(w, bar, 1);
        w.MouseDown(up, MouseButton.Left);
        w.MouseUp(up, MouseButton.Left);
        await Settle(10);
        Assert.True(r.VerticalOffset < r.ExtentHeight * 0.05, $"처음으로 돌아가지 않았다: {r.VerticalOffset}");
        Assert.Equal(r.VerticalOffset, l.VerticalOffset, 1);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 휠은_편집기와_개요_막대_모두에서_양쪽을_함께_스크롤한다()
    {
        var w = CompareView.Open(null, Write("l4.txt", Lines("a", 400)), Write("r4.txt", Lines("a", 400) + "extra\n"));
        await Settle();
        var (l, r) = Editors(w);
        ScrollerOf(r).Offset = new Vector(0, 0);
        await Settle(5);

        var inEditor = r.TranslatePoint(new Point(200, 200), w)!.Value;
        w.MouseWheel(inEditor, new Vector(0, -5));
        await Settle(10);
        var afterEditor = r.VerticalOffset;
        Assert.True(afterEditor > 0, "편집기 위에서 휠이 안 먹는다");
        Assert.Equal(afterEditor, l.VerticalOffset, 1);

        var bar = Overview(w);
        w.MouseWheel(In(w, bar, bar.Bounds.Height / 2), new Vector(0, -5));
        await Settle(10);
        Assert.True(r.VerticalOffset > afterEditor, "개요 막대 위에서 휠이 안 먹는다");
        Assert.Equal(r.VerticalOffset, l.VerticalOffset, 1);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 다음_차이로_가면_양쪽_편집기가_같은_자리를_보인다()
    {
        var w = CompareView.Open(null, Write("l5.txt", Lines("a", 300) + "LEFT\n" + Lines("b", 100)), Write("r5.txt", Lines("a", 300) + "RIGHT\n" + Lines("b", 100)));
        await Settle();
        var (l, r) = Editors(w);
        Assert.True(r.VerticalOffset > 1000, $"처음 차이로 스크롤되지 않았다: {r.VerticalOffset}"); // 처음 차이(301행)가 보이게
        Assert.Equal(r.VerticalOffset, l.VerticalOffset, 1);
        using (var frame = w.CaptureRenderedFrame())
            frame!.Save(Path.Combine(TestApp.Shots, "compare-scrollbars.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        w.Close();
    }

    [AvaloniaFact]
    public async Task 개요_막대는_넓고_차이_종류별로_선명하게_보인다()
    {
        const string NL = "\n";
        // 바뀜 · 왼쪽에만 · 오른쪽에만 이 파일 곳곳에 있다.
        var left = new List<string>();
        var right = new List<string>();
        for (var i = 0; i < 300; i++)
        {
            if (i == 40) { left.Add("changed-L"); right.Add("changed-R"); continue; }
            if (i == 120) { left.Add("only-left-1"); left.Add("only-left-2"); }
            if (i == 200) { right.Add("only-right-1"); right.Add("only-right-2"); right.Add("only-right-3"); }
            left.Add($"line {i}");
            right.Add($"line {i}");
        }
        var w = CompareView.Open(null, Write("l6.txt", string.Join(NL, left) + NL), Write("r6.txt", string.Join(NL, right) + NL));
        await Settle();
        var bar = Overview(w);
        Assert.True(bar.Bounds.Width >= 20, $"개요 막대가 좁다: {bar.Bounds.Width}");
        using (var frame = w.CaptureRenderedFrame())
            frame!.Save(Path.Combine(TestApp.Shots, "overview-wide.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        w.Close();
    }
}
