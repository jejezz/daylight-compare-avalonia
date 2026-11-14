using System.Reflection;
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

/// <summary>패널 머리글의 저장 형식: 줄 끝·인코딩을 바꾸면 고친 상태가 되고, 저장할 때 그 형식으로 쓴다.</summary>
public sealed class CompareFormatTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-format").FullName;

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
        File.WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(text));
        return path;
    }

    const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>오른쪽 머리글을 눌렀을 때 뜨는 메뉴에서 이 글을 가진 항목을 누른다.</summary>
    static void Choose(CompareView view, string header)
    {
        var right = typeof(CompareView).GetField("_right", Any)!.GetValue(view)!;
        var flyout = (MenuFlyout)typeof(CompareView).GetMethod("FormatFlyout", Any)!.Invoke(view, [right])!;
        var item = flyout.Items.OfType<MenuItem>().First(i => i.Header as string == header);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    [AvaloniaFact]
    public async Task 저장할_줄_끝을_바꾸면_고친_상태가_되고_저장하면_그_줄_끝으로_쓴다()
    {
        var l = Write("l.txt", "a\r\nb\r\n");
        var r = Write("r.txt", "a\r\nb\r\n");
        var w = CompareView.Open(null, l, r);
        await Settle();
        var view = w.ActiveFile!;
        Assert.DoesNotContain("●", w.Title);

        Choose(view, "LF (Unix)");
        await Settle();
        Assert.StartsWith("●", w.Title);

        var right = w.GetVisualDescendants().OfType<TextEditor>().Last();
        right.TextArea.Focus();
        w.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);
        await Settle();
        Assert.DoesNotContain("●", w.Title);
        Assert.Equal("a\nb\n", File.ReadAllText(r));
        Assert.Equal("a\r\nb\r\n", File.ReadAllText(l)); // 왼쪽은 그대로

        // 읽을 때와 같은 형식으로 되돌리면 고친 상태가 풀린다 (이제 읽은 형식은 LF).
        Choose(view, "CRLF (Windows)");
        await Settle();
        Assert.StartsWith("●", w.Title);
        Choose(view, "LF (Unix)");
        await Settle();
        Assert.DoesNotContain("●", w.Title);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 저장_인코딩을_UTF8_BOM으로_바꾸면_BOM을_붙여_쓴다()
    {
        var l = Write("l2.txt", "abc\n");
        var r = Write("r2.txt", "abc\n");
        var w = CompareView.Open(null, l, r);
        await Settle();
        Choose(w.ActiveFile!, "UTF-8 BOM");
        await Settle();
        Assert.StartsWith("●", w.Title);
        w.GetVisualDescendants().OfType<TextEditor>().Last().TextArea.Focus();
        w.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);
        await Settle();
        Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(r).Take(3).ToArray());
        w.Close();
    }
}
