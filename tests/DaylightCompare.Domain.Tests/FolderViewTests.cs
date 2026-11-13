using DaylightCompare.Domain.Folder;

namespace DaylightCompare.Domain.Tests;

public sealed class FolderViewTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    const string L = @"C:\l", R = @"C:\r";

    static FileEntry E(string root, string rel, bool dir = false) => new(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)), Path.GetFileName(rel), dir, dir ? null : 1, T0);

    static FolderNode Root() => new("", "", true, E(L, "", true), E(R, "", true), null) { Scanning = false };

    static FolderNode File(FolderNode parent, string name, FolderState state, NewerSide newer = NewerSide.None)
    {
        var rel = parent.RelativePath.Length == 0 ? name : parent.RelativePath + "/" + name;
        var left = state != FolderState.RightOrphan ? E(L, rel) : null;
        var right = state != FolderState.LeftOrphan ? E(R, rel) : null;
        var n = new FolderNode(name, rel, false, left, right, parent) { State = state, Newer = newer };
        parent.Children.Add(n);
        return n;
    }

    static FolderNode Dir(FolderNode parent, string name, bool left = true, bool right = true)
    {
        var rel = parent.RelativePath.Length == 0 ? name : parent.RelativePath + "/" + name;
        var n = new FolderNode(name, rel, true, left ? E(L, rel, true) : null, right ? E(R, rel, true) : null, parent) { Scanning = false };
        parent.Children.Add(n);
        return n;
    }

    static string[] Names(IEnumerable<FolderRow> rows) => [.. rows.Select(r => new string(' ', r.Depth) + r.Node.Name)];

    static readonly IReadOnlyDictionary<string, bool> NoOverrides = new Dictionary<string, bool>();

    [Fact]
    public void BC_보기_필터_이름과_토글_조합()
    {
        Assert.Equal("all", FolderShowPresets.IdOf(FolderShow.All));
        Assert.Equal(FolderShow.All & ~FolderShow.Same, FolderShowPresets.Parse("differences"));
        Assert.Null(FolderShowPresets.IdOf(FolderShow.Same | FolderShow.LeftOrphan));
        Assert.Equal(FolderShow.All, FolderShowPresets.Parse("없는 이름"));
    }

    [Fact]
    public void 필터에_따라_파일을_거르고_보일_파일이_없는_폴더는_숨긴다()
    {
        var root = Root(); // 폴더 먼저, 그다음 파일 (짝짓기의 순서)
        var a = Dir(root, "a");
        File(a, "x.txt", FolderState.Same);
        var b = Dir(root, "b");
        File(b, "y.txt", FolderState.RightOrphan);
        File(root, "left.txt", FolderState.Different, NewerSide.Left);
        File(root, "same.txt", FolderState.Same);
        File(root, "wait.txt", FolderState.Pending);
        var open = new Dictionary<string, bool> { ["a"] = true, ["b"] = true };

        Assert.Equal(["a", " x.txt", "b", " y.txt", "left.txt", "same.txt", "wait.txt"], Names(FolderView.Flatten(root, FolderShow.All, false, open)));
        // Show Differences: 같은 것만 든 a는 숨고, 모르는 것은 늘 보인다.
        Assert.Equal(["b", " y.txt", "left.txt", "wait.txt"], Names(FolderView.Flatten(root, FolderShow.All & ~FolderShow.Same, false, open)));
        Assert.Equal(["a", " x.txt", "same.txt", "wait.txt"], Names(FolderView.Flatten(root, FolderShow.Same, false, open)));
        Assert.Equal(["left.txt", "wait.txt"], Names(FolderView.Flatten(root, FolderShow.LeftNewer, false, open)));
        Assert.Equal(["b", " y.txt", "wait.txt"], Names(FolderView.Flatten(root, FolderShow.RightOrphan, false, open)));
    }

    [Fact]
    public void 폴더_펼침은_기본이_같지_않으면_펴고_사용자가_바꾼_값이_이긴다()
    {
        var root = Root();
        var a = Dir(root, "a");
        File(a, "x.txt", FolderState.Different);
        a.State = FolderState.Different;
        var s = Dir(root, "s");
        File(s, "y.txt", FolderState.Same);
        s.State = FolderState.Same;

        Assert.Equal(["a", " x.txt", "s"], Names(FolderView.Flatten(root, FolderShow.All, false, NoOverrides)));
        Assert.Equal(["a", "s", " y.txt"], Names(FolderView.Flatten(root, FolderShow.All, false, new Dictionary<string, bool> { ["a"] = false, ["s"] = true })));
    }

    [Fact]
    public void 폴더를_늘_보이기는_빈_폴더도_보인다()
    {
        var root = Root();
        var s = Dir(root, "s");
        File(s, "y.txt", FolderState.Same);
        Assert.Empty(FolderView.Flatten(root, FolderShow.All & ~FolderShow.Same, false, NoOverrides));
        Assert.Equal(["s"], Names(FolderView.Flatten(root, FolderShow.All & ~FolderShow.Same, true, NoOverrides)));
    }

    [Fact]
    public void 개수는_파일_기준이다()
    {
        var root = Root();
        File(root, "a", FolderState.Same);
        File(root, "b", FolderState.Different);
        File(root, "c", FolderState.LeftOrphan);
        File(Dir(root, "d"), "e", FolderState.RightOrphan);
        File(root, "f", FolderState.Pending);
        Assert.Equal((1, 1, 1, 1, 1), FolderView.Count(root));
    }

    [Fact]
    public void 복사_계획은_폴더_안의_같지_않은_파일만_담고_원본에만_있는_폴더는_통째로()
    {
        var root = Root();
        var both = Dir(root, "both");
        File(both, "same.txt", FolderState.Same);
        File(both, "diff.txt", FolderState.Different);
        File(both, "new.txt", FolderState.LeftOrphan);
        var onlyL = Dir(root, "onlyL", right: false);
        File(onlyL, "z.txt", FolderState.LeftOrphan);

        var plan = FolderOps.PlanCopy([both, onlyL], toRight: true, L, R);
        Assert.Equal(
            [(Path.Combine(L, "both", "diff.txt"), Path.Combine(R, "both"), true),
             (Path.Combine(L, "both", "new.txt"), Path.Combine(R, "both"), false),
             (Path.Combine(L, "onlyL"), R, false)],
            plan.Select(i => (i.Source, i.TargetDir, i.Overwrites)));
    }

    [Fact]
    public void 고른_폴더_안의_것은_따로_세지_않고_원본이_없으면_건너뛴다()
    {
        var root = Root();
        var d = Dir(root, "d");
        var f = File(d, "f.txt", FolderState.Different);
        var orphanRight = File(root, "r.txt", FolderState.RightOrphan);
        Assert.Single(FolderOps.PlanCopy([d, f], toRight: true, L, R));
        Assert.Empty(FolderOps.PlanCopy([orphanRight], toRight: true, L, R)); // 왼쪽에 없으니 오른쪽으로 복사할 것이 없다
        Assert.Single(FolderOps.PlanCopy([orphanRight], toRight: false, L, R));
        Assert.Equal([d.Left!.Path], FolderOps.PlanDelete([d, f], onLeft: true)); // 폴더가 골라졌으면 그 폴더 하나
    }
}
