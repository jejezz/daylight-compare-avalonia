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

public class CollapsibleRunsTests
{
    static AlignedDiff Align(string l, string r) => new(TextDiff.Compare(l, r, ComparePolicy.Builtin.Resolve("a.txt")));

    static string Lines(int from, int to) => string.Concat(Enumerable.Range(from, to - from).Select(i => $"line{i}\n"));

    [Fact]
    public void 차이_위아래로_문맥만_남기고_접는다()
    {
        // 0..9 같음, 10 바뀜, 11..29 같음
        var a = Align(Lines(0, 10) + "LEFT\n" + Lines(11, 30), Lines(0, 10) + "RIGHT\n" + Lines(11, 30));
        Assert.Equal([(0, 7), (14, 16)], a.CollapsibleRuns(3));
        Assert.Equal([(0, 10), (11, 19)], a.CollapsibleRuns(0));
    }

    [Fact]
    public void 차이_사이가_문맥의_두_배보다_짧으면_접지_않는다()
    {
        var a = Align("a\nX\nb\nc\nd\nY\ne\n", "a\nX2\nb\nc\nd\nY2\ne\n");
        Assert.Empty(a.CollapsibleRuns(2));
        Assert.Equal([(2, 3)], a.CollapsibleRuns(0));
    }

    [Fact]
    public void 한_줄만_남는_구간은_접지_않는다()
    {
        // 접으면 그 자리에 표시 한 행이 남으므로 한 행짜리는 접어도 줄지 않는다.
        var a = Align("a\nb\nX\n", "a\nb\nY\n");
        Assert.Empty(a.CollapsibleRuns(1));
        Assert.Equal([(0, 2)], a.CollapsibleRuns(0));
    }

    [Fact]
    public void 같은_파일은_전체가_한_구간이다()
    {
        var a = Align(Lines(0, 5), Lines(0, 5));
        Assert.Equal([(0, a.Rows.Count)], a.CollapsibleRuns(3));
    }

    [Fact]
    public void 중요하지_않은_차이도_접는다()
    {
        var a = Align("a\nb \nc\nd\nX\n", "a\nb\nc\nd\nY\n");
        Assert.Equal([(0, 4)], a.CollapsibleRuns(0));
    }
}
