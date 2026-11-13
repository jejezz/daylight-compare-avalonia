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
}
