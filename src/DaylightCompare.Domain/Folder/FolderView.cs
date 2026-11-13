namespace DaylightCompare.Domain.Folder;

/// <summary>
/// 폴더 비교의 보기 필터 토글 (BC View > Show …, BC_COMPAT.md §2). 파일 한 줄은 아래 여섯 가지 중 하나이고, 켜 놓은 것만 보인다.
/// 아직 모르는 것(<see cref="FolderState.Pending"/>)과 오류는 늘 보인다.
/// </summary>
[Flags]
public enum FolderShow
{
    None = 0,
    Same = 1,
    /// <summary>내용은 다르지만 어느 쪽이 더 새것인지 모르는 것 (시각이 같거나 허용 오차 안).</summary>
    Different = 2,
    LeftNewer = 4,
    RightNewer = 8,
    LeftOrphan = 16,
    RightOrphan = 32,
    All = Same | Different | LeftNewer | RightNewer | LeftOrphan | RightOrphan,
}

/// <summary>BC 보기 필터 이름 → 토글 조합 (BC View 메뉴의 Show … 항목).</summary>
public static class FolderShowPresets
{
    const FolderShow Diffs = FolderShow.Different | FolderShow.LeftNewer | FolderShow.RightNewer;
    const FolderShow Orphans = FolderShow.LeftOrphan | FolderShow.RightOrphan;

    public static readonly IReadOnlyList<(string Id, FolderShow Show)> All =
    [
        ("all", FolderShow.All),
        ("differences", FolderShow.All & ~FolderShow.Same),
        ("same", FolderShow.Same),
        ("noOrphans", FolderShow.Same | Diffs),
        ("differencesNoOrphans", Diffs),
        ("orphans", Orphans),
        ("leftNewer", FolderShow.LeftNewer),
        ("rightNewer", FolderShow.RightNewer),
        ("leftNewerAndOrphans", FolderShow.LeftNewer | FolderShow.LeftOrphan),
        ("rightNewerAndOrphans", FolderShow.RightNewer | FolderShow.RightOrphan),
        ("leftOrphans", FolderShow.LeftOrphan),
        ("rightOrphans", FolderShow.RightOrphan),
    ];

    /// <summary>이 조합과 같은 BC 이름 (없으면 null — 토글을 직접 맞춘 것).</summary>
    public static string? IdOf(FolderShow show) => All.FirstOrDefault(p => p.Show == show).Id;

    public static FolderShow Parse(string? id) => All.FirstOrDefault(p => p.Id == id) is { Id: not null } p ? p.Show : FolderShow.All;
}

/// <summary>화면에 그릴 한 줄 (펼친 폴더 아래로 풀어 놓은 나무의 한 마디).</summary>
public sealed record FolderRow(FolderNode Node, int Depth, bool IsExpanded)
{
    public bool IsFolder => Node.IsDirectory;
}

/// <summary>
/// 폴더 비교 결과 나무를 화면 줄 목록으로 만든다 (BC의 보기 필터·접고 펴기). 마디를 읽는 동안 비교기가 바꾸지 않게
/// 부르는 쪽이 <c>FolderComparer.Gate</c>를 잡고 부른다.
/// </summary>
public static class FolderView
{
    /// <summary>파일 마디가 어느 토글에 속하는가. 모름·오류는 null (늘 보인다).</summary>
    public static FolderShow? KindOf(FolderNode file) => file.State switch
    {
        FolderState.Same => FolderShow.Same,
        FolderState.Different => file.Newer switch { NewerSide.Left => FolderShow.LeftNewer, NewerSide.Right => FolderShow.RightNewer, _ => FolderShow.Different },
        FolderState.LeftOrphan => FolderShow.LeftOrphan,
        FolderState.RightOrphan => FolderShow.RightOrphan,
        _ => null,
    };

    /// <summary>
    /// 줄 목록. 폴더는 아래에 보일 파일이 있으면 보이고(<paramref name="alwaysShowFolders"/>면 늘), 파일이 하나도 없는 폴더는 자기 상태로 본다.
    /// 펼침: <paramref name="expanded"/>에 사용자가 정한 값이 있으면 그것, 없으면 "같지 않은 폴더는 펼치고 같은 폴더는 접는다".
    /// </summary>
    public static List<FolderRow> Flatten(FolderNode root, FolderShow show, bool alwaysShowFolders, IReadOnlyDictionary<string, bool> expanded)
    {
        var rows = new List<FolderRow>();
        Walk(root, 0);
        return rows;

        // 보일 파일이 (자기 아래에) 있는가를 올리면서 줄을 쌓는다. 접힌 폴더 아래도 보임 여부는 계산해야 폴더를 보일지 안다.
        bool Walk(FolderNode folder, int depth)
        {
            var any = false;
            foreach (var child in folder.Children)
            {
                if (!child.IsDirectory)
                {
                    if (KindOf(child) is { } kind && (show & kind) == 0) continue;
                    any = true;
                    rows.Add(new FolderRow(child, depth, false));
                    continue;
                }
                var at = rows.Count;
                rows.Add(new FolderRow(child, depth, IsExpanded(child)));
                var open = IsExpanded(child);
                var sub = open ? Walk(child, depth + 1) : AnyVisible(child);
                var visible = alwaysShowFolders || sub || (child.Children.Count == 0 && PassesAsFolder(child));
                if (!visible) rows.RemoveRange(at, rows.Count - at);
                any |= visible;
            }
            return any;
        }

        bool IsExpanded(FolderNode f) => expanded.TryGetValue(f.RelativePath, out var v) ? v : f.State != FolderState.Same;

        bool PassesAsFolder(FolderNode f) => f.State switch
        {
            FolderState.Same => (show & FolderShow.Same) != 0,
            FolderState.LeftOrphan => (show & FolderShow.LeftOrphan) != 0,
            FolderState.RightOrphan => (show & FolderShow.RightOrphan) != 0,
            _ => true,
        };

        bool AnyVisible(FolderNode f) =>
            f.Children.Any(c => c.IsDirectory ? AnyVisible(c) || c.Children.Count == 0 && PassesAsFolder(c) : KindOf(c) is not { } k || (show & k) != 0);
    }

