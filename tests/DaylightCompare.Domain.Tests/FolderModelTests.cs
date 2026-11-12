using System.Text.RegularExpressions;
using DaylightCompare.Domain.Folder;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Tests;

public sealed class FolderModelTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    static FileEntry F(string name, long size = 10, DateTimeOffset? at = null) => new("/x/" + name, name, false, size, at ?? T0);
    static FileEntry D(string name) => new("/x/" + name, name, true);

    static FolderRules Rules(bool ignoreCase = true, bool ignoreExtension = false, params RenameRule[] renames) =>
        new([new(CriterionKind.Size), new(CriterionKind.Timestamp, 2, true), new(CriterionKind.Content)],
            FileMask.All, FileMask.All, false, null, null, ignoreCase, ignoreExtension, renames);

    [Fact]
    public void 이름으로_짝짓고_폴더를_먼저_이름순으로()
    {
        var pairs = FolderPairing.Pair([F("b.txt"), D("src"), F("A.txt")], [F("a.txt"), F("c.txt"), D("src")], Rules());
        Assert.Equal(["src|src", "A.txt|a.txt", "b.txt|", "|c.txt"], pairs.Select(p => $"{p.Left?.Name}|{p.Right?.Name}"));
    }

    [Fact]
    public void 대소문자를_가리면_다른_이름이다() =>
        Assert.Equal(2, FolderPairing.Pair([F("A.txt")], [F("a.txt")], Rules(ignoreCase: false)).Count);

    [Fact]
    public void 같은_이름의_파일과_폴더는_짝이_아니다() =>
        Assert.Equal(2, FolderPairing.Pair([F("x")], [D("x")], Rules()).Count);

    [Fact]
    public void 확장자_무시와_이름_바꾸기()
    {
        Assert.Single(FolderPairing.Pair([F("a.c")], [F("a.cpp")], Rules(ignoreExtension: true)));
        var rename = new RenameRule(new Regex(@"^old_(\d+)\.txt$"), "new_$1.txt");
        Assert.Single(FolderPairing.Pair([F("old_7.txt")], [F("new_7.txt")], Rules(renames: rename)));
    }

    [Fact]
    public void 크기가_다르면_바로_다르고_더_새것을_알린다()
    {
        IReadOnlyList<FolderCriterion> quick = [new(CriterionKind.Size), new(CriterionKind.Timestamp), new(CriterionKind.Hash)];
        var (v, newer, _) = FolderJudge.Quick(F("a", 10, T0.AddMinutes(5)), F("a", 11, T0), quick);
        Assert.Equal(QuickVerdict.Different, v);
        Assert.Equal(NewerSide.Left, newer);
    }

    [Fact]
    public void 규칙_기준_내용_단계가_있으면_크기가_달라도_내용이_정한다() =>
        Assert.Equal(QuickVerdict.NeedsContent, FolderJudge.Quick(F("a", 10), F("a", 12), Rules().Criteria).Verdict); // CRLF·BOM은 크기를 바꾼다

    [Fact]
    public void 시각만_다르면_내용이_정한다()
    {
        var (v, newer, step) = FolderJudge.Quick(F("a", 10, T0), F("a", 10, T0.AddMinutes(5)), Rules().Criteria);
        Assert.Equal(QuickVerdict.NeedsContent, v);
        Assert.Equal(NewerSide.Right, newer);
        Assert.Equal(CriterionKind.Content, step!.By);
    }

    [Fact]
    public void 내용_단계가_없으면_시각이_다를_때_다르다()
    {
        IReadOnlyList<FolderCriterion> quick = [new(CriterionKind.Size), new(CriterionKind.Timestamp)];
        Assert.Equal(QuickVerdict.Different, FolderJudge.Quick(F("a", 10, T0), F("a", 10, T0.AddMinutes(5)), quick).Verdict);
        Assert.Equal(QuickVerdict.Same, FolderJudge.Quick(F("a", 10, T0), F("a", 10, T0.AddSeconds(1)), quick).Verdict);
    }

    [Fact]
    public void 정확히_몇_시간_차이는_서머타임_시간대로_보고_같은_시각이다()
    {
        var ts = new FolderCriterion(CriterionKind.Timestamp, 2, IgnoreHourOffsets: true);
        Assert.Equal(NewerSide.None, FolderJudge.NewerOf(F("a", at: T0), F("a", at: T0.AddHours(1).AddSeconds(1)), ts));
        Assert.Equal(NewerSide.Right, FolderJudge.NewerOf(F("a", at: T0), F("a", at: T0.AddHours(1).AddMinutes(3)), ts));
        Assert.Equal(NewerSide.Right, FolderJudge.NewerOf(F("a", at: T0), F("a", at: T0.AddHours(1)), ts with { IgnoreHourOffsets = false }));
    }

    [Fact]
    public void 폴더_상태는_다른_것이_있으면_바로_다름()
    {
        var root = new FolderNode("", "", true, D("l"), D("r"), null) { Scanning = false };
        var same = new FolderNode("a", "a", false, F("a"), F("a"), root) { State = FolderState.Same };
        var pending = new FolderNode("b", "b", false, F("b"), F("b"), root);
        root.Children.AddRange([same, pending]);
        Assert.Equal(FolderState.Pending, FolderJudge.FolderStateOf(root));
        root.Children.Add(new FolderNode("c", "c", false, F("c"), null, root));
        Assert.Equal(FolderState.Different, FolderJudge.FolderStateOf(root));
        root.Children.RemoveAt(2);
        pending.State = FolderState.Same;
        Assert.Equal(FolderState.Same, FolderJudge.FolderStateOf(root));
    }
}
