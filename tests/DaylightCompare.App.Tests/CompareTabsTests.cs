using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using DaylightCompare.App.Compare;

namespace DaylightCompare.App.Tests;

/// <summary>비교 창의 탭: 폴더 비교에서 파일을 열면 같은 창의 새 탭, 탭 이동·닫기·분리.</summary>
public sealed class CompareTabsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-tabs").FullName;

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

    string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
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

    [AvaloniaFact]
    public async Task 폴더_비교의_파일은_같은_창의_새_탭으로_열고_같은_짝은_그_탭으로_간다()
    {
        var a1 = Write("a/x.txt", "one\ntwo\n");
        var b1 = Write("b/x.txt", "one\nTWO\n");
        var w = FolderCompareView.Open(null, Path.Combine(_dir, "a"), Path.Combine(_dir, "b"));
        await Settle();
        Assert.Single(w.Tabs);
        Assert.NotNull(w.ActiveFolder);

        var file = w.OpenFileTab(a1, b1);
        await Settle();
        Assert.Equal(2, w.Tabs.Count);
        Assert.Same(file, w.ActiveTab);
        Assert.Equal(a1, w.LeftPath);
        Assert.Equal(2, w.GetVisualDescendants().OfType<TextEditor>().Count(e => e.IsVisible));
        Shot(w, "tabs-file-open");

        // 같은 짝은 새 탭이 아니라 그 탭으로.
        w.Select(w.Tabs[0]);
        Assert.NotNull(w.ActiveFolder);
        Assert.Same(file, w.OpenFileTab(a1, b1));
        Assert.Equal(2, w.Tabs.Count);
        Assert.Same(file, w.ActiveTab);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Ctrl_Tab으로_탭을_옮기고_Ctrl_W로_탭을_닫고_마지막이면_창이_닫힌다()
    {
        var a1 = Write("a/x.txt", "one\n");
        var b1 = Write("b/x.txt", "two\n");
        var w = FolderCompareView.Open(null, Path.Combine(_dir, "a"), Path.Combine(_dir, "b"));
        await Settle();
        w.OpenFileTab(a1, b1);
        await Settle();
        Assert.NotNull(w.ActiveFile);

        w.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        Assert.NotNull(w.ActiveFolder);
        w.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.NotNull(w.ActiveFile);

        w.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Control);
        await Settle();
        Assert.Single(w.Tabs);
        Assert.NotNull(w.ActiveFolder);
        Assert.True(w.IsVisible);

        w.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Control);
        await Settle();
        Assert.False(w.IsVisible);
    }

    [AvaloniaFact]
    public async Task 고친_탭을_닫으면_묻고_창은_그대로_남는다()
    {
        var a1 = Write("a/x.txt", "one\n");
        var b1 = Write("b/x.txt", "two\n");
        var w = FolderCompareView.Open(null, Path.Combine(_dir, "a"), Path.Combine(_dir, "b"));
        await Settle();
        var file = w.OpenFileTab(a1, b1);
        await Settle();
        var r = w.GetVisualDescendants().OfType<TextEditor>().Where(e => e.IsVisible).Last();
        r.Document.Insert(0, "x");
        await Settle(60);
        Assert.StartsWith("●", w.Title);

        w.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Control);
        await Settle();
        Assert.Equal(2, w.Tabs.Count); // 저장 여부를 묻는 대화상자가 떠 있다 — 탭은 아직 열려 있다
        Assert.Contains(file, w.Tabs);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 탭을_새_창으로_분리한다()
    {
        var a1 = Write("a/x.txt", "one\n");
        var b1 = Write("b/x.txt", "two\n");
        var w = FolderCompareView.Open(null, Path.Combine(_dir, "a"), Path.Combine(_dir, "b"));
        await Settle();
        var file = w.OpenFileTab(a1, b1);
        await Settle();

        var detach = typeof(CompareHost).GetMethod("Detach", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var tabs = (System.Collections.IList)typeof(CompareHost).GetField("_tabs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(w)!;
        detach.Invoke(w, [tabs[1]]);
        await Settle();

        Assert.Single(w.Tabs);
        Assert.NotNull(w.ActiveFolder);
        Assert.Empty(w.GetVisualDescendants().OfType<TextEditor>());
        w.Close();
    }
}
