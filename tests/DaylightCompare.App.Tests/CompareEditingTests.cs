using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using DaylightCompare.App.Compare;

namespace DaylightCompare.App.Tests;

/// <summary>비교 창의 편집·블록 복사·실행 취소·저장 (키 입력으로).</summary>
public sealed class CompareEditingTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-edit").FullName;

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

    static (TextEditor Left, TextEditor Right) Editors(Window w)
    {
        var editors = w.GetVisualDescendants().OfType<TextEditor>().ToList();
        return (editors[0], editors[1]);
    }

    static void Press(Window w, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None) => w.KeyPressQwerty(key, mods);

    [AvaloniaFact]
    public async Task 차이를_오른쪽으로_복사하고_되돌리고_다시_한_뒤_저장한다()
    {
        var left = Write("l.txt", "a\nLEFT\nc\n");
        var right = Write("r.txt", "a\nright\nc\n");
        var w = CompareWindow.Open(null, left, right);
        await Settle();
        var (_, r) = Editors(w);
        Assert.Equal(2, r.TextArea.Caret.Line); // 처음 차이로 이동해 있다

        Press(w, PhysicalKey.R, RawInputModifiers.Control); // BC Copy to Right
        await Settle();
        Assert.Equal("a\nLEFT\nc", r.Document.Text);
        Assert.StartsWith("●", w.Title);

        Press(w, PhysicalKey.Z, RawInputModifiers.Control);
        await Settle();
        Assert.Equal("a\nright\nc", r.Document.Text);
        Press(w, PhysicalKey.Y, RawInputModifiers.Control);
        await Settle();
        Assert.Equal("a\nLEFT\nc", r.Document.Text);

        r.TextArea.Focus();
        Press(w, PhysicalKey.S, RawInputModifiers.Control);
        await Settle();
        Assert.Equal("a\nLEFT\nc\n", File.ReadAllText(right));
        Assert.DoesNotContain("●", w.Title);
        var backups = Path.Combine(TestApp.DataDir, "DaylightCompare", "backups");
        Assert.Contains(Directory.EnumerateFiles(backups, "r.txt.*.bak", SearchOption.AllDirectories), b => File.ReadAllText(b) == "a\nright\nc\n");
        w.Close();
    }

    [AvaloniaFact]
    public async Task 채움_줄에_글을_쓰면_실제_줄이_되고_다시_비교한다()
    {
        var left = Write("l2.txt", "a\nb\nc\n");
        var right = Write("r2.txt", "a\nc\n");
        var w = CompareWindow.Open(null, left, right);
        await Settle();
        var (_, r) = Editors(w);
        Assert.Equal("a\n\nc", r.Document.Text); // 2번째 행은 채움 줄

        // 채움 줄(2행)에 "b"를 쓴다 → 손을 멈추면 다시 비교되어 두 파일이 같아진다.
        r.Document.Insert(r.Document.GetLineByNumber(2).Offset, "b");
        await Settle(60);
        Assert.Equal("a\nb\nc", r.Document.Text);
        Assert.StartsWith("●", w.Title);

        // 진짜 줄을 고친 뒤 저장하지 않고 닫으면 묻는다 (창이 그대로 남는다).
        w.Close();
        await Settle();
        Assert.True(w.IsVisible);
    }

    [AvaloniaFact]
    public async Task 바깥에서_파일이_바뀌면_자동으로_다시_비교한다()
    {
        var left = Write("l3.txt", "same\n");
        var right = Write("r3.txt", "old\n");
        var w = CompareWindow.Open(null, left, right);
        await Settle();
        var (_, r) = Editors(w);
        Assert.Equal("old", r.Document.Text);

        File.WriteAllText(right, "same\n");
        File.SetLastWriteTimeUtc(right, DateTime.UtcNow.AddSeconds(5));
        await Settle(80);
        Assert.Equal("same", r.Document.Text);
        w.Close();
    }
}
