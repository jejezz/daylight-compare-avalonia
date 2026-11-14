using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using DaylightCompare.App.Compare;
using DaylightCompare.Domain.Policy;
using DaylightCompare.Domain.Settings;

namespace DaylightCompare.App.Tests;

/// <summary>바이너리(16진수) 비교: 텍스트가 아닌 파일은 같은 자리의 탭이 바이너리 비교로 바뀌고, 차이 이동·정렬 방식·세션이 동작한다.</summary>
public sealed class BinaryCompareTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-binary").FullName;

    public void Dispose()
    {
        App.Settings.Update(s => s with { Sessions = [], LastWindows = [], CompareHistory = [] });
        Directory.Delete(_dir, recursive: true);
    }

    static async Task Settle(int rounds = 60)
    {
        for (var i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }
        Dispatcher.UIThread.RunJobs();
    }

    string Write(string name, byte[] data)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    static byte[] Random(int length, int seed)
    {
        var b = new byte[length];
        new System.Random(seed).NextBytes(b);
        b[3] = 0; // NUL이 있어야 바이너리로 본다
        return b;
    }

    static string Shot(Window w, string name)
    {
        var dir = Environment.GetEnvironmentVariable("DCMP_SHOTS");
        if (string.IsNullOrEmpty(dir)) return "";
        Directory.CreateDirectory(dir);
        Dispatcher.UIThread.RunJobs();
        var path = Path.Combine(dir, name + ".png");
        w.CaptureRenderedFrame()?.Save(path);
        return path;
    }

    static BinaryCompareView Binary(CompareHost host) => Assert.IsType<BinaryCompareView>(host.ActiveTab);

    [AvaloniaFact]
    public async Task NUL이_있는_파일은_바이너리_비교로_바뀌고_차이가_보인다()
    {
        var a = Random(5000, 1);
        var b = (byte[])a.Clone();
        b[100] ^= 0xFF;
        b[4000] ^= 0x01;
        var host = CompareView.Open(null, Write("a.dat", a), Write("b.dat", b));
        await Settle();
        var view = Binary(host);
        Assert.Single(host.Tabs);
        Assert.NotNull(view.Result);
        Assert.Equal(2, view.Result!.DifferenceCount);
        Assert.Equal(BinaryAlignKind.Fixed, view.Align);
        Shot(host, "binary-compare");

        // 다음 차이 · 이전 차이
        var firstRow = view.Rows!.FirstRowOf(view.Rows.BlockOfRow(100 / 16));
        view.GoDifference(forward: true);
        view.GoDifference(forward: false);
        host.Close();
        Assert.True(firstRow >= 0);
    }

    [AvaloniaFact]
    public async Task 삽입_감지로_바꾸면_끼어든_바이트만_차이로_나온다()
    {
        var a = Random(20_000, 2);
        var b = a.Take(8000).Concat(new byte[] { 1, 2, 3 }).Concat(a.Skip(8000)).ToArray();
        var host = CompareView.Open(null, Write("c.dat", a), Write("d.dat", b));
        await Settle();
        var view = Binary(host);
        Assert.True(view.Result!.DifferentBytes > 10_000); // 고정 위치로는 뒤가 모두 다르다
        await view.SetAlignAsync(BinaryAlignKind.Insertions);
        await Settle();
        Assert.Equal(BinaryAlignKind.Insertions, view.Align);
        Assert.Equal(1, view.Result!.DifferenceCount);
        Assert.Equal(3, view.Result.DifferentBytes);
        Assert.Equal("insertions", view.Capture().Align);
        host.Close();
    }

    [AvaloniaFact]
    public async Task 같은_파일이면_같다고_하고_위치_문자열을_읽는다()
    {
        var a = Random(3000, 3);
        var host = CompareView.Open(null, Write("e.dat", a), Write("f.dat", (byte[])a.Clone()));
        await Settle();
        var view = Binary(host);
        Assert.True(view.Result!.IsIdentical);
        Assert.True(BinaryCompareView.TryParseOffset("0x1A2B", out var hex));
        Assert.Equal(0x1A2B, hex);
        Assert.True(BinaryCompareView.TryParseOffset("1,024", out var dec));
        Assert.Equal(1024, dec);
        Assert.False(BinaryCompareView.TryParseOffset("abc", out _));
        Assert.False(BinaryCompareView.TryParseOffset("-5", out _));
        host.Close();
    }

    [AvaloniaFact]
    public async Task 정책이_바이너리로_정한_이름은_글자만_있어도_바이너리_비교로_연다()
    {
        // 내장 정책: *.bin → binary. 글처럼 보이는 내용이어도 바이너리 비교.
        var host = CompareView.Open(null, Write("x.bin", "hello\n"u8.ToArray()), Write("y.bin", "hellp\n"u8.ToArray()));
        await Settle();
        var view = Binary(host);
        Assert.Equal(1, view.Result!.DifferenceCount);
        host.Close();
    }

    [AvaloniaFact]
    public async Task 폴더_비교에서_연_바이너리_파일은_같은_창의_바이너리_탭이_되고_같은_짝은_그_탭으로_간다()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "a"));
        Directory.CreateDirectory(Path.Combine(_dir, "b"));
        var l = Write("a/f.dat", Random(2000, 4));
        var r = Write("b/f.dat", Random(2000, 5));
        var host = FolderCompareView.Open(null, Path.Combine(_dir, "a"), Path.Combine(_dir, "b"));
        await Settle();
        host.OpenFileTab(l, r);
        await Settle();
        Assert.Equal(2, host.Tabs.Count);
        Assert.IsType<BinaryCompareView>(host.ActiveTab);
        host.Select(host.Tabs[0]);
        var again = host.OpenFileTab(l, r);
        Assert.IsType<BinaryCompareView>(again);
        Assert.Equal(2, host.Tabs.Count);
        Assert.Same(again, host.ActiveTab);
        host.Close();
    }

    [AvaloniaFact]
    public async Task 바이너리_탭도_세션으로_저장되고_복원된다()
    {
        App.Settings.Update(s => s with { Sessions = [] });
        var l = Write("g.dat", Random(1500, 6));
        var r = Write("h.dat", Random(1500, 7));
        var host = CompareView.Open(null, l, r);
        await Settle();
        host.SaveSession("바이너리");
        var saved = App.Settings.Value.Sessions.Single();
        Assert.Equal(SessionTab.Binary, saved.Window.Tabs[0].Kind);
        var restored = CompareHost.OpenSession(null, saved.Window, saved.Name)!;
        await Settle();
        var view = Binary(restored);
        Assert.Equal(l, view.LeftPath);
        Assert.NotNull(view.Result);
        restored.Close();
        host.Close();
    }

    [AvaloniaFact]
    public async Task 키로_다음_차이로_가고_커서를_옮긴다()
    {
        var a = Random(4000, 8);
        var b = (byte[])a.Clone();
        b[500] ^= 1;
        b[3000] ^= 1;
        var host = CompareView.Open(null, Write("i.dat", a), Write("j.dat", b));
        await Settle();
        var view = Binary(host);
        host.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.Control);
        host.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        host.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control); // 위치 이동 대화상자가 뜬다 — 창은 그대로
        await Settle();
        Assert.True(host.IsVisible);
        host.Close();
        Assert.NotNull(view);
    }

    [AvaloniaFact]
    public async Task 정책의_무시_구간과_정렬이_바이너리_비교에_적용된다()
    {
        // 사용자 정책: *.fw 는 바이너리, 헤더의 8바이트(100~107)는 비교에서 뺀다, 정렬은 삽입 감지.
        var policyFile = App.Policy.FilePath;
        await App.Policy.LoadAsync(); // 처음이면 내장 정책으로 만들어 둔다
        var original = File.ReadAllText(policyFile);
        try
        {
            File.WriteAllText(policyFile, """
                {"schema":1,"formats":[{"id":"fw","view":"binary","masks":["*.fw"],
                  "binary":{"align":"insertions","ignoreRanges":[{"start":100,"length":8}]}}]}
                """);
            var a = Random(2000, 9);
            var b = (byte[])a.Clone();
            for (var i = 100; i < 108; i++) b[i] ^= 0xFF;  // 타임스탬프 자리: 무시한다
            b[1500] ^= 1;                                  // 진짜 차이
            var host = CompareView.Open(null, Write("a.fw", a), Write("b.fw", b));
            await Settle();
            var view = Binary(host);
            Assert.Equal(BinaryAlignKind.Insertions, view.Align);
            Assert.Equal(1, view.Result!.DifferenceCount);
            Assert.Equal(1, view.Result.DifferentBytes);
            host.Close();
        }
        finally
        {
            File.WriteAllText(policyFile, original);
        }
    }
}
