using DaylightCompare.Domain.Settings;

namespace DaylightCompare.Domain.Tests;

public sealed class CompareHistoryTests
{
    static CompareHistoryEntry E(string l, string r, string kind = CompareHistoryEntry.Files, int minute = 0) =>
        new() { Left = l, Right = r, Kind = kind, OpenedAt = new DateTimeOffset(2026, 11, 13, 9, minute, 0, TimeSpan.Zero) };

    static string[] Pairs(IEnumerable<CompareHistoryEntry> list) => [.. list.Select(e => $"{e.Left}|{e.Right}")];

    [Fact]
    public void 새_비교는_맨_앞에_들어간다()
    {
        var list = CompareHistoryList.Record([E("a", "b")], E("c", "d"), ignoreCase: false);
        Assert.Equal(["c|d", "a|b"], Pairs(list));
    }

    [Fact]
    public void 같은_쌍을_다시_열면_하나로_합쳐_앞으로_올리고_시각만_새로_바뀐다()
    {
        var list = CompareHistoryList.Record([E("a", "b", minute: 1), E("c", "d", minute: 2)], E("a", "b", minute: 9), ignoreCase: false);
        Assert.Equal(["a|b", "c|d"], Pairs(list));
        Assert.Equal(9, list[0].OpenedAt.Minute);
    }

    [Fact]
    public void 좌우를_바꾼_쌍은_다른_비교다() =>
        Assert.Equal(["b|a", "a|b"], Pairs(CompareHistoryList.Record([E("a", "b")], E("b", "a"), ignoreCase: false)));

    [Fact]
    public void 대소문자는_옵션에_따라_가린다()
    {
        Assert.Single(CompareHistoryList.Record([E(@"C:\Src", @"C:\Dst")], E(@"c:\src", @"c:\dst"), ignoreCase: true));
        Assert.Equal(2, CompareHistoryList.Record([E("/Src", "/Dst")], E("/src", "/dst"), ignoreCase: false).Count);
    }

    [Fact]
    public void 최대_개수를_넘으면_가장_오래된_것부터_버린다()
    {
        IReadOnlyList<CompareHistoryEntry> list = [];
        for (var i = 0; i < CompareHistoryList.Max + 10; i++) list = CompareHistoryList.Record(list, E($"l{i}", $"r{i}"), ignoreCase: false);
        Assert.Equal(CompareHistoryList.Max, list.Count);
        Assert.Equal($"l{CompareHistoryList.Max + 9}", list[0].Left);
        Assert.DoesNotContain(list, e => e.Left == "l0");
    }

    [Fact]
    public void 경로가_비어_있으면_기록하지_않는다()
    {
        var start = new[] { E("a", "b") };
        Assert.Same(start, CompareHistoryList.Record(start, E("a", ""), ignoreCase: false));
        Assert.Same(start, CompareHistoryList.Record(start, E(" ", "b"), ignoreCase: false));
    }

    [Fact]
    public void 한_줄을_뺀다()
    {
        var list = CompareHistoryList.Remove([E("a", "b"), E("c", "d")], E("a", "b"), ignoreCase: false);
        Assert.Equal(["c|d"], Pairs(list));
    }

    [Fact]
    public void 폴더_종류를_알린다()
    {
        Assert.True(E("a", "b", CompareHistoryEntry.Folders).IsFolders);
        Assert.False(E("a", "b").IsFolders);
    }
}
