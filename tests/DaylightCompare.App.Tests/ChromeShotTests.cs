using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaylightCompare.App.Compare;
using DaylightCompare.Domain.Theme;

namespace DaylightCompare.App.Tests;

/// <summary>다크 테마에서도 도구 모음·경로 줄 띠와 편집기·목록이 구분되는지 그림으로 확인한다 (DCMP_SHOTS).</summary>
public sealed class ChromeShotTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-chrome").FullName;

    public void Dispose()
    {
        App.Settings.Update(s => s with { Theme = ThemeMode.Light });
        Directory.Delete(_dir, recursive: true);
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

    static void Shot(Window w, string name)
    {
        using var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(Path.Combine(TestApp.Shots, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    [AvaloniaFact]
    public async Task 다크_테마_비교_창과_폴더_비교_창()
    {
        App.Settings.Update(s => s with { Theme = ThemeMode.Dark });
        await Settle(10);
        var l = Path.Combine(_dir, "l.txt");
        var r = Path.Combine(_dir, "r.txt");
        File.WriteAllText(l, "one\ntwo\nthree\n");
        File.WriteAllText(r, "one\nTWO\nthree\nfour\n");
        var w = CompareWindow.Open(null, l, r);
        await Settle();
        Shot(w, "chrome-dark-compare");
        w.Close();

        Directory.CreateDirectory(Path.Combine(_dir, "a"));
        Directory.CreateDirectory(Path.Combine(_dir, "b"));
        File.WriteAllText(Path.Combine(_dir, "a", "x.txt"), "1");
        File.WriteAllText(Path.Combine(_dir, "b", "x.txt"), "22");
        File.WriteAllText(Path.Combine(_dir, "a", "only.txt"), "o");
        var f = FolderCompareWindow.Open(null, Path.Combine(_dir, "a"), Path.Combine(_dir, "b"));
        await Settle();
        Shot(f, "chrome-dark-folder");
        f.Close();
    }
}
