using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Folding;
using DaylightCompare.App.Compare;

namespace DaylightCompare.App.Tests;

/// <summary>비교 창의 편의 기능: BC 보기 필터(접기), 찾기, 파일 없는 칸(붙여 넣은 글).</summary>
public sealed class CompareConvenienceTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-conv").FullName;

    public void Dispose()
    {
        App.Settings.Update(s => s with { CompareViewFilter = Domain.Settings.CompareViewFilter.All, CompareContextLines = Domain.Settings.AppSettings.DefaultContextLines });
        Directory.Delete(_dir, recursive: true);
    }

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

    static string Shot(Window w, string name)
    {
        var path = Path.Combine(TestApp.Shots, name + ".png");
        using var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        return path;
    }

    static string Lines(int from, int to, string prefix = "line") => string.Concat(Enumerable.Range(from, to - from).Select(i => $"{prefix} {i}\n"));

    static List<FoldingSection> Folds(TextEditor e) =>
        e.TextArea.TextView.GetService(typeof(FoldingManager)) is FoldingManager m ? m.AllFoldings.ToList() : [];

    [AvaloniaFact]
    public async Task 문맥_보기는_양쪽의_같은_행을_똑같이_접는다()
    {
        var left = Write("l.txt", Lines(0, 40) + "LEFT\n" + Lines(41, 80));
        var right = Write("r.txt", Lines(0, 40) + "RIGHT\n" + Lines(41, 80));
        var all = CompareWindow.Open(null, left, right);
        await Settle();
        Assert.Empty(Folds(Editors(all).Right)); // 기본은 Show All
        all.Close();

        App.Settings.Update(s => s with { CompareViewFilter = Domain.Settings.CompareViewFilter.Context, CompareContextLines = 3 });
        var w = CompareWindow.Open(null, left, right);
        await Settle();
        var (l, r) = Editors(w);
        var lf = Folds(l).Select(f => (f.StartOffset, f.EndOffset, f.IsFolded)).ToList();
        Assert.Equal(2, lf.Count);
        Assert.All(lf, f => Assert.True(f.IsFolded));
        Assert.Equal(Folds(l).Select(f => l.Document.GetLineByOffset(f.StartOffset).LineNumber), Folds(r).Select(f => r.Document.GetLineByOffset(f.StartOffset).LineNumber));
        Assert.Equal(1, l.Document.GetLineByOffset(Folds(l)[0].StartOffset).LineNumber);
        Assert.Equal(37, l.Document.GetLineByOffset(Folds(l)[0].EndOffset).LineNumber); // 41행(차이) 위로 3줄(38~40)은 남는다
        Shot(w, "compare-only-differences");

        // 왼쪽 접기 여백의 [+]를 누르면 오른쪽도 함께 펴진다.
        var margin = l.TextArea.LeftMargins.OfType<FoldingMargin>().First();
        var lineHeight = l.TextArea.TextView.DefaultLineHeight;
        var at = margin.TranslatePoint(new Avalonia.Point(margin.Bounds.Width / 2, lineHeight / 2), w)!.Value;
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        await Settle();
        Assert.False(Folds(l)[0].IsFolded);
        Assert.False(Folds(r)[0].IsFolded);
        Assert.True(Folds(r)[1].IsFolded);

        w.Close();

        // Show Same은 반대로 차이 행을 접는다 (한 행짜리는 접지 않으므로 차이를 두 줄로).
        App.Settings.Update(s => s with { CompareViewFilter = Domain.Settings.CompareViewFilter.Same });
        var left2 = Write("l2.txt", Lines(0, 10) + "A\nB\n" + Lines(12, 20));
        var right2 = Write("r2.txt", Lines(0, 10) + "a\nb\n" + Lines(12, 20));
        var same = CompareWindow.Open(null, left2, right2);
        await Settle();
        var folds = Folds(Editors(same).Right);
        Assert.Single(folds);
        Assert.Equal(11, Editors(same).Right.Document.GetLineByOffset(folds[0].StartOffset).LineNumber);
        same.Close();
    }

    [AvaloniaFact]
    public async Task 찾기는_일치를_골라_보이고_접힌_곳이면_편다()
    {
        App.Settings.Update(s => s with { CompareViewFilter = Domain.Settings.CompareViewFilter.Context });
        var left = Write("fl.txt", Lines(0, 30) + "needle here\n" + Lines(31, 60) + "LEFT\n");
        var right = Write("fr.txt", Lines(0, 30) + "needle here\n" + Lines(31, 60) + "RIGHT\n");
        var w = CompareWindow.Open(null, left, right);
        await Settle();
        var (_, r) = Editors(w);
        Assert.Contains(Folds(r), f => f.IsFolded);

        r.TextArea.Focus();
        w.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);
        await Settle();
        var box = w.GetVisualDescendants().OfType<TextBox>().First(t => t.IsVisible && t.Width == 300);
        Assert.True(box.IsFocused);
        box.Text = "NEEDLE";
        await Settle();
        Assert.Equal("needle", r.SelectedText);
        Assert.Equal(31, r.TextArea.Caret.Line);
        Assert.DoesNotContain(Folds(r), f => f.IsFolded && f.StartOffset <= r.SelectionStart && f.EndOffset >= r.SelectionStart);
        Shot(w, "compare-find");

        // 찾기 칸에서 Ctrl+L은 블록 복사(BC Copy to Left)가 아니다 (파일이 그대로).
        w.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.Control);
        await Settle();
        Assert.EndsWith("RIGHT", r.Document.Text);

        w.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await Settle();
        Assert.False(box.IsEffectivelyVisible);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 빈_칸에_글을_쓰면_파일_없이_비교하고_저장을_묻지_않는다()
    {
        var left = Write("sl.txt", "alpha\nbeta\n");
        var w = CompareWindow.Open(null, left, null);
        await Settle();
        var (_, r) = Editors(w);
        Assert.False(r.IsReadOnly);

        r.Document.Text = "alpha\nbeta";
        await Settle(60);
        Assert.Equal("alpha\nbeta", r.Document.Text);
        Assert.DoesNotContain("●", w.Title);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is "두 파일이 같습니다" or "The files are identical");
        Shot(w, "compare-scratch");

        w.Close();
        await Settle();
        Assert.False(w.IsVisible); // 붙여 넣은 글은 저장하지 않으므로 묻지 않고 닫힌다
    }
}
