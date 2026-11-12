using DaylightCompare.Domain.Cli;

namespace DaylightCompare.Domain.Tests;

/// <summary>Beyond Compare와 같은 명령줄 (BC_COMPAT.md §4).</summary>
public sealed class CommandLineTests
{
    static CliRequest Win(params string[] args) => CommandLine.Parse(args, slashSwitches: true);
    static CliRequest Unix(params string[] args) => CommandLine.Parse(args, slashSwitches: false);

    [Fact]
    public void git_difftool_꼴을_읽는다()
    {
        var r = Win("--wait", @"C:\Temp\a_LOCAL.txt", "b.txt");
        Assert.True(r.IsValid);
        Assert.True(r.Wait);
        Assert.Equal([@"C:\Temp\a_LOCAL.txt", "b.txt"], r.Paths);
    }

    [Fact]
    public void BC_빠른_비교_스위치를_읽는다()
    {
        Assert.Equal(QuickCompareKind.Rules, Win("/qc", "a", "b").QuickCompare);
        Assert.Equal(QuickCompareKind.Binary, Win("/qc=binary", "a", "b").QuickCompare);
        Assert.Equal(QuickCompareKind.Size, Win("-QC=size", "a", "b").QuickCompare); // 대소문자 무시
        Assert.Equal(QuickCompareKind.Crc, Unix("-quickcompare=crc", "a", "b").QuickCompare);
        Assert.Equal(CliError.UnknownOption, Win("/qc=fast", "a", "b").Error);
        Assert.Equal(CliError.QuickCompareNeedsTwoPaths, Win("/qc", "a").Error);
    }

    [Fact]
    public void macOS_Linux에서는_빗금으로_시작하면_경로다()
    {
        var r = Unix("-qc", "/tmp/a.txt", "/tmp/b.txt");
        Assert.True(r.IsValid);
        Assert.Equal(["/tmp/a.txt", "/tmp/b.txt"], r.Paths);
    }

    [Fact]
    public void 칸_제목과_읽기_전용과_중요하지_않은_차이_무시()
    {
        var r = Win("/title1=a.txt (HEAD)", "/righttitle=\"작업 사본\"", "/ro1", "/iu", "l", "r");
        Assert.Equal("a.txt (HEAD)", r.LeftTitle);
        Assert.Equal("작업 사본", r.RightTitle);
        Assert.True(r.LeftReadOnly);
        Assert.False(r.RightReadOnly);
        Assert.True(r.IgnoreUnimportant);
        Assert.True(Win("/ro", "l", "r") is { LeftReadOnly: true, RightReadOnly: true });
        Assert.True(Win("/rro", "l", "r").RightReadOnly);
    }

    [Fact]
    public void 아직_하지_않는_BC_스위치와_모르는_스위치를_가른다()
    {
        Assert.Equal(CliError.UnsupportedOption, Win("/automerge", "a", "b").Error);
        Assert.Equal(CliError.UnsupportedOption, Win("/fv=Hex Compare", "a", "b").Error);
        Assert.True(Win("/fv=\"Text Compare\"", "a", "b").IsValid);
        var unknown = Win("/frobnicate", "a");
        Assert.Equal(CliError.UnknownOption, unknown.Error);
        Assert.Equal("/frobnicate", unknown.ErrorArgument);
    }

    [Fact]
    public void 경로가_셋이면_아직_오류다() => Assert.Equal(CliError.TooManyPaths, Win("a", "b", "c").Error);

    [Theory]
    [InlineData("/?")]
    [InlineData("/h")]
    [InlineData("--help")]
    public void 도움말(string arg) => Assert.True(Win(arg).Help);

    [Fact]
    public void 줄표_뒤는_모두_경로이고_줄표_하나도_경로다()
    {
        Assert.Equal(["/qc", "-x"], Win("--", "/qc", "-x").Paths);
        Assert.Equal(["-"], Win("-").Paths);
    }

    [Fact]
    public void 넘길_인자로_바꿔도_같게_읽힌다()
    {
        var r = new CliRequest(["-odd name", @"C:\x\b.txt"], Wait: true, QuickCompare: null, Help: false,
            LeftTitle: "왼쪽 = HEAD", RightTitle: "", LeftReadOnly: true, IgnoreUnimportant: true);
        foreach (var slash in new[] { true, false })
        {
            var back = CommandLine.Parse(CommandLine.Format(r), slash);
            Assert.True(back.IsValid);
            Assert.Equal(r, back with { Paths = r.Paths });
            Assert.Equal(r.Paths, back.Paths);
        }
    }
}
