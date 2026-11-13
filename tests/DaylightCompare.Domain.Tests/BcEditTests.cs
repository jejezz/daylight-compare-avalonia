using DaylightCompare.Domain.Diff;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Tests;

/// <summary>BC Next / Previous Difference(줄 단위).</summary>
public sealed class BcEditTests
{
    static AlignedDiff Align(string l, string r) => new(TextDiff.Compare(l, r, ComparePolicy.Builtin.Resolve("a.txt")));

    [Fact]
    public void 줄_단위_다음_차이는_구간_안에서도_한_행씩()
    {
        var a = Align("a\nB1\nB2\nd\nE\n", "a\nb1\nb2\nd\ne\n");
        Assert.Equal(1, a.NextDifferenceRow(-1));
        Assert.Equal(2, a.NextDifferenceRow(1));
        Assert.Equal(4, a.NextDifferenceRow(2));
        Assert.Null(a.NextDifferenceRow(4));
        Assert.Equal(2, a.PreviousDifferenceRow(4));
        Assert.Null(a.PreviousDifferenceRow(1));
    }
}
