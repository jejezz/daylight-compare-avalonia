using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using DaylightCompare.App.Compare;

namespace DaylightCompare.App.Tests;

/// <summary>아래 상태 표시줄: 구간 위치, 양쪽 줄:칸·줄 끝, 삽입/덮어쓰기, 읽은 시간.</summary>
public sealed class CompareStatusBarTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-status").FullName;

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

    static string[] Texts(Window w) => [.. w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];

    [AvaloniaFact]
    public async Task 상태_표시줄이_구간_위치와_양쪽_줄_칸_줄_끝을_보인다()
    {
        var l = Write("l.txt", "a\r\nX\r\nc\r\nY\r\n");
        var r = Write("r.txt", "a\nx\nc\ny\n");
        var w = CompareView.Open(null, l, r);
        await Settle();
        var texts = Texts(w);
        Shot(w, "status-bar");
        // 처음 차이(2번째 줄)에 서 있다: 구간 1 / 2, 왼쪽은 CRLF, 오른쪽은 LF.
        Assert.Contains(texts, t => t.Contains("1 / 2") || t.Contains("Section 1 of 2"));
        Assert.Contains(texts, t => t.Contains("2:") && t.EndsWith("CRLF"));
        Assert.Contains(texts, t => t.Contains("2:") && t.EndsWith("LF") && !t.EndsWith("CRLF"));
        Assert.Contains(texts, t => t.Contains("초") && t.Contains("읽은") || t.StartsWith("Load time"));

        // 다음 차이로 가면 구간 2 / 2.
        var left = w.GetVisualDescendants().OfType<TextEditor>().First();
        left.TextArea.Caret.Line = 4;
        await Settle();
        Assert.Contains(Texts(w), t => t.Contains("2 / 2") || t.Contains("Section 2 of 2"));
        w.Close();
    }

    [AvaloniaFact]
    public async Task 줄_끝이_섞인_파일은_혼합으로_보인다()
    {
        var l = Write("l2.txt", "a\r\nb\nc\n");
        var w = CompareView.Open(null, l, l);
        await Settle();
        Assert.Contains(Texts(w), t => t.Contains("혼합") || t.Contains("Mixed"));
        w.Close();
    }
}
