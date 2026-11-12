using DaylightCompare.Domain.Diff;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Tests;

public class DiffEditsTests
{
    static TextDiffResult Diff(string l, string r) => TextDiff.Compare(l, r, ComparePolicy.Builtin.Resolve("a.txt"));

    [Theory]
    [InlineData("a\nb\n", true)]
    [InlineData("a\nb", false)]
    [InlineData("", false)]
    public void 나누고_다시_합치면_같다(string text, bool ends)
    {
        var (lines, e) = DiffEdits.Split(text);
        Assert.Equal(ends, e);
        Assert.Equal(text, DiffEdits.Join(lines, e));
    }

    [Fact]
    public void 화면_글에서_비어_있는_채움_줄만_뺀다()
    {
        var aligned = new AlignedDiff(Diff("a\nc\n", "a\nb\nc\n"));
        var fillers = aligned.Rows.Select((r, i) => (r, i)).Where(x => x.r.Left is null).Select(x => x.i).ToHashSet();
        Assert.Equal("a\nc\n", DiffEdits.RealText(aligned.LeftText, fillers, endsWithNewline: true));
        // 채움 줄에 글을 쓰면 실제 줄이 된다.
        Assert.Equal("a\nnew\nc\n", DiffEdits.RealText("a\nnew\nc", fillers, endsWithNewline: true));
        // 진짜 빈 줄은 남는다.
        Assert.Equal("a\n\nc", DiffEdits.RealText("a\n\nc", new HashSet<int>(), endsWithNewline: false));
    }

    [Fact]
    public void 바뀐_구간을_오른쪽으로_복사한다()
    {
        var d = Diff("x\nleft1\nleft2\ny\n", "x\nright\ny\n");
        var block = d.Blocks.Select((b, i) => (b, i)).First(t => t.b.Kind == DiffBlockKind.Changed).i;
        var (l, r) = DiffEdits.CopyBlock(d, block, toRight: true);
        Assert.Equal("x\nleft1\nleft2\ny\n", l);
        Assert.Equal("x\nleft1\nleft2\ny\n", r);
        Assert.True(Diff(l, r).IsIdentical);
    }

    [Fact]
    public void 한쪽에만_있는_줄을_복사하면_넣고_반대로_복사하면_지운다()
    {
        var d = Diff("a\nc\n", "a\nb\nc\n");
        var only = d.Blocks.Select((b, i) => (b, i)).First(t => t.b.Kind == DiffBlockKind.RightOnly).i;
        Assert.Equal(("a\nb\nc\n", "a\nb\nc\n"), DiffEdits.CopyBlock(d, only, toRight: false)); // 왼쪽에 넣기
        Assert.Equal(("a\nc\n", "a\nc\n"), DiffEdits.CopyBlock(d, only, toRight: true));        // 오른쪽에서 지우기
    }

    [Fact]
    public void 빈_쪽으로_복사하면_원본의_끝_줄바꿈을_따른다()
    {
        var d = Diff("a\nb\n", "");
        Assert.Equal(("a\nb\n", "a\nb\n"), DiffEdits.CopyBlock(d, 0, toRight: true));
    }
}
