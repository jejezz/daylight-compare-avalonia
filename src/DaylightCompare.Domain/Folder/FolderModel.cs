using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Folder;

/// <summary>
/// 폴더 비교에서 한 줄(짝 하나)의 상태 — Beyond Compare의 뜻과 같다(BC_COMPAT.md §2): 같음(검정), 다름(빨강), 한쪽에만(보라, orphan),
/// 아직 모름(회색 — 내용 비교를 기다리는 중). 다른 파일 중 어느 쪽이 더 새것인지는 <see cref="NewerSide"/>로 따로 둔다(BC의 "Newer").
/// </summary>
public enum FolderState { Pending, Same, Different, LeftOrphan, RightOrphan, Error }

public enum NewerSide { None, Left, Right }

/// <summary>
/// 폴더 비교 결과 나무의 마디 하나. 폴더면 <see cref="Children"/>가 있다. 비교기가 백그라운드에서 채우고 바꾸므로,
/// 화면은 비교기의 변경 알림을 받은 뒤 UI 스레드에서 읽는다.
/// </summary>
public sealed class FolderNode(string name, string relativePath, bool isDirectory, FileEntry? left, FileEntry? right, FolderNode? parent)
{
    public string Name { get; } = name;

    /// <summary>기준 폴더에서의 상대 경로 (<c>/</c>로 나눔, 루트는 빈 문자열).</summary>
    public string RelativePath { get; } = relativePath;

    public bool IsDirectory { get; } = isDirectory;
    public FileEntry? Left { get; set; } = left;
    public FileEntry? Right { get; set; } = right;
    public FolderNode? Parent { get; } = parent;
    public List<FolderNode> Children { get; } = [];
    public FolderState State { get; set; } = left is null ? FolderState.RightOrphan : right is null ? FolderState.LeftOrphan : FolderState.Pending;
    public NewerSide Newer { get; set; }

    /// <summary>읽기·비교에 실패한 이유 (<see cref="FolderState.Error"/>).</summary>
    public string? Error { get; set; }

    /// <summary>폴더: 그 아래를 아직 다 읽지 않았다.</summary>
    public bool Scanning { get; set; } = isDirectory;

    public bool IsOrphan => Left is null || Right is null;

    /// <summary>한쪽에만 있는 폴더이고 안을 아직 읽지 않았다 (펼칠 때 읽는다).</summary>
    public bool IsUnloaded => IsDirectory && IsOrphan && Scanning;

    public IEnumerable<FolderNode> Descendants()
    {
        foreach (var c in Children)
        {
            yield return c;
            foreach (var d in c.Descendants()) yield return d;
        }
    }

    public override string ToString() => $"{RelativePath} {State}";
}

/// <summary>한쪽 목록끼리 짝짓기 (Session Settings > Comparison의 이름 맞추기 — 정책 <c>folder.pairing</c>).</summary>
public static class FolderPairing
{
    /// <summary>
    /// 왼쪽·오른쪽 항목을 이름으로 짝짓는다. 폴더는 폴더끼리, 파일은 파일끼리. 대소문자 무시·확장자 무시·이름 바꾸기 규칙(왼쪽 이름 → 오른쪽 이름)을 쓴다.
    /// 결과는 폴더 먼저, 그다음 이름 순 (BC와 같은 순서).
    /// </summary>
    public static List<(FileEntry? Left, FileEntry? Right)> Pair(IReadOnlyList<FileEntry> left, IReadOnlyList<FileEntry> right, FolderRules rules)
    {
        var comparer = rules.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        string Key(FileEntry e, bool isLeft)
        {
            var name = e.Name;
            if (isLeft && !e.IsDirectory)
                foreach (var r in rules.Renames)
                    if (r.Left.IsMatch(name)) { name = r.Left.Replace(name, r.Right); break; }
            if (rules.IgnoreExtension && !e.IsDirectory) name = Path.GetFileNameWithoutExtension(name);
            return (e.IsDirectory ? "d:" : "f:") + name;
        }
        var rightByKey = new Dictionary<string, FileEntry>(comparer);
        foreach (var r in right) rightByKey.TryAdd(Key(r, false), r);
        var used = new HashSet<FileEntry>(ReferenceEqualityComparer.Instance);
        var pairs = new List<(FileEntry?, FileEntry?)>();
        foreach (var l in left)
        {
            if (rightByKey.TryGetValue(Key(l, true), out var r) && used.Add(r)) pairs.Add((l, r));
            else pairs.Add((l, null));
        }
        foreach (var r in right)
            if (!used.Contains(r)) pairs.Add((null, r));
        return [.. pairs
            .OrderBy(p => (p.Item1 ?? p.Item2)!.IsDirectory ? 0 : 1)
            .ThenBy(p => (p.Item1 ?? p.Item2)!.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => (p.Item1 ?? p.Item2)!.Name, StringComparer.Ordinal)];
    }
}

