using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using DaylightCompare.App;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using DaylightCompare.App.Compare;
using DaylightCompare.Domain.Settings;

namespace DaylightCompare.App.Tests;

/// <summary>줄 단위 바이트 패널: 커서가 있는 행의 양쪽 줄 바이트를 보이고, 켜고 끈 것은 설정에 남는다.</summary>
public sealed class CompareLineBytesTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-bytes").FullName;

    public void Dispose()
    {
        App.Settings.Update(s => s with { CompareShowBytes = false });
        Directory.Delete(_dir, recursive: true);
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

    string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
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

    static string[] RowTexts(Window w)
    {
        var panel = w.GetVisualDescendants().OfType<Border>().First(b => b.GetType().Name == "LineBytesPanel");
        return [.. panel.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Inlines is { Count: > 0 })
            .Select(t => string.Concat(t.Inlines!.OfType<Run>().Select(r => r.Text)))];
    }

    [AvaloniaFact]
    public async Task 커서가_있는_행의_양쪽_줄_바이트를_보인다()
    {
        App.Settings.Update(s => s with { CompareShowBytes = true });
        // 둘째 줄: 왼쪽은 공백 + LF, 오른쪽은 탭 + CRLF.
        var l = Write("l.txt", "a\nir0 = &irrx;\nz\n"u8.ToArray());
        var r = Write("r.txt", "a\r\nir0\t= &irrx;\r\nz\r\n"u8.ToArray());
        var w = CompareView.Open(null, l, r);
        await Settle();
        var left = w.GetVisualDescendants().OfType<TextEditor>().First();
        await Settle();
        var rows = RowTexts(w);
        Shot(w, "line-bytes");
        Assert.True(rows.Any(t => t.StartsWith("69 72 30 20 3D") && t.EndsWith("3B 0A")), string.Join(" | ", rows));
        Assert.True(rows.Any(t => t.StartsWith("69 72 30 09 3D") && t.EndsWith("3B 0D 0A")), string.Join(" | ", rows));
        w.Close();
    }

    [AvaloniaFact]
    public async Task 끄면_패널이_사라지고_설정에_남는다()
    {
        App.Settings.Update(s => s with { CompareShowBytes = false });
        var l = Write("l2.txt", "a\n"u8.ToArray());
        var w = CompareView.Open(null, l, l);
        await Settle();
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<Border>(), b => b.GetType().Name == "LineBytesPanel" && b.IsVisible);
        var toggle = w.GetVisualDescendants().OfType<ToggleButton>().First(t => t.Content is DaylightCompare.App.Icons.DuoIcon { Icon: "memory" });
        toggle.IsChecked = true;
        await Settle();
        Assert.True(App.Settings.Value.CompareShowBytes);
        Assert.Contains(w.GetVisualDescendants().OfType<Border>(), b => b.GetType().Name == "LineBytesPanel" && b.IsVisible);
        w.Close();
    }
}
