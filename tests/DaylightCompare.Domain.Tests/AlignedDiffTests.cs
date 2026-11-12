using DaylightCompare.Domain.Diff;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Tests;

public class AlignedDiffTests
{
    static AlignedDiff Align(string l, string r, string name = "a.txt") =>
        new(TextDiff.Compare(l, r, ComparePolicy.Builtin.Resolve(name)));

    [Fact]
    public void 한쪽에만_있는_줄의_맞은편은_채움_줄이다()
    {
        var a = Align("a\nc\n", "a\nb\nc\n");
        Assert.Equal([(0, 0), (null, 1), (1, 2)], a.Rows.Select(r => (r.Left, r.Right)));
        Assert.Equal("a\n\nc", a.LeftText);
        Assert.Equal("a\nb\nc", a.RightText);
        Assert.Equal(a.Rows.Count, a.LeftText.Split('\n').Length);
        Assert.Equal(a.Rows.Count, a.RightText.Split('\n').Length);
    }

    [Fact]
    public void 줄_수가_다른_바뀐_구간은_앞에서부터_짝짓는다()
    {
        var a = Align("x\n1\n2\ny\n", "x\nA\ny\n");
        Assert.Equal([(0, 0), (1, 1), (2, null), (3, 2)], a.Rows.Select(r => (r.Left, r.Right)));
        Assert.Equal(DiffBlockKind.Changed, a.Rows[2].Kind);
        Assert.Equal(3, a.RowOfLeftLine(3));
        Assert.Equal(3, a.RowOfRightLine(2));
    }

    [Fact]
    public void 다음_이전_차이는_중요한_구간만_기본으로_본다()
    {
        // 0: same, 1: unimportant (뒤 공백), 2: same, 3: changed, 4: same, 5: right only
        var a = Align("s\nt \nu\nv\nw\n", "s\nt\nu\nV\nw\nz\n");
        Assert.Equal(3, a.Next(-1)!.Value.StartRow);
        Assert.Equal(1, a.Next(-1, includeUnimportant: true)!.Value.StartRow);
        Assert.Equal(5, a.Next(3)!.Value.StartRow);
        Assert.Null(a.Next(5));
        Assert.Equal(3, a.Previous(5)!.Value.StartRow);
        Assert.Null(a.Previous(3));
        Assert.Equal(DiffBlockKind.Changed, a.RangeAt(3)!.Value.Kind);
        Assert.Null(a.RangeAt(0));
    }

    [Fact]
    public void 빈_파일도_다룬다()
    {
        var a = Align("", "a\n");
        Assert.Equal([(null, 0)], a.Rows.Select(r => (r.Left, r.Right)));
        var e = Align("", "");
        Assert.Empty(e.Rows);
        Assert.Equal("", e.LeftText);
    }
}
