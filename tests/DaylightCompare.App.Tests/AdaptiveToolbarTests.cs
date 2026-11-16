using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaylightCompare.App.Compare;

namespace DaylightCompare.App.Tests;

/// <summary>비교 창 툴바: 폭이 넉넉하면 아이콘 위·글자 아래, 모자라면 글자를 떼고, 더 모자라면 » 로 접는다.</summary>
public sealed class AdaptiveToolbarTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-toolbar").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static async Task Settle(int rounds = 40)
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
        var path = Path.Combine(dir, name + ".png");
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()?.Save(path);
        return path;
    }

    static AdaptiveToolbar Bar(Window w) => w.GetVisualDescendants().OfType<AdaptiveToolbar>().First();

    /// <summary>툴바 안에서 실제로 보이는(잘리는 곳 밖이 아닌) 글자 수와 » 단추가 보이는지.</summary>
    static (int Labels, bool More) Probe(Window w)
    {
        var bar = Bar(w);
        var labels = bar.Children.OfType<Button>().Count(b => b.Content is Grid && b.Bounds.X >= 0); // 글자 있는 모양의 단추
        var more = bar.Children[0] is Button { IsVisible: true } m && m.Bounds.X >= 0; // 첫 자식이 » 단추
        return (labels, more);
    }

    string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    [AvaloniaTheory]
    [InlineData(1400)]
    [InlineData(900)]
    [InlineData(700)]
    [InlineData(520)]
    public async Task 텍스트_비교_툴바가_폭에_맞춰_글자를_떼고_접는다(int width)
    {
        var l = Write("l.txt", "a\nX\nc\n");
        var r = Write("r.txt", "a\nx\nc\n");
        var w = CompareView.Open(null, l, r);
        w.Width = width;
        await Settle();
        Shot(w, $"toolbar-text-{width}");
        var (labels, more) = Probe(w);
        if (width >= 1400) { Assert.True(labels >= 10, $"글자 {labels}"); Assert.False(more); }
        if (width <= 520) Assert.True(more);
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(1400)]
    [InlineData(900)]
    [InlineData(700)]
    [InlineData(520)]
    public async Task 폴더_비교_툴바가_폭에_맞춰_글자를_떼고_접는다(int width)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "a"));
        Directory.CreateDirectory(Path.Combine(_dir, "b"));
        File.WriteAllText(Path.Combine(_dir, "a", "x.txt"), "1");
        File.WriteAllText(Path.Combine(_dir, "b", "y.txt"), "2");
        var w = FolderCompareView.Open(null, Path.Combine(_dir, "a"), Path.Combine(_dir, "b"));
        w.Width = width;
        await Settle(80);
        Shot(w, $"toolbar-folder-{width}");
        var (labels, more) = Probe(w);
        if (width >= 1400) { Assert.True(labels >= 10, $"글자 {labels}"); Assert.False(more); }
        if (width <= 520) Assert.True(more);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 언어를_바꾸면_툴바_글자도_바뀐다()
    {
        var l = Write("l.txt", "a\nX\n");
        var r = Write("r.txt", "a\nx\n");
        var w = CompareView.Open(null, l, r);
        w.Width = 1400;
        await Settle();
        string[] Labels() => [.. Bar(w).GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];
        try
        {
            Strings.Apply(Domain.Settings.AppLocale.English);
            await Settle();
            Assert.Contains("Next diff", Labels());
            Strings.Apply(Domain.Settings.AppLocale.Korean);
            await Settle();
            Assert.Contains("다음 차이", Labels());
        }
        finally { Strings.Apply(Domain.Settings.AppLocale.Korean); w.Close(); }
    }
}