/// <summary>빠른 검사 결과: 같음·다름이 정해졌거나, 내용(또는 해시)을 봐야 한다.</summary>
public enum QuickVerdict { Same, Different, NeedsContent }

/// <summary>
/// 판정 단계(정책 <c>folder.criteria</c>, BC Session Settings > Comparison)의 빠른 검사. 순서대로 본다:
/// 크기가 다르면 다름. 시각이 다르면 — 뒤에 내용·해시 단계가 있으면 그 결과가 정한다(BC "Override quick test results"), 없으면 다름.
/// 내용·해시 단계에 닿으면 <see cref="QuickVerdict.NeedsContent"/>. 끝까지 같으면 같음. 더 새것인 쪽은 따로 알려 준다.
/// </summary>
public static class FolderJudge
{
    public static (QuickVerdict Verdict, NewerSide Newer, FolderCriterion? ContentStep) Quick(FileEntry left, FileEntry right, IReadOnlyList<FolderCriterion> criteria)
    {
        var timestamp = criteria.FirstOrDefault(c => c.By == CriterionKind.Timestamp) ?? new FolderCriterion(CriterionKind.Timestamp);
        var newer = NewerOf(left, right, timestamp);
        for (var i = 0; i < criteria.Count; i++)
        {
            var c = criteria[i];
            switch (c.By)
            {
                // 크기가 달라도 뒤에 규칙 기준 내용 단계가 있으면 그 결과가 정한다 (줄바꿈·인코딩만 다른 텍스트는 크기가 달라도 같다).
                // 해시 단계는 바이트를 보므로 크기가 다르면 이미 다르다.
                case CriterionKind.Size when left.SizeBytes is { } a && right.SizeBytes is { } b && a != b:
                    if (!criteria.Skip(i + 1).Any(x => x.By == CriterionKind.Content)) return (QuickVerdict.Different, newer, null);
                    break;
                case CriterionKind.Timestamp when NewerOf(left, right, c) != NewerSide.None:
                    if (!criteria.Skip(i + 1).Any(x => x.By is CriterionKind.Content or CriterionKind.Hash)) return (QuickVerdict.Different, newer, null);
                    break;
                case CriterionKind.Content or CriterionKind.Hash:
                    return (QuickVerdict.NeedsContent, newer, c);
            }
        }
        return (QuickVerdict.Same, newer, null);
    }

    /// <summary>
    /// 수정 시각이 허용 오차보다 차이 나면 더 새것인 쪽. <see cref="FolderCriterion.IgnoreHourOffsets"/>면 정확히 n시간(± 오차) 차이는
    /// 같은 시각으로 본다 (서머타임·FAT 시간대 — BC "Ignore daylight saving / timezone differences").
    /// </summary>
    public static NewerSide NewerOf(FileEntry left, FileEntry right, FolderCriterion timestamp)
    {
        if (left.ModifiedAt is not { } a || right.ModifiedAt is not { } b) return NewerSide.None;
        var diff = (a - b).TotalSeconds;
        var tolerance = Math.Max(0, timestamp.ToleranceSeconds);
        if (Math.Abs(diff) <= tolerance) return NewerSide.None;
        if (timestamp.IgnoreHourOffsets)
        {
            var hours = Math.Round(diff / 3600);
            if (hours != 0 && Math.Abs(hours) <= 24 && Math.Abs(diff - hours * 3600) <= tolerance) return NewerSide.None;
        }
        return diff > 0 ? NewerSide.Left : NewerSide.Right;
    }

    /// <summary>
    /// 폴더의 상태: 한쪽에만 있으면 그 쪽 고아, 아래에 다른 것(다름·고아·오류)이 하나라도 있으면 바로 다름(나머지를 기다리지 않는다),
    /// 아직 모르는 것이 있으면 모름, 모두 같으면 같음.
    /// </summary>
    public static FolderState FolderStateOf(FolderNode folder)
    {
        if (folder.Left is null) return FolderState.RightOrphan;
        if (folder.Right is null) return FolderState.LeftOrphan;
        if (folder.Children.Any(c => c.State is FolderState.Different or FolderState.LeftOrphan or FolderState.RightOrphan or FolderState.Error))
            return FolderState.Different;
        if (folder.Scanning || folder.Children.Any(c => c.State == FolderState.Pending)) return FolderState.Pending;
        return FolderState.Same;
    }
}
