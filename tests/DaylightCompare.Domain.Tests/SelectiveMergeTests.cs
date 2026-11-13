using DaylightCompare.Domain.Diff;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Tests;

/// <summary>
/// 파일 비교의 병합은 사용자가 고른 행만 옮긴다 (<see cref="DiffEdits.CopyRows"/>). 파일 전체는 어떤 경우에도 옮기지 않는다 —
/// 그것은 폴더 비교의 일이다 (<see cref="DiffEdits.CoversWholeSource"/>가 막는다).
/// </summary>
public sealed class SelectiveMergeTests
{
    static AlignedDiff Align(string l, string r) => new(TextDiff.Compare(l, r, ComparePolicy.Builtin.Resolve("a.txt")));

    [Fact]
    public void 고른_한_줄만_옮기고_나머지는_그대로_둔다()
    {
        var a = Align("a\nL1\nL2\nz\n", "a\nR1\nR2\nz\n");
        Assert.Equal(("a\nL1\nL2\nz\n", "a\nL1\nR2\nz\n"), DiffEdits.CopyRows(a, 1, 1, toRight: true)); // L1만
        Assert.Equal(("a\nL1\nR2\nz\n", "a\nR1\nR2\nz\n"), DiffEdits.CopyRows(a, 2, 2, toRight: false)); // 오른쪽 R2만 왼쪽으로
    }

    [Fact]
    public void 고른_여러_줄만_옮긴다()
    {
        var a = Align("h\nL1\nL2\nL3\nt\n", "h\nR1\nR2\nR3\nt\n");
        var (l, r) = DiffEdits.CopyRows(a, 1, 2, toRight: true); // 두 줄만
        Assert.Equal("h\nL1\nL2\nL3\nt\n", l);
        Assert.Equal("h\nL1\nL2\nR3\nt\n", r);
    }

    [Fact]
    public void 서로_다른_두_구간_중_하나만_고르면_다른_구간은_건드리지_않는다()
    {
        var a = Align("a\nX\nb\nc\nY\nd\n", "a\nx\nb\nc\ny\nd\n");
        var (_, r) = DiffEdits.CopyRows(a, 4, 4, toRight: true); // Y만
        Assert.Equal("a\nx\nb\nc\nY\nd\n", r);
    }

    [Fact]
    public void 원본에만_있는_줄을_고르면_대상의_같은_자리에_들어간다()
    {
        var a = Align("a\nb\nc\n", "a\nc\n"); // 1행: 왼쪽에만 b
        Assert.Equal(("a\nb\nc\n", "a\nb\nc\n"), DiffEdits.CopyRows(a, 1, 1, toRight: true));
    }

    [Fact]
    public void 원본에_없는_줄을_고르면_대상에서_그_줄이_빠진다()
    {
        var a = Align("a\nb\nc\n", "a\nc\n"); // 1행: 오른쪽에는 채움 줄
        Assert.Equal(("a\nc\n", "a\nc\n"), DiffEdits.CopyRows(a, 1, 1, toRight: false)); // 오른쪽에 없는 줄을 합치면 왼쪽의 b가 빠진다
    }

    [Fact]
    public void 채움_행을_포함해_고르면_대상_줄이_빠지고_원본_줄이_들어간다()
    {
        var a = Align("x\nold1\nold2\ny\n", "x\nnew\ny\n"); // 왼쪽 두 줄 ↔ 오른쪽 한 줄
        var (l, r) = DiffEdits.CopyRows(a, 1, 2, toRight: false);
        Assert.Equal("x\nnew\ny\n", l.Replace("\r", "")); // 왼쪽의 두 줄이 오른쪽의 한 줄로
        Assert.Equal("x\nnew\ny\n", r);
    }

    [Fact]
    public void 비어_있는_파일로_일부만_옮기면_원본의_끝_줄바꿈을_따른다()
    {
        var a = Align("a\nb\n", "");
        Assert.Equal(("a\nb\n", "a\n"), DiffEdits.CopyRows(a, 0, 0, toRight: true));
    }

    [Fact]
    public void 행_범위의_순서가_바뀌거나_끝을_넘어도_알맞게_다듬는다()
    {
        var a = Align("a\nL\nz\n", "a\nR\nz\n"); // 행 3개
        Assert.Equal(DiffEdits.CopyRows(a, 0, 2, toRight: true), DiffEdits.CopyRows(a, 2, 0, toRight: true)); // 순서를 바꿔 줘도 같다
        Assert.Equal(DiffEdits.CopyRows(a, 1, 2, toRight: true), DiffEdits.CopyRows(a, 1, 99, toRight: true)); // 끝을 넘으면 마지막 행까지
    }

    [Fact]
    public void 파일_내용이_전부_달라도_전체를_고르면_전체_복사로_막는다()
    {
        var a = Align("1\n2\n3\n", "a\nb\nc\n"); // 한 줄도 같지 않다 — 차이 구간 하나가 파일 전체
        Assert.True(DiffEdits.CoversWholeSource(a, 0, 2, toRight: true));
        Assert.True(DiffEdits.CoversWholeSource(a, 0, 2, toRight: false));
        Assert.False(DiffEdits.CoversWholeSource(a, 0, 1, toRight: true)); // 일부면 된다
        Assert.False(DiffEdits.CoversWholeSource(a, 1, 2, toRight: true));
    }

    [Fact]
    public void 채움_줄은_원본_줄로_세지_않는다()
    {
        var a = Align("a\nb\nc\n", "a\nc\n");
        Assert.Equal(1, DiffEdits.SourceLineCount(a, 1, 1, toRight: true));  // 왼쪽 b
        Assert.Equal(0, DiffEdits.SourceLineCount(a, 1, 1, toRight: false)); // 오른쪽은 채움
        Assert.False(DiffEdits.CoversWholeSource(a, 1, 1, toRight: false));  // 원본에 줄이 없는 범위는 전체가 아니다
        Assert.False(DiffEdits.CoversWholeSource(Align("", "x\n"), 0, 0, toRight: true)); // 원본이 빈 파일
    }
}