    /// <summary>파일 기준 개수 (상태 줄용).</summary>
    public static (int Same, int Different, int LeftOrphan, int RightOrphan, int Pending) Count(FolderNode root)
    {
        int same = 0, diff = 0, lo = 0, ro = 0, pending = 0;
        foreach (var n in root.Descendants())
        {
            if (n.IsDirectory) continue;
            switch (n.State)
            {
                case FolderState.Same: same++; break;
                case FolderState.Different or FolderState.Error: diff++; break;
                case FolderState.LeftOrphan: lo++; break;
                case FolderState.RightOrphan: ro++; break;
                default: pending++; break;
            }
        }
        return (same, diff, lo, ro, pending);
    }
}

/// <summary>한 번의 복사·삭제 항목.</summary>
/// <param name="Source">원본 경로.</param>
/// <param name="TargetDir">넣을 폴더 (없으면 만들어야 한다).</param>
/// <param name="Overwrites">대상에 같은 이름이 이미 있어 덮어쓴다.</param>
public sealed record CopyItem(string Source, string TargetDir, bool Overwrites);

/// <summary>선택한 줄을 복사·삭제 항목으로 푼다 (BC Copy to Right / Left, Delete).</summary>
public static class FolderOps
{
    /// <param name="nodes">고른 줄(폴더 포함).</param>
    /// <param name="toRight">왼쪽 → 오른쪽이면 true.</param>
    /// <param name="leftRoot">왼쪽 기준 폴더 경로.</param>
    /// <param name="rightRoot">오른쪽 기준 폴더 경로.</param>
    /// <remarks>
    /// 고른 폴더 안의 것은 따로 세지 않는다(위 폴더가 이미 골라졌으면). 양쪽에 있는 폴더는 폴더째 덮지 않고(합치기 문제를 피한다)
    /// 그 안에서 같지 않은 파일만 복사한다. 원본 쪽에만 있는 폴더는 폴더째 복사한다.
    /// </remarks>
    public static List<CopyItem> PlanCopy(IEnumerable<FolderNode> nodes, bool toRight, string leftRoot, string rightRoot)
    {
        var selected = nodes.ToList();
        var paths = selected.Select(n => n.RelativePath).ToHashSet();
        var items = new List<CopyItem>();
        foreach (var node in selected)
        {
            if (HasSelectedAncestor(node, paths)) continue;
            Add(node, explicitly: true);
        }
        return items;

        void Add(FolderNode node, bool explicitly)
        {
            var source = toRight ? node.Left : node.Right;
            var target = toRight ? node.Right : node.Left;
            if (source is null) return;
            if (node.IsDirectory && target is not null)
            {
                foreach (var child in node.Children)
                    if (child.State != FolderState.Same) Add(child, explicitly: false);
                return;
            }
            if (!node.IsDirectory && !explicitly && node.State == FolderState.Same) return;
            var parentRel = node.Parent?.RelativePath ?? "";
            var targetDir = Join(toRight ? rightRoot : leftRoot, parentRel);
            items.Add(new CopyItem(source.Path, targetDir, target is not null));
        }
    }

    /// <summary>고른 줄에서 <paramref name="onLeft"/> 쪽에 있는 항목 경로 (삭제). 위 폴더가 골라졌으면 안의 것은 뺀다.</summary>
    public static List<string> PlanDelete(IEnumerable<FolderNode> nodes, bool onLeft)
    {
        var selected = nodes.ToList();
        var paths = selected.Select(n => n.RelativePath).ToHashSet();
        return [.. selected.Where(n => !HasSelectedAncestor(n, paths)).Select(n => (onLeft ? n.Left : n.Right)?.Path).OfType<string>()];
    }

    static bool HasSelectedAncestor(FolderNode node, HashSet<string> selected)
    {
        for (var p = node.Parent; p is not null && p.RelativePath.Length > 0; p = p.Parent)
            if (selected.Contains(p.RelativePath)) return true;
        return false;
    }

    static string Join(string root, string relative)
    {
        if (relative.Length == 0) return root;
        return Locations.IsRemote(root) || ArchivePaths.IsInArchive(root)
            ? Locations.Combine(root, relative)
            : Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
