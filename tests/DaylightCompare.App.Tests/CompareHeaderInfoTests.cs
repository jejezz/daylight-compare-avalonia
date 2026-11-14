using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using DaylightCompare.App.Compare;

namespace DaylightCompare.App.Tests;

/// <summary>패널 머리글 둘째 줄: 파일의 수정 시각과 크기 (저장하면 새 값으로).</summary>
public sealed class CompareHeaderInfoTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-header").FullName;

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

    static IEnumerable<string> Texts(Window w) => w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text ?? "");

    [AvaloniaFact]
    public async Task 머리글에_수정_시각과_크기를_보이고_저장하면_새_크기로_바뀐다()
    {
        var l = Path.Combine(_dir, "l.txt");
        var r = Path.Combine(_dir, "r.txt");
        File.WriteAllBytes(l, "abc\n"u8.ToArray());
        File.WriteAllBytes(r, "abcdef\n"u8.ToArray());
        File.SetLastWriteTime(l, new DateTime(2023, 12, 5, 20, 47, 42));
        var w = CompareView.Open(null, l, r);
        await Settle();
        Assert.Contains(Texts(w), t => t.StartsWith("2023-12-05 20:47:42") && (t.EndsWith("4 바이트") || t.EndsWith("4 bytes")));
        Assert.Contains(Texts(w), t => t.EndsWith("7 바이트") || t.EndsWith("7 bytes"));
        var dir = Environment.GetEnvironmentVariable("DCMP_SHOTS");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            w.CaptureRenderedFrame()?.Save(Path.Combine(dir, "header-info.png"));
        }

        var right = w.GetVisualDescendants().OfType<TextEditor>().Last();
        right.Document.Insert(0, "X");
        await Settle(60);
        right.TextArea.Focus();
        w.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);
        await Settle();
        Assert.Contains(Texts(w), t => t.EndsWith("8 바이트") || t.EndsWith("8 bytes"));
        w.Close();
    }
}
