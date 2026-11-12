using DaylightCompare.Domain.Cli;
using DaylightCompare.Domain.Policy;
using DaylightCompare.Services.Cli;

namespace DaylightCompare.Services.Tests;

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
    public async Task 줄바꿈만_다른_텍스트는_같다()
    {
        var a = Write("a.txt", "one\ntwo\n"u8.ToArray());
        var b = Write("b.txt", "one\r\ntwo\r\n"u8.ToArray());
        Assert.Equal(ExitCode.Same, await QuickCompare.RunAsync(a, b, Policy));
    }

    [Fact]
    public async Task 내용이_다르면_다르다()
    {
        var a = Write("a.txt", "one\n"u8.ToArray());
        var b = Write("b.txt", "two\n"u8.ToArray());
        Assert.Equal(ExitCode.Different, await QuickCompare.RunAsync(a, b, Policy));
    }

    [Fact]
    public async Task 바이너리는_바이트로_비교한다()
    {
        byte[] bytes = [0, 1, 2, 3, 0, 255];
        var a = Write("a.bin", bytes);
        var b = Write("b.bin", bytes);
        var c = Write("c.bin", [0, 1, 2, 3, 0, 254]);
        Assert.Equal(ExitCode.Same, await QuickCompare.RunAsync(a, b, Policy));
        Assert.Equal(ExitCode.Different, await QuickCompare.RunAsync(a, c, Policy));
    }

    [Fact]
    public async Task 없는_파일과_폴더는_오류다()
    {
        var a = Write("a.txt", "x\n"u8.ToArray());
        Assert.Equal(ExitCode.Error, await QuickCompare.RunAsync(a, Path.Combine(_dir, "missing.txt"), Policy));
        Assert.Equal(ExitCode.Error, await QuickCompare.RunAsync(a, _dir, Policy));
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
        Assert.Equal(["--wait", "--", "C:\\한글 경로\\a.txt", "b \"q\".txt"], received);
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
