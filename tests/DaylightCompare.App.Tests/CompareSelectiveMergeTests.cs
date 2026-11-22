using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using DaylightCompare.App.Compare;

namespace DaylightCompare.App.Tests;

/// <summary>
/// 파일 비교의 병합은 사용자가 고른 줄만 옮긴다: 줄 번호를 누르거나 글을 끌어 고르면 양쪽에 틀이 생기고, 가운데의 → / ← 로 그 줄만 복사한다.
/// 고르지 않으면 아무것도 복사하지 않고, 파일 전체를 고르면 거절한다 (파일 전체 복사는 폴더 비교의 일).
/// </summary>
public sealed class CompareSelectiveMergeTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-merge").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    static async Task Settle(int rounds = 30)
    {
        for (var i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static (TextEditor Left, TextEditor Right) Editors(Window w)
    {
        var editors = w.GetVisualDescendants().OfType<TextEditor>().ToList();
        return (editors[0], editors[1]);
    }

    static Button Arrow(Window w, string arrow) =>
        w.GetVisualDescendants().OfType<Button>().Single(b => b.Name == (arrow == "→" ? "CopyRightButton" : "CopyLeftButton"));

    static void Press(Window w, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None) => w.KeyPressQwerty(key, mods);

    /// <summary>편집기의 줄 번호 여백 안, 그 줄(1부터)의 가운데 위치 (창 좌표).</summary>
    static Point GutterPoint(Window w, TextEditor e, int line)
    {
        var margin = e.TextArea.LeftMargins.OfType<Control>().First(m => m.GetType().Name == "RowNumberMargin");
        var tv = e.TextArea.TextView;
        var y = tv.GetVisualTopByDocumentLine(line) - tv.VerticalOffset + tv.DefaultLineHeight / 2;
        return margin.TranslatePoint(new Point(margin.Bounds.Width / 2, y), w)!.Value;
    }

    static string Status(Window w) => w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").FirstOrDefault(t => t.Contains("줄") || t.Contains("파일") || t.Contains("line") || t.Contains("whole")) ?? "";

    [AvaloniaFact]
    public async Task 줄_번호를_눌러_고르면_양쪽이_틀로_감싸이고_그_줄만_복사된다()
    {
        var w = CompareView.Open(null, Write("l.txt", "a\nL1\nL2\nL3\nz\n"), Write("r.txt", "a\nR1\nR2\nR3\nz\n"));
        await Settle();
        var (l, r) = Editors(w);
        Assert.False(Arrow(w, "→").IsVisible); // 고르기 전에는 → 도 ← 도 없다
        Assert.False(Arrow(w, "←").IsVisible);

        var down = GutterPoint(w, l, 3); // 왼쪽 3번째 줄(L2)의 줄 번호
        w.MouseDown(down, MouseButton.Left);
        w.MouseUp(down, MouseButton.Left);
        await Settle(10);
        Assert.True(Arrow(w, "→").IsVisible);
        Assert.False(Arrow(w, "←").IsVisible);
        using (var frame = w.CaptureRenderedFrame())
            frame!.Save(Path.Combine(TestApp.Shots, "merge-selected-line.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        Arrow(w, "→").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Settle();
        Assert.Equal("a\nR1\nL2\nR3\nz", r.Document.Text); // 고른 한 줄만 — 나머지는 그대로
        Assert.Equal("a\nL1\nL2\nL3\nz", l.Document.Text);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 글을_끌어_고른_여러_줄만_복사하고_되돌리면_원래대로다()
    {
        var w = CompareView.Open(null, Write("l2.txt", "h\nL1\nL2\nL3\nt\n"), Write("r2.txt", "h\nR1\nR2\nR3\nt\n"));
        await Settle();
        var (l, r) = Editors(w);

        var from = r.Document.GetLineByNumber(2); // 오른쪽에서 두 줄을 고른다
        r.Select(from.Offset, r.Document.GetLineByNumber(3).EndOffset - from.Offset);
        await Settle(5);
        Assert.True(Arrow(w, "←").IsVisible); // 오른쪽에서 골랐으니 ←
        Assert.False(Arrow(w, "→").IsVisible);

        Arrow(w, "←").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Settle();
        Assert.Equal("h\nR1\nR2\nL3\nt", l.Document.Text);
        Assert.Equal("h\nR1\nR2\nR3\nt", r.Document.Text);

        Press(w, PhysicalKey.Z, RawInputModifiers.Control); // 되돌리기는 사용자 몫
        await Settle();
        Assert.Equal("h\nL1\nL2\nL3\nt", l.Document.Text);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 파일_내용이_전부_달라도_전체를_고르면_복사하지_않는다()
    {
        var w = CompareView.Open(null, Write("l3.txt", "1\n2\n3\n"), Write("r3.txt", "a\nb\nc\n"));
        await Settle();
        var (l, r) = Editors(w);

        l.SelectAll(); // 파일 전체
        await Settle(5);
        Assert.True(Arrow(w, "→").IsVisible);
        Assert.False(Arrow(w, "→").IsEnabled); // 누를 수 없다

        Press(w, PhysicalKey.R, RawInputModifiers.Control); // 단축키로도 안 된다
        await Settle();
        Assert.Equal("a\nb\nc", r.Document.Text);
        Assert.DoesNotContain("●", w.Title);

        var one = l.Document.GetLineByNumber(2); // 일부만 고르면 된다
        l.Select(one.Offset, one.Length);
        await Settle(5);
        Assert.True(Arrow(w, "→").IsEnabled);
        Arrow(w, "→").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Settle();
        Assert.Equal("a\n2\nc", r.Document.Text);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 고르지_않고_복사_키를_누르면_아무것도_바뀌지_않는다()
    {
        var w = CompareView.Open(null, Write("l4.txt", "a\nL\nz\n"), Write("r4.txt", "a\nR\nz\n"));
        await Settle();
        var (_, r) = Editors(w);
        Press(w, PhysicalKey.R, RawInputModifiers.Control);
        await Settle();
        Press(w, PhysicalKey.L, RawInputModifiers.Control);
        await Settle();
        Assert.Equal("a\nR\nz", r.Document.Text);
        Assert.DoesNotContain("●", w.Title);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Esc는_고른_범위를_풀고_틀과_단추를_지운다()
    {
        var w = CompareView.Open(null, Write("l5.txt", "a\nL1\nL2\nz\n"), Write("r5.txt", "a\nR1\nR2\nz\n"));
        await Settle();
        var (l, _) = Editors(w);
        var one = l.Document.GetLineByNumber(2);
        l.Select(one.Offset, one.Length);
        await Settle(5);
        Assert.True(Arrow(w, "→").IsVisible);

        Press(w, PhysicalKey.Escape);
        await Settle(5);
        Assert.False(Arrow(w, "→").IsVisible);
        w.Close();
    }
}

/// <summary>한쪽에는 줄이 없고(채움 줄) 맞은편에는 빈 줄이 있을 때 — 없는 줄을 골라 그 빈 줄을 없앨 수 있어야 한다.</summary>
public sealed class CompareMergeAbsenceTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-absence").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    static async Task Settle(int rounds = 30)
    {
        for (var i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static (TextEditor Left, TextEditor Right) Editors(Window w)
    {
        var editors = w.GetVisualDescendants().OfType<TextEditor>().ToList();
        return (editors[0], editors[1]);
    }

    static Button Arrow(Window w, string arrow) =>
        w.GetVisualDescendants().OfType<Button>().Single(b => b.Name == (arrow == "→" ? "CopyRightButton" : "CopyLeftButton"));

    static Point Inside(Window w, Control c, double x, double y) => c.TranslatePoint(new Point(x, y), w)!.Value;

    /// <summary>줄(1부터)의 세로 가운데 (편집기 안 좌표).</summary>
    static double LineY(TextEditor e, int line)
    {
        var tv = e.TextArea.TextView;
        return tv.GetVisualTopByDocumentLine(line) - tv.VerticalOffset + tv.DefaultLineHeight / 2;
    }

    [AvaloniaFact]
    public async Task 채움_줄의_줄_번호_자리를_눌러_고르고_맞은편의_빈_줄을_없앤다()
    {
        // 왼쪽에는 없는 줄 / 오른쪽에는 빈 줄 (행 2)
        var w = CompareView.Open(null, Write("l.txt", "a\nz\n"), Write("r.txt", "a\n\nz\n"));
        await Settle();
        var (l, r) = Editors(w);
        Assert.Equal("a\n\nz", l.Document.Text); // 화면에서는 왼쪽 행 2가 채움 줄

        var margin = l.TextArea.LeftMargins.OfType<Control>().First(m => m.GetType().Name == "RowNumberMargin");
        var p = Inside(w, margin, margin.Bounds.Width / 2, LineY(l, 2));
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        await Settle(10);
        Assert.True(Arrow(w, "→").IsVisible, "채움 줄의 줄 번호 자리를 눌러도 → 가 뜨지 않는다");
        Assert.True(Arrow(w, "→").IsEnabled);
        using (var frame = w.CaptureRenderedFrame())
            frame!.Save(Path.Combine(TestApp.Shots, "merge-absent-line.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        Arrow(w, "→").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Settle();
        Assert.Equal("a\nz", r.Document.Text); // 오른쪽의 빈 줄이 없어졌다
        Assert.Equal("a\nz", l.Document.Text);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 채움_줄_자체를_눌러도_그_행이_골라진다()
    {
        var w = CompareView.Open(null, Write("l2.txt", "a\nz\n"), Write("r2.txt", "a\n\nz\n"));
        await Settle();
        var (l, r) = Editors(w);

        var p = Inside(w, l, 200, LineY(l, 2)); // 왼쪽 편집기 글 자리의 채움 줄
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        await Settle(10);
        Assert.True(Arrow(w, "→").IsVisible, "채움 줄을 눌러도 고른 것으로 보지 않는다");

        Arrow(w, "→").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Settle();
        Assert.Equal("a\nz", r.Document.Text);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 반대_방향도_된다_오른쪽의_없는_줄로_왼쪽의_빈_줄을_없앤다()
    {
        var w = CompareView.Open(null, Write("l3.txt", "a\n\nz\n"), Write("r3.txt", "a\nz\n"));
        await Settle();
        var (l, r) = Editors(w);
        var p = Inside(w, r, 200, LineY(r, 2));
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        await Settle(10);
        Assert.True(Arrow(w, "←").IsVisible);
        Arrow(w, "←").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Settle();
        Assert.Equal("a\nz", l.Document.Text);
        w.Close();
    }
}
