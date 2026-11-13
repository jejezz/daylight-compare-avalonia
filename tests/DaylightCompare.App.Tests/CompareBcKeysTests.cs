using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using DaylightCompare.App.Compare;

namespace DaylightCompare.App.Tests;

/// <summary>Beyond Compare와 같은 키 (BC_COMPAT.md §1).</summary>
public sealed class CompareBcKeysTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-bc").FullName;

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

    static (TextEditor Left, TextEditor Right) Editors(Window w)
    {
        var editors = w.GetVisualDescendants().OfType<TextEditor>().ToList();
        return (editors[0], editors[1]);
    }

    static void Press(Window w, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None) => w.KeyPressQwerty(key, mods);

    const RawInputModifiers Ctrl = RawInputModifiers.Control;
    const RawInputModifiers CtrlShift = RawInputModifiers.Control | RawInputModifiers.Shift;

    [AvaloniaFact]
    public async Task 다음_차이_구간과_줄_단위_다음_차이()
    {
        // 행: 0 a | 1~2 바뀐 두 줄 | 3 d | 4 바뀜 | 5 f
        var w = CompareWindow.Open(null, Write("l.txt", "a\nB1\nB2\nd\nE\nf\n"), Write("r.txt", "a\nb1\nb2\nd\ne\nf\n"));
        await Settle();
        var (_, r) = Editors(w);
        Assert.Equal(2, r.TextArea.Caret.Line); // 처음 차이로

        Press(w, PhysicalKey.N, Ctrl); // Next Difference Section → 5행
        await Settle(10);
        Assert.Equal(5, r.TextArea.Caret.Line);
        Press(w, PhysicalKey.P, Ctrl); // Previous Difference Section → 2행
        await Settle(10);
        Assert.Equal(2, r.TextArea.Caret.Line);
        Press(w, PhysicalKey.N, CtrlShift); // Next Difference (줄 단위) → 같은 구간의 3행
        await Settle(10);
        Assert.Equal(3, r.TextArea.Caret.Line);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 왼쪽으로_복사와_한_줄만_오른쪽으로_복사()
    {
        var left = Write("l2.txt", "a\nL1\nL2\nz\n");
        var right = Write("r2.txt", "a\nR1\nR2\nz\n");
        var w = CompareWindow.Open(null, left, right);
        await Settle();
        var (l, r) = Editors(w);

        r.TextArea.Focus();
        Press(w, PhysicalKey.R, CtrlShift); // Copy Line to Right: 2행(L1)만
        await Settle();
        Assert.Equal("a\nL1\nR2\nz", r.Document.Text);

        Press(w, PhysicalKey.Z, Ctrl);
        await Settle();
        var from = r.Document.GetLineByNumber(2);
        r.Select(from.Offset, r.Document.GetLineByNumber(3).EndOffset - from.Offset); // 오른쪽에서 고른 두 줄만
        Press(w, PhysicalKey.L, Ctrl); // Copy Selection to Left
        await Settle();
        Assert.Equal("a\nR1\nR2\nz", l.Document.Text);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 구간_선택과_모두_바꾸기()
    {
        var w = CompareWindow.Open(null, Write("l3.txt", "a\nx1\nx2\nb\n"), Write("r3.txt", "a\ny1\ny2\nb\n"));
        await Settle();
        var (_, r) = Editors(w);
        r.TextArea.Focus();
        Press(w, PhysicalKey.D, Ctrl); // Select Section
        await Settle(10);
        Assert.Equal("y1\ny2", r.SelectedText);

        r.TextArea.Caret.Offset = 0;
        Press(w, PhysicalKey.H, Ctrl); // Replace
        await Settle();
        var boxes = w.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsEffectivelyVisible && t.Width == 300).ToList();
        Assert.Equal(2, boxes.Count); // 찾기 + 바꾸기
        boxes[0].Text = "y";
        boxes[1].Text = "x";
        boxes[1].Focus();
        Press(w, PhysicalKey.Enter, Ctrl); // 바꾸기 칸에서 Ctrl+Enter = 모두 바꾸기
        await Settle(60);
        Assert.Equal("a\nx1\nx2\nb", r.Document.Text);
        Assert.StartsWith("●", w.Title);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 칸_제목과_읽기_전용()
    {
        var left = Write("tmp_LOCAL_123.txt", "a\n");
        var right = Write("r4.txt", "b\n");
        var w = CompareWindow.Open(null, left, right, new CompareOptions(LeftTitle: "a.txt (HEAD)", LeftReadOnly: true));
        await Settle();
        var (l, r) = Editors(w);
        Assert.True(l.IsReadOnly);
        Assert.False(r.IsReadOnly);
        Assert.Contains("a.txt (HEAD)", w.Title);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "a.txt (HEAD)");

        var picked = r.Document.GetLineByNumber(1);
        r.Select(picked.Offset, picked.Length);
        Press(w, PhysicalKey.L, Ctrl); // 읽기 전용 쪽으로는 복사하지 않는다
        await Settle();
        Assert.Equal("a", l.Document.Text);
        w.Close();
    }
}
