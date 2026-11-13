using DaylightCompare.Domain.Cli;
using DaylightCompare.Domain.Policy;
using DaylightCompare.Services.Cli;

namespace DaylightCompare.Services.Tests;

/// <summary>BC <c>/qc</c>와 같은 종료 코드 (BC_COMPAT.md §4).</summary>
public sealed class QuickCompareTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-cli").FullName;
    static readonly ComparePolicy Policy = ComparePolicy.Builtin;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public async Task 규칙_기준_종료_코드는_BC와_같다()
    {
        var a = Write("a.txt", "one\ntwo\n"u8.ToArray());
        Assert.Equal(ExitCode.BinarySame, await QuickCompare.RunAsync(a, Write("same.txt", "one\ntwo\n"u8.ToArray()), Policy));
        Assert.Equal(ExitCode.RulesSame, await QuickCompare.RunAsync(a, Write("crlf.txt", "one\r\ntwo\r\n"u8.ToArray()), Policy));
        var trailing = Write("ws.txt", "one  \ntwo\n"u8.ToArray());
        Assert.Equal(ExitCode.Similar, await QuickCompare.RunAsync(a, trailing, Policy));
        Assert.Equal(ExitCode.RulesSame, await QuickCompare.RunAsync(a, trailing, Policy, ignoreUnimportant: true));
        Assert.Equal(ExitCode.RulesDifferent, await QuickCompare.RunAsync(a, Write("b.txt", "one\nTWO\n"u8.ToArray()), Policy));
    }

    [Fact]
    public async Task 바이너리_크기_비교는_1과_11()
    {
        byte[] bytes = [0, 1, 2, 0, 255]; // 홀수 길이 — UTF-16으로 읽히지 않게
        var a = Write("a.bin", bytes);
        var b = Write("b.bin", bytes);
        var c = Write("c.bin", [0, 1, 2, 0, 254]);
        Assert.Equal(ExitCode.BinarySame, await QuickCompare.RunAsync(a, b, Policy));
        Assert.Equal(ExitCode.BinaryDifferent, await QuickCompare.RunAsync(a, c, Policy));
        Assert.Equal(ExitCode.BinarySame, await QuickCompare.RunAsync(a, c, Policy, QuickCompareKind.Size));
        // 바이트 비교는 줄바꿈만 다른 텍스트도 다르다고 본다.
        var t1 = Write("t1.txt", "x\n"u8.ToArray());
        var t2 = Write("t2.txt", "x\r\n"u8.ToArray());
        Assert.Equal(ExitCode.BinaryDifferent, await QuickCompare.RunAsync(t1, t2, Policy, QuickCompareKind.Binary));
    }

    [Fact]
    public async Task 없는_파일과_폴더는_100()
    {
        var a = Write("a.txt", "x\n"u8.ToArray());
        Assert.Equal(ExitCode.Error, await QuickCompare.RunAsync(a, Path.Combine(_dir, "missing.txt"), Policy));
        Assert.Equal(ExitCode.Error, await QuickCompare.RunAsync(a, _dir, Policy));
        Assert.Equal(100, (int)ExitCode.Error);
    }

    [Fact]
    public void 상대_경로를_현재_폴더_기준으로_바꾸고_압축_안_경로는_지킨다()
    {
        var cwd = _dir;
        Assert.Equal(Path.Combine(cwd, "a.txt"), QuickCompare.ResolvePath("a.txt", cwd));
        Assert.Equal(Path.Combine(cwd, "x.zip") + "!/inner/b.txt", QuickCompare.ResolvePath("x.zip!/inner/b.txt", cwd));
    }
}

public sealed class InstanceChannelTests
{
    static string UniqueName() => "dcmp-test-" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task 인자를_넘기고_돌려준_코드를_받는다()
    {
        var name = UniqueName();
        using var stop = new CancellationTokenSource();
        string[]? received = null;
        var server = InstanceChannel.ServeAsync(name, args => { received = args; return Task.FromResult(1); }, stop.Token);

        var code = await InstanceChannel.SendAsync(name, ["--wait", "--", "C:\\한글 경로\\a.txt", "b \"q\".txt"], TimeSpan.FromSeconds(5));
        Assert.Equal(1, code);
        Assert.Equal(["--wait", "--", "C:\\한글 경로\\a.txt", "b \"q\".txt"], received!);
        stop.Cancel();
        await server;
    }

    [Fact]
    public async Task 오래_기다리는_연결이_있어도_다음_연결을_받는다()
    {
        var name = UniqueName();
        using var stop = new CancellationTokenSource();
        var release = new TaskCompletionSource<int>();
        var server = InstanceChannel.ServeAsync(name, args => args[0] == "wait" ? release.Task : Task.FromResult(0), stop.Token);

        var waiting = InstanceChannel.SendAsync(name, ["wait"], TimeSpan.FromSeconds(5));
        Assert.Equal(0, await InstanceChannel.SendAsync(name, ["quick"], TimeSpan.FromSeconds(5)));
        Assert.False(waiting.IsCompleted);
        release.SetResult(2);
        Assert.Equal(2, await waiting);
        stop.Cancel();
        await server;
    }

    [Fact]
    public async Task 본체가_없으면_null()
    {
        Assert.Null(await InstanceChannel.SendAsync(UniqueName(), ["x"], TimeSpan.FromMilliseconds(200)));
    }
}

public sealed class QuickCompareFolderTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-qcf").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string Put(string side, string rel, string text)
    {
        var path = Path.Combine(_dir, side, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public async Task 폴더_둘은_모두_같으면_2_다른_것이_있으면_13()
    {
        Put("l", "a.txt", "x\n"); Put("r", "a.txt", "x\r\n"); // 줄바꿈만 다름 → 규칙으로 같음
        Put("l", "s/b.txt", "y\n"); Put("r", "s/b.txt", "y\n");
        var l = Path.Combine(_dir, "l");
        var r = Path.Combine(_dir, "r");
        Assert.Equal(ExitCode.RulesSame, await QuickCompare.RunAsync(l, r, ComparePolicy.Builtin));

        Put("r", "only.txt", "z");
        Assert.Equal(ExitCode.RulesDifferent, await QuickCompare.RunAsync(l, r, ComparePolicy.Builtin)); // 한쪽에만 있는 것
        File.Delete(Path.Combine(r, "only.txt"));
        File.WriteAllText(Path.Combine(r, "s", "b.txt"), "changed\n");
        Assert.Equal(ExitCode.RulesDifferent, await QuickCompare.RunAsync(l, r, ComparePolicy.Builtin));
    }

    [Fact]
    public async Task 폴더와_파일을_섞으면_오류다()
    {
        var file = Put("l", "a.txt", "x");
        Assert.Equal(ExitCode.Error, await QuickCompare.RunAsync(Path.Combine(_dir, "l"), file, ComparePolicy.Builtin));
    }
}
