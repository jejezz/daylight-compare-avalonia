namespace DaylightCompare.Domain.Diff;

/// <summary>
/// 비교 화면의 편집을 실제 글(파일 내용)로 옮기는 순수 함수들. 화면은 채움 줄이 섞인 글을 보이지만 저장·다시 비교는 실제 글로 한다.
/// </summary>
public static class DiffEdits
{
    /// <summary>본문을 줄로 (LF 기준, 끝의 줄바꿈 뒤에 빈 줄을 만들지 않는다). 끝에 줄바꿈이 있었는지도 돌려준다.</summary>
    public static (List<string> Lines, bool EndsWithNewline) Split(string text)
    {
        if (text.Length == 0) return ([], false);
        var ends = text[^1] == '\n';
        var lines = (ends ? text[..^1] : text).Split('\n').ToList();
        return (lines, ends);
    }

    public static string Join(IReadOnlyList<string> lines, bool endsWithNewline) =>
        lines.Count == 0 ? "" : string.Join('\n', lines) + (endsWithNewline ? "\n" : "");

    /// <summary>
    /// 화면 글에서 실제 글을 꺼낸다: 채움 줄(<paramref name="fillerLines"/>, 0부터)이면서 <b>아직 비어 있는</b> 줄을 뺀다.
    /// 사용자가 채움 줄에 글을 쓰면 그 줄은 실제 줄이 된다.
    /// </summary>
    public static string RealText(string displayText, IReadOnlySet<int> fillerLines, bool endsWithNewline)
    {
        var lines = displayText.Split('\n');
        var kept = new List<string>(lines.Length);
        for (var i = 0; i < lines.Length; i++)
            if (!(fillerLines.Contains(i) && lines[i].Length == 0)) kept.Add(lines[i]);
        // 화면 글은 모든 줄이 채움이면 빈 줄 하나("")로 남는다 — 실제 글로는 비어 있다.
        if (kept.Count == 1 && kept[0].Length == 0 && displayText.Length == 0 && fillerLines.Count > 0) kept.Clear();
        return Join(kept, endsWithNewline && kept.Count > 0);
    }

    /// <summary>
    /// 사용자가 고른 화면 행 <paramref name="firstRow"/>~<paramref name="lastRow"/>(0부터, 양끝 포함)<b>만</b> 반대쪽으로 합친다.
    /// 대상 쪽에서 그 행들에 있던 줄을 원본 쪽 그 행들의 줄로 바꾼다 — 원본에만 있는 줄은 대상의 같은 자리에 들어가고,
    /// 원본이 채움 줄인 행(= 원본에 없는 줄)은 대상에서 그 줄이 빠진다. 고른 범위 밖은 한 줄도 건드리지 않는다.
    /// 결과는 새 실제 글 한 쌍이다 — 화면은 이것으로 다시 비교한다.
    /// <para>
    /// <b>파일 전체를 옮기는 일은 파일 비교에서 하지 않는다</b>(그것은 폴더 비교의 일). 이 함수는 범위만 믿고 움직이므로,
    /// 부르는 쪽이 먼저 <see cref="CoversWholeSource"/>로 막아야 한다.
    /// </para>
    /// </summary>
    public static (string Left, string Right) CopyRows(AlignedDiff aligned, int firstRow, int lastRow, bool toRight)
    {
        (firstRow, lastRow) = ClampRows(aligned, firstRow, lastRow);
        var diff = aligned.Diff;
        var (left, leftEnds) = Split(diff.Left.Text);
        var (right, rightEnds) = Split(diff.Right.Text);
        var (source, target) = toRight ? (left, right) : (right, left);

        // 고른 행 안의 원본 줄 (순서대로)과, 대상 쪽에서 그 행들이 차지하던 줄 수.
        var replacement = new List<string>();
        var removeCount = 0;
        for (var row = firstRow; row <= lastRow; row++)
        {
            var (sourceLine, targetLine) = toRight ? (aligned.Rows[row].Left, aligned.Rows[row].Right) : (aligned.Rows[row].Right, aligned.Rows[row].Left);
            if (sourceLine is { } s) replacement.Add(source[s]);
            if (targetLine is not null) removeCount++;
        }
        // 넣을 자리 = 대상 쪽에서 첫 고른 행보다 앞에 있는 실제 줄 수.
        var at = 0;
        for (var row = 0; row < firstRow; row++)
            if ((toRight ? aligned.Rows[row].Right : aligned.Rows[row].Left) is not null) at++;
        target.RemoveRange(at, removeCount);
        target.InsertRange(at, replacement);

        // 대상이 비어 있다가 줄이 생기면 원본의 끝 줄바꿈 습관을 따른다.
        if (toRight && diff.Right.Text.Length == 0) rightEnds = leftEnds;
        if (!toRight && diff.Left.Text.Length == 0) leftEnds = rightEnds;
        return (Join(left, leftEnds), Join(right, rightEnds));
    }

    /// <summary>
    /// 고른 행 범위가 원본 쪽 파일의 <b>모든 줄</b>을 담는가. 그렇다면 그것은 "파일 전체 복사"이므로 파일 비교에서는 막는다
    /// (파일 전체를 옮기는 일은 폴더 비교의 일 — ROADMAP). 원본에 줄이 하나도 없으면 false.
    /// </summary>
    public static bool CoversWholeSource(AlignedDiff aligned, int firstRow, int lastRow, bool toRight)
    {
        (firstRow, lastRow) = ClampRows(aligned, firstRow, lastRow);
        var total = Split(toRight ? aligned.Diff.Left.Text : aligned.Diff.Right.Text).Lines.Count;
        return total > 0 && SourceLineCount(aligned, firstRow, lastRow, toRight) == total;
    }

    /// <summary>고른 행 범위 안에서 원본 쪽에 실제로 있는 줄 수 (채움 줄은 세지 않는다).</summary>
    public static int SourceLineCount(AlignedDiff aligned, int firstRow, int lastRow, bool toRight)
    {
        (firstRow, lastRow) = ClampRows(aligned, firstRow, lastRow);
        var count = 0;
        for (var row = firstRow; row <= lastRow; row++)
            if ((toRight ? aligned.Rows[row].Left : aligned.Rows[row].Right) is not null) count++;
        return count;
    }

    static (int First, int Last) ClampRows(AlignedDiff aligned, int firstRow, int lastRow)
    {
        var max = aligned.Rows.Count - 1;
        var (a, b) = (Math.Clamp(Math.Min(firstRow, lastRow), 0, Math.Max(0, max)), Math.Clamp(Math.Max(firstRow, lastRow), 0, Math.Max(0, max)));
        return (a, b);
    }
}
