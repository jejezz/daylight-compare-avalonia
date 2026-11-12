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
    /// 차이 구간 하나를 반대쪽으로 복사한다 (Ctrl+→ 는 왼쪽 → 오른쪽). 대상 쪽의 그 구간 줄을 원본 쪽 줄로 바꾼다.
    /// 결과는 새 실제 글 한 쌍이다 — 화면은 이것으로 다시 비교한다.
    /// </summary>
    public static (string Left, string Right) CopyBlock(TextDiffResult diff, int blockIndex, bool toRight)
    {
        var block = diff.Blocks[blockIndex];
        var (left, leftEnds) = Split(diff.Left.Text);
        var (right, rightEnds) = Split(diff.Right.Text);
        if (toRight)
        {
            var source = left.GetRange(block.LeftStart, block.LeftCount);
            right.RemoveRange(block.RightStart, block.RightCount);
            right.InsertRange(block.RightStart, source);
            // 오른쪽이 비어 있다가 줄이 생기면 왼쪽의 끝 줄바꿈 습관을 따른다.
            if (diff.Right.Text.Length == 0) rightEnds = leftEnds;
        }
        else
        {
            var source = right.GetRange(block.RightStart, block.RightCount);
            left.RemoveRange(block.LeftStart, block.LeftCount);
            left.InsertRange(block.LeftStart, source);
            if (diff.Left.Text.Length == 0) leftEnds = rightEnds;
        }
        return (Join(left, leftEnds), Join(right, rightEnds));
    }
}
