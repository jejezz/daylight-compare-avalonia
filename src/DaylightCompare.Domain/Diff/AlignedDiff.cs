using System.Text;

namespace DaylightCompare.Domain.Diff;

/// <summary>
/// 화면 한 줄(행): 왼쪽·오른쪽의 실제 줄 번호(0부터, 없으면 채움 줄) · 그 행이 속한 diff 구간.
/// </summary>
public readonly record struct AlignedRow(int? Left, int? Right, DiffBlockKind Kind, int Block)
{
    public bool IsDifference => Kind is DiffBlockKind.Changed or DiffBlockKind.LeftOnly or DiffBlockKind.RightOnly;
}

/// <summary>차이 구간 하나가 차지하는 행 범위 (다음/이전 차이 이동과 개요 막대).</summary>
public readonly record struct AlignedRange(int StartRow, int RowCount, DiffBlockKind Kind, int Block);

/// <summary>
/// 좌우를 같은 행에 맞춘 보기 (ROADMAP 3단계 위험 항목의 해법): 한쪽에만 있는 줄 맞은편에 <b>채움 줄</b>(빈 행)을 넣어 두 편집기의
/// 행 번호를 같게 만든다. 그러면 같은 글꼴·줄 높이의 두 편집기는 스크롤 위치만 같게 두면 늘 나란히 보인다.
/// <list type="bullet">
/// <item>바뀐 구간에서 줄 수가 다르면 앞에서부터 짝짓고, 남는 줄의 맞은편을 채운다.</item>
/// <item>보이는 글(<see cref="LeftText"/>/<see cref="RightText"/>)에서 채움 줄은 빈 줄이다. 줄 번호 여백은 <see cref="AlignedRow.Left"/>로 실제 번호를 보인다.</item>
/// </list>
/// </summary>
public sealed class AlignedDiff
{
    readonly int[] _rowOfLeft;
    readonly int[] _rowOfRight;

    public AlignedDiff(TextDiffResult diff)
    {
        Diff = diff;
        var rows = new List<AlignedRow>();
        var ranges = new List<AlignedRange>();
        for (var bi = 0; bi < diff.Blocks.Count; bi++)
        {
            var b = diff.Blocks[bi];
            var start = rows.Count;
            var count = Math.Max(b.LeftCount, b.RightCount);
            for (var i = 0; i < count; i++)
                rows.Add(new AlignedRow(
                    i < b.LeftCount ? b.LeftStart + i : null,
                    i < b.RightCount ? b.RightStart + i : null,
                    b.Kind, bi));
            if (b.Kind != DiffBlockKind.Equal && count > 0) ranges.Add(new AlignedRange(start, count, b.Kind, bi));
        }
        Rows = rows;
        Ranges = ranges;

        _rowOfLeft = new int[diff.Left.LineCount];
        _rowOfRight = new int[diff.Right.LineCount];
        for (var r = 0; r < rows.Count; r++)
        {
            if (rows[r].Left is { } l) _rowOfLeft[l] = r;
            if (rows[r].Right is { } rr) _rowOfRight[rr] = r;
        }
        LeftText = Build(rows, diff.Left, left: true);
        RightText = Build(rows, diff.Right, left: false);
    }

    public TextDiffResult Diff { get; }

    public IReadOnlyList<AlignedRow> Rows { get; }

    /// <summary>같지 않은 구간들 (중요하지 않은 차이 포함 — 필요하면 <see cref="AlignedRange.Kind"/>로 거른다).</summary>
    public IReadOnlyList<AlignedRange> Ranges { get; }

    /// <summary>왼쪽 편집기에 보일 글: 행마다 한 줄, 채움 줄은 빈 줄.</summary>
    public string LeftText { get; }

    public string RightText { get; }

    public int RowOfLeftLine(int line) => _rowOfLeft[Math.Clamp(line, 0, Math.Max(0, _rowOfLeft.Length - 1))];

    public int RowOfRightLine(int line) => _rowOfRight[Math.Clamp(line, 0, Math.Max(0, _rowOfRight.Length - 1))];

    /// <summary>
    /// <paramref name="row"/> 다음(아래)에 시작하는 차이 구간. <paramref name="includeUnimportant"/>가 아니면 중요한 차이만. 없으면 null.
    /// </summary>
    public AlignedRange? Next(int row, bool includeUnimportant = false) =>
        Ranges.Where(r => (includeUnimportant || IsImportant(r.Kind)) && r.StartRow > row).Cast<AlignedRange?>().FirstOrDefault();

    public AlignedRange? Previous(int row, bool includeUnimportant = false) =>
        Ranges.Where(r => (includeUnimportant || IsImportant(r.Kind)) && r.StartRow < row).Cast<AlignedRange?>().LastOrDefault();

    /// <summary><paramref name="row"/>가 들어 있는 차이 구간.</summary>
    public AlignedRange? RangeAt(int row) =>
        Ranges.Where(r => row >= r.StartRow && row < r.StartRow + r.RowCount).Cast<AlignedRange?>().FirstOrDefault();

    static bool IsImportant(DiffBlockKind kind) => kind is DiffBlockKind.Changed or DiffBlockKind.LeftOnly or DiffBlockKind.RightOnly;

    static string Build(List<AlignedRow> rows, TextDocument doc, bool left)
    {
        var sb = new StringBuilder(doc.Text.Length + rows.Count);
        for (var r = 0; r < rows.Count; r++)
        {
            if (r > 0) sb.Append('\n');
            if ((left ? rows[r].Left : rows[r].Right) is { } line) sb.Append(doc.Text, doc.LineStart(line), doc.LineEnd(line) - doc.LineStart(line));
        }
        return sb.ToString();
    }
}
