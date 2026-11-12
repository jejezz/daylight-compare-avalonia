using DaylightCompare.Domain.Diff;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Tests;

/// <summary>BC Copy Line to Right / Left, Next / Previous Difference(줄 단위).</summary>
public sealed class BcEditTests
{
    static AlignedDiff Align(string l, string r) => new(TextDiff.Compare(l, r, ComparePolicy.Builtin.Resolve("a.txt")));

    [Fact]
    public void 양쪽에_줄이_있으면_그_줄만_바꾼다()
    {
        var a = Align("a\nL1\nL2\nz\n", "a\nR1\nR2\nz\n");
        Assert.Equal(("a\nL1\nL2\nz\n", "a\nL1\nR2\nz\n"), DiffEdits.CopyRow(a, 1, toRight: true));
        Assert.Equal(("a\nL1\nR2\nz\n", "a\nR1\nR2\nz\n"), DiffEdits.CopyRow(a, 2, toRight: false));
    }

    [Fact]
    public void 원본에만_있으면_같은_자리에_넣고_원본이_채움이면_지운다()
    {
        var a = Align("a\nb\nc\n", "a\nc\n"); // 1행: 왼쪽에만 b
        Assert.Equal(("a\nb\nc\n", "a\nb\nc\n"), DiffEdits.CopyRow(a, 1, toRight: true));
        Assert.Equal(("a\nc\n", "a\nc\n"), DiffEdits.CopyRow(a, 1, toRight: false));
    }

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
