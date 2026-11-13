namespace DaylightCompare.Domain.Policy;

/// <summary>
/// 폴더 비교 필터 막대의 두 칸 — <b>포함</b>과 <b>제외</b> 목록 (BC 세션 설정의 Include/Exclude에 해당).
/// 각 칸은 <see cref="FileMask"/>와 같은 문법(<c>;</c>로 나눔, 폴더는 끝에 <c>\</c>)이고, 제외 칸의 항목은 <c>-</c>가 없어도 제외다.
/// 포함 칸에 <c>-bin\</c>처럼 적은 것도 제외로 받으므로 BC의 한 줄 필터를 그대로 붙여 넣어도 된다.
/// 파일(.dcfilter)은 줄마다 <c>include: …</c> / <c>exclude: …</c>이고 <c>#</c>은 주석이다 — 손으로 고치기 쉽게 평문으로 둔다.
/// </summary>
public sealed record FilterSpec(string Include, string Exclude)
{
    public static FilterSpec Empty { get; } = new("", "");

    public bool IsEmpty => Include.Trim().Length == 0 && Exclude.Trim().Length == 0;

    /// <summary>두 칸을 하나의 마스크로 합친다.</summary>
    public FileMask ToMask()
    {
        var patterns = new List<string>();
        if (Include.Trim().Length > 0) patterns.Add(Include);
        foreach (var raw in Exclude.Split(';'))
        {
            var p = raw.Trim().TrimStart('-').Trim();
            if (p.Length > 0) patterns.Add("-" + p);
        }
        return new FileMask(patterns);
    }

    public string Format() =>
        "# Daylight Compare filter\n" +
        $"include: {Include.Trim()}\n" +
        $"exclude: {Exclude.Trim()}\n";

    /// <summary>파일 글을 읽는다. 알아볼 수 없는 줄은 버리고, 같은 키가 여러 줄이면 <c>;</c>로 이어 붙인다. 아무것도 못 읽으면 null.</summary>
    public static FilterSpec? Parse(string text)
    {
        List<string> include = [], exclude = [];
        var found = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (key.Equals("include", StringComparison.OrdinalIgnoreCase)) { found = true; if (value.Length > 0) include.Add(value); }
            else if (key.Equals("exclude", StringComparison.OrdinalIgnoreCase)) { found = true; if (value.Length > 0) exclude.Add(value); }
        }
        return found ? new FilterSpec(string.Join(';', include), string.Join(';', exclude)) : null;
    }
}
