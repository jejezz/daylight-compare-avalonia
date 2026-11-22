using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaylightCompare.App.Compare;

namespace DaylightCompare.App.Tests;

public sealed class CompareWindowTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-ui").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string Write(string name, string text, Encoding? encoding = null)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, (encoding ?? new UTF8Encoding(false)).GetBytes(text));
        return path;
    }

    static async Task Settle(Window w)
    {
        for (var i = 0; i < 40; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static string Shot(Window w, string name)
    {
        var path = Path.Combine(TestApp.Shots, name + ".png");
        using var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        return path;
    }

    const string LeftCode = """
        using System;

        // 계산기 (왼쪽)
        public static class Calc
        {
            public static int Add(int a, int b) => a + b;
            public static int Count = 10;
            public static int Old() => 1;
            public static int Sub(int a, int b) => a - b;   
        }
        """;

    const string RightCode = """
        using System;

        /* 계산기 (오른쪽) */
        public static class Calc
        {
            public static int Add(int a, int b) => a + b;
            public static int Counts = 20;
            public static int Sub(int a, int b) => a - b;
            public static int Mul(int a, int b) => a * b;
            public static int Div(int a, int b) => a / b;
        }
        """;

    [AvaloniaFact]
    public async Task 두_파일을_나란히_비교해_그린다()
    {
        var w = CompareView.Open(null, Write("Calc.cs", LeftCode), Write("Calc2.cs", RightCode));
        w.Width = 1280;
        w.Height = 520;
        await Settle(w);
        var shot = Shot(w, "compare-cs");
        Assert.True(new FileInfo(shot).Length > 10_000);
        Assert.Contains("Calc.cs", w.Title);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 섞인_인코딩은_안내_띠를_보인다()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var mixed = Path.Combine(_dir, "mixed.txt");
        File.WriteAllBytes(mixed, [.. Encoding.UTF8.GetBytes("첫 줄\n둘째 줄\n"), .. Encoding.GetEncoding(949).GetBytes("셋째 줄은 CP949\n")]);
        var w = CompareView.Open(null, mixed, Write("clean.txt", "첫 줄\n둘째 줄\n셋째 줄은 CP949\n"));
        w.Width = 1280;
        w.Height = 360;
        await Settle(w);
        Shot(w, "compare-encoding");
        w.Close();
    }

    [AvaloniaFact]
    public async Task 한쪽만_있으면_빈_쪽_안내를_보인다()
    {
        var w = CompareView.Open(null, Write("only.txt", "a\nb\n"), null);
        w.Width = 1000;
        w.Height = 300;
        await Settle(w);
        Shot(w, "compare-one-side");
        w.Close();
    }

    [AvaloniaFact]
    public async Task 홈_창을_그린다()
    {
        var w = new HomeWindow();
        w.Show();
        await Settle(w);
        Shot(w, "home");
        w.Close();
    }
}
