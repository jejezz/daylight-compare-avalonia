using DaylightCompare.Domain.Settings;

namespace DaylightCompare.Domain.Tests;

/// <summary>같은 파일·폴더를 양쪽에 놓은 비교(자기 자신과의 비교)는 최근 비교에 기록하지 않는다.</summary>
public sealed class CompareHistorySelfTests
{
    static CompareHistoryEntry E(string l, string r, string kind = CompareHistoryEntry.Files) =>
        new() { Left = l, Right = r, Kind = kind, OpenedAt = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero) };

    static string[] Pairs(IEnumerable<CompareHistoryEntry> list) => [.. list.Select(e => $"{e.Left}|{e.Right}")];

    [Theory]
    [InlineData(@"C:\work\a.txt", @"C:\work\a.txt", true)]
    [InlineData(@"C:\work\a.txt", @"c:\WORK\A.TXT", true)]       // 대소문자 (Windows·macOS)
    [InlineData(@"C:\work\dir", @"C:\work\dir\", true)]          // 끝 구분자
    [InlineData(@"C:\work\dir", "C:/work/dir", true)]            // 역빗금과 빗금은 같다
    [InlineData(@"C:\work\a.txt", @"C:\work\b.txt", false)]
    [InlineData(@"C:\a\x.txt", @"C:\b\x.txt", false)]            // 이름이 같아도 위치가 다르면 다른 파일
    public void 같은_경로인지_본다(string left, string right, bool expected) =>
        Assert.Equal(expected, CompareHistoryList.IsSamePath(left, right, ignoreCase: true));

    [Fact]
    public void 대소문자를_가리면_다른_경로다() =>
        Assert.False(CompareHistoryList.IsSamePath("/Work/a.txt", "/work/a.txt", ignoreCase: false));

    [Fact]
    public void 자기_자신과의_비교는_기록하지_않는다()
    {
        var start = new[] { E("a", "b") };
        // 목록이 그대로다 (바뀐 것이 없다)
        Assert.Same(start, CompareHistoryList.Record(start, E(@"C:\x\f.txt", @"C:\x\f.txt"), ignoreCase: true));
        Assert.Same(start, CompareHistoryList.Record(start, E(@"C:\x\dir", @"c:\X\DIR\", CompareHistoryEntry.Folders), ignoreCase: true));
    }

    [Fact]
    public void 서로_다른_파일이면_이름이_같아도_기록한다()
    {
        var after = CompareHistoryList.Record([], E(@"C:\a\x.txt", @"C:\b\x.txt"), ignoreCase: true);
        Assert.Single(after);
    }

    [Fact]
    public void 이미_쌓인_자기_자신과의_비교는_정리하고_뺄_것이_없으면_같은_목록을_돌려준다()
    {
        var clean = new[] { E("a", "b"), E("c", "d") };
        Assert.Same(clean, CompareHistoryList.WithoutSelfComparisons(clean, ignoreCase: true));

        var dirty = new[] { E("a", "b"), E("same", "same"), E("c", "d") };
        Assert.Equal(["a|b", "c|d"], Pairs(CompareHistoryList.WithoutSelfComparisons(dirty, ignoreCase: true)));
    }

    [Fact]
    public void 설정을_읽을_때_예전_기록의_자기_자신과의_비교를_뺀다()
    {
        var settings = new AppSettings { CompareHistory = [E("a", "b"), E("same", "same")] };
        Assert.Equal(["a|b"], Pairs(settings.Normalized().CompareHistory));
    }
}
