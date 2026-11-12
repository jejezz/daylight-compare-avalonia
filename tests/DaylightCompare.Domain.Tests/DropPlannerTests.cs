using DaylightCompare.Domain.Drop;

namespace DaylightCompare.Domain.Tests;

public class DropPlannerTests
{
    static DroppedItem F(string p) => new(p, DroppedKind.File);
    static DroppedItem D(string p) => new(p, DroppedKind.Folder);
    static DroppedItem Z(string p) => new(p, DroppedKind.Archive);

    [Fact]
    public void 두_개를_놓으면_어디에_놓든_양쪽을_채우고_바로_비교한다()
    {
        foreach (var target in Enum.GetValues<DropTarget>())
        {
            Assert.Equal(new DropPlan.Compare(F("a.txt"), F("b.txt"), CompareMode.Files),
                DropPlanner.Plan([F("a.txt"), F("b.txt")], target, left: D("old"), right: D("old2")));
        }
        Assert.Equal(CompareMode.Folders, ((DropPlan.Compare)DropPlanner.Plan([D("x"), Z("y.zip")], DropTarget.Window)).Mode);
        Assert.Equal(CompareMode.FileAgainstFolder, ((DropPlan.Compare)DropPlanner.Plan([F("a.c"), D("src")], DropTarget.Window)).Mode);
    }

    [Fact]
    public void 하나를_가운데에_놓으면_빈_쪽부터_채운다()
    {
        Assert.Equal(new DropPlan.Pending(F("a"), null), DropPlanner.Plan([F("a")], DropTarget.Window));
        Assert.Equal(new DropPlan.Compare(F("a"), F("b"), CompareMode.Files), DropPlanner.Plan([F("b")], DropTarget.Window, left: F("a")));
        Assert.Equal(new DropPlan.Compare(F("b"), F("a"), CompareMode.Files), DropPlanner.Plan([F("b")], DropTarget.Window, right: F("a")));
    }

    [Fact]
    public void 양쪽이_차_있을_때_가운데에_놓으면_새_비교로_시작한다()
    {
        Assert.Equal(new DropPlan.Pending(F("new"), null), DropPlanner.Plan([F("new")], DropTarget.Window, F("a"), F("b")));
    }

    [Fact]
    public void 한쪽_절반에_놓으면_그쪽만_바꾼다()
    {
        Assert.Equal(new DropPlan.Compare(F("a"), F("new"), CompareMode.Files), DropPlanner.Plan([F("new")], DropTarget.Right, F("a"), F("b")));
        Assert.Equal(new DropPlan.Pending(null, D("r")), DropPlanner.Plan([D("r")], DropTarget.Right));
    }

    [Fact]
    public void 파일_세_개는_병합이고_그_밖의_셋_이상은_거절한다()
    {
        Assert.Equal(new DropPlan.Merge(F("l"), F("base"), F("r")), DropPlanner.Plan([F("l"), F("base"), F("r")], DropTarget.Window));
        Assert.Equal(new DropPlan.Rejected(DropRejection.TooMany), DropPlanner.Plan([F("l"), D("d"), F("r")], DropTarget.Window));
        Assert.Equal(new DropPlan.Rejected(DropRejection.TooMany), DropPlanner.Plan([F("1"), F("2"), F("3"), F("4")], DropTarget.Window));
        Assert.Equal(new DropPlan.Rejected(DropRejection.Empty), DropPlanner.Plan([], DropTarget.Window));
    }

    [Fact]
    public void 파일과_폴더면_폴더_안의_같은_이름_파일이_짝이다()
    {
        var plan = (DropPlan.Compare)DropPlanner.Plan([D(@"C:\proj\src"), F(@"D:\backup\main.c")], DropTarget.Window);
        var (file, container, fileOnLeft) = DropPlanner.Split(plan);
        Assert.False(fileOnLeft);
        Assert.Equal(@"D:\backup\main.c", file.Path);
        Assert.Equal(Path.Combine(@"C:\proj\src", "main.c"), DropPlanner.Counterpart(container, "main.c"));
        Assert.Equal(@"C:\a.zip!/main.c", DropPlanner.Counterpart(Z(@"C:\a.zip"), "main.c"));
    }
}
