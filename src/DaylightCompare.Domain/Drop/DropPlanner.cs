namespace DaylightCompare.Domain.Drop;

/// <summary>놓은 자리: 창 전체(홈 창·비교 창 가운데), 왼쪽 절반, 오른쪽 절반.</summary>
public enum DropTarget { Window, Left, Right }

/// <summary>놓은 항목의 종류. 압축 파일은 기본으로 폴더처럼 연다 (Beyond Compare와 같음).</summary>
public enum DroppedKind { File, Folder, Archive }

public sealed record DroppedItem(string Path, DroppedKind Kind)
{
    /// <summary>안을 들여다볼 수 있는 것 (폴더·압축 파일).</summary>
    public bool IsContainer => Kind != DroppedKind.File;
}

public enum CompareMode
{
    /// <summary>폴더·압축 파일끼리 → 폴더 비교.</summary>
    Folders,

    /// <summary>파일끼리 → 파일 비교 (텍스트·바이너리·이미지는 정책이 고른다).</summary>
    Files,

    /// <summary>파일 하나와 폴더 하나 → 폴더 안의 같은 이름 파일과 비교 (<see cref="DropPlanner.Counterpart"/>).</summary>
    FileAgainstFolder,
}

public enum DropRejection
{
    /// <summary>아무것도 없다 (텍스트 조각 등 파일이 아닌 것을 놓음).</summary>
    Empty,

    /// <summary>4개 이상, 또는 3개인데 폴더가 섞임.</summary>
    TooMany,
}

/// <summary>끌어다 놓은 결과 화면이 할 일.</summary>
public abstract record DropPlan
{
    /// <summary>한쪽만 채워졌다 — 다른 쪽을 기다린다 (그 칸에 "여기에 놓거나 고르세요"를 보인다).</summary>
    public sealed record Pending(DroppedItem? Left, DroppedItem? Right) : DropPlan;

    /// <summary>양쪽이 채워져 바로 비교한다.</summary>
    public sealed record Compare(DroppedItem Left, DroppedItem Right, CompareMode Mode) : DropPlan;

    /// <summary>파일 세 개 → 3-way 병합 (가운데가 공통 조상). 화면에서 역할을 바꿀 수 있게 한다.</summary>
    public sealed record Merge(DroppedItem Left, DroppedItem Base, DroppedItem Right) : DropPlan;

    public sealed record Rejected(DropRejection Reason) : DropPlan;
}

/// <summary>
/// 끌어다 놓기 규칙 (ROADMAP §6-1 사용자 편의). 화면은 놓은 경로를 <see cref="DroppedItem"/>으로 바꿔 넘기고 결과대로 창을 연다.
/// <list type="bullet">
/// <item><b>두 개</b>를 한 번에 놓으면 어디에 놓든 왼쪽·오른쪽을 한꺼번에 채우고 바로 비교한다.</item>
/// <item><b>하나</b>를 왼쪽/오른쪽 절반에 놓으면 그쪽만 바꾼다. 창 전체(가운데)에 놓으면 빈 쪽(왼쪽 먼저)을 채우고,
/// 양쪽이 다 차 있으면 새 비교로 보고 왼쪽에 넣고 오른쪽을 비운다.</item>
/// <item>양쪽이 차면: 폴더·압축끼리 폴더 비교, 파일끼리 파일 비교, 파일과 폴더면 폴더 안의 같은 이름 파일과 비교.</item>
/// <item><b>세 개</b>가 모두 파일이면 3-way 병합. 그 밖에 셋 이상은 거절.</item>
/// </list>
/// </summary>
public static class DropPlanner
{
    public static DropPlan Plan(IReadOnlyList<DroppedItem> items, DropTarget target, DroppedItem? left = null, DroppedItem? right = null)
    {
        switch (items.Count)
        {
            case 0:
                return new DropPlan.Rejected(DropRejection.Empty);
            case 2:
                return Decide(items[0], items[1]);
            case 3 when items.All(i => !i.IsContainer):
                return new DropPlan.Merge(items[0], items[1], items[2]);
            case > 2:
                return new DropPlan.Rejected(DropRejection.TooMany);
        }

        var item = items[0];
        switch (target)
        {
            case DropTarget.Left: left = item; break;
            case DropTarget.Right: right = item; break;
            default:
                if (left is null) left = item;
                else if (right is null) right = item;
                else (left, right) = (item, null);
                break;
        }
        return left is not null && right is not null ? Decide(left, right) : new DropPlan.Pending(left, right);
    }

    static DropPlan Decide(DroppedItem left, DroppedItem right) => (left.IsContainer, right.IsContainer) switch
    {
        (true, true) => new DropPlan.Compare(left, right, CompareMode.Folders),
        (false, false) => new DropPlan.Compare(left, right, CompareMode.Files),
        _ => new DropPlan.Compare(left, right, CompareMode.FileAgainstFolder),
    };

    /// <summary>
    /// <see cref="CompareMode.FileAgainstFolder"/>에서 폴더 쪽의 짝: 폴더(또는 압축 파일) 안의 같은 이름 파일 경로.
    /// 그 파일이 실제로 있는지는 화면이 파일 시스템에 묻는다 — 없으면 "폴더에 같은 이름 파일이 없다"고 알리고 그 쪽을 비워 둔다.
    /// </summary>
    public static string Counterpart(DroppedItem container, string fileName) =>
        container.Kind == DroppedKind.Archive
            ? ArchivePaths.Format(container.Path, fileName)
            : Locations.Combine(container.Path, fileName);

    /// <summary>파일 쪽과 폴더 쪽을 나눠 준다 (<see cref="CompareMode.FileAgainstFolder"/>일 때).</summary>
    public static (DroppedItem File, DroppedItem Container, bool FileOnLeft) Split(DropPlan.Compare compare) =>
        compare.Left.IsContainer ? (compare.Right, compare.Left, false) : (compare.Left, compare.Right, true);
}
