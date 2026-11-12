using DaylightCompare.Domain.Cli;

namespace DaylightCompare.Domain.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void git_difftool_꼴을_읽는다()
    {
        var r = CommandLine.Parse(["--wait", "/tmp/a_LOCAL.txt", "b.txt"]);
        Assert.True(r.IsValid);
        Assert.True(r.Wait);
        Assert.Equal(["/tmp/a_LOCAL.txt", "b.txt"], r.Paths);
    }

    [Fact]
    public void 인자가_없으면_비교가_아니다()
    {
        var r = CommandLine.Parse([]);
        Assert.True(r.IsValid);
        Assert.False(r.IsCompare);
    }

    [Fact]
    public void 줄표_뒤는_모두_경로다()
    {
        var r = CommandLine.Parse(["--", "--wait", "-x"]);
        Assert.True(r.IsValid);
        Assert.False(r.Wait);
        Assert.Equal(["--wait", "-x"], r.Paths);
    }

    [Theory]
    [InlineData("-w")]
    [InlineData("--wait")]
    public void 짧은_옵션도_받는다(string option) => Assert.True(CommandLine.Parse([option, "a", "b"]).Wait);

    [Fact]
    public void 모르는_옵션은_오류다()
    {
        var r = CommandLine.Parse(["--frobnicate", "a"]);
        Assert.Equal(CliError.UnknownOption, r.Error);
        Assert.Equal("--frobnicate", r.ErrorArgument);
    }

    [Fact]
    public void 경로가_셋이면_오류다() => Assert.Equal(CliError.TooManyPaths, CommandLine.Parse(["a", "b", "c"]).Error);

    [Fact]
    public void silent는_경로_둘이_필요하다()
    {
        Assert.Equal(CliError.SilentNeedsTwoPaths, CommandLine.Parse(["--silent", "a"]).Error);
        Assert.True(CommandLine.Parse(["-s", "a", "b"]).Silent);
    }

    [Fact]
    public void 줄표_하나는_경로다() => Assert.Equal(["-"], CommandLine.Parse(["-"]).Paths);

    [Fact]
    public void 넘길_인자로_바꿔도_같게_읽힌다()
    {
        var r = new CliRequest(["-odd name", @"C:\x\b.txt"], Wait: true, Silent: false, Help: false);
        var back = CommandLine.Parse(CommandLine.Format(r));
        Assert.True(back.IsValid);
        Assert.True(back.Wait);
        Assert.Equal(r.Paths, back.Paths);
    }
}
