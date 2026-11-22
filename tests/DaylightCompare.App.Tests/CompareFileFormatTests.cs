using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaylightCompare.App.Compare;

namespace DaylightCompare.App.Tests;

/// <summary>파일 형식 드롭다운: 형식을 고르면 그 형식의 규칙으로 다시 비교한다 (C 형식은 주석 차이가 중요하지 않다).</summary>
public sealed class CompareFileFormatTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-fileformat").FullName;

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
        File.WriteAllText(path, text);
        return path;
    }

    const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static int Important(CompareView v)
    {
        var state = typeof(CompareView).GetField("_leftState", Any)!.GetValue(v)!;
        var aligned = state.GetType().GetProperty("Diff", Any)!.GetValue(state)!;
        var diff = aligned.GetType().GetProperty("Diff", Any)!.GetValue(aligned)!;
        return (int)diff.GetType().GetProperty("DifferenceCount", Any)!.GetValue(diff)!;
    }

    static async Task SetFormat(CompareView v, string? id) =>
        await (Task)typeof(CompareView).GetMethod("SetFormatAsync", Any)!.Invoke(v, [id])!;

    [AvaloniaFact]
    public async Task 형식을_고르면_그_형식의_규칙으로_다시_비교한다()
    {
        var l = Write("a.c", "int a; // one\n");
        var r = Write("b.c", "int a; // two\n");
        var w = CompareView.Open(null, l, r);
        await Settle();
        var view = w.ActiveFile!;
        Assert.Equal(0, Important(view)); // .c는 c-family: 주석 차이는 중요하지 않다
        var dir = Environment.GetEnvironmentVariable("DCMP_SHOTS");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            Dispatcher.UIThread.RunJobs();
            w.CaptureRenderedFrame()?.Save(Path.Combine(dir, "file-format.png"));
        }

        await SetFormat(view, ""); // 일반 텍스트
        await Settle();
        Assert.Equal(1, Important(view));

        await SetFormat(view, "c-family");
        await Settle();
        Assert.Equal(0, Important(view));

        await SetFormat(view, null); // 자동
        await Settle();
        Assert.Equal(0, Important(view));
        w.Close();
    }

    [AvaloniaFact]
    public async Task 이름으로는_일반_텍스트인_파일도_C_형식을_골라_주석을_무시한다()
    {
        var l = Write("a.dat", "int a; // one\n");
        var r = Write("b.dat", "int a; // two\n");
        var w = CompareView.Open(null, l, r);
        await Settle();
        var view = w.ActiveFile!;
        Assert.Equal(1, Important(view));
        await SetFormat(view, "c-family");
        await Settle();
        Assert.Equal(0, Important(view));
        w.Close();
    }
}
