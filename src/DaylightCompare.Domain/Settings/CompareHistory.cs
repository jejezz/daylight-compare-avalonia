using System.Text.Json.Serialization;

namespace DaylightCompare.Domain.Settings;

/// <summary>최근 비교 기록 한 줄 (BC 홈의 Recent Sessions와 같은 자리 — 같은 쌍을 매번 다시 고르지 않게).</summary>
public sealed record CompareHistoryEntry
{
    public const string Files = "files";
    public const string Folders = "folders";

    [JsonPropertyName("left")]
    public string Left { get; init; } = "";

    [JsonPropertyName("right")]
    public string Right { get; init; } = "";

    /// <summary><see cref="Files"/>(텍스트 비교) 또는 <see cref="Folders"/>(폴더 비교) — 아이콘과 다시 열 창을 정한다.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = Files;

    [JsonPropertyName("opened_at")]
    public DateTimeOffset OpenedAt { get; init; }

    public bool IsFolders => Kind == Folders;
}

/// <summary>최근 비교 목록의 순수한 갱신 규칙 (UI·IO 없음).</summary>
public static class CompareHistoryList
{
    /// <summary>보관하는 최대 개수.</summary>
    public const int Max = 50;

    /// <summary>
    /// 이 비교를 맨 앞에 넣는다. 같은 쌍(왼쪽·오른쪽 경로가 모두 같음)이 이미 있으면 그 줄을 지우고 앞으로 올린다 — 시각만 새로 바뀐다.
    /// 좌우를 바꾼 쌍은 다른 비교로 본다(BC와 같이). 경로가 비어 있거나, 양쪽이 같은 경로(자기 자신과의 비교)면 넣지 않는다.
    /// </summary>
    /// <param name="ignoreCase">경로의 대소문자를 가리지 않는다 (Windows·macOS).</param>
    public static IReadOnlyList<CompareHistoryEntry> Record(IReadOnlyList<CompareHistoryEntry> list, CompareHistoryEntry entry, bool ignoreCase)
    {
        if (string.IsNullOrWhiteSpace(entry.Left) || string.IsNullOrWhiteSpace(entry.Right)) return list;
        if (IsSamePath(entry.Left, entry.Right, ignoreCase)) return list; // 같은 파일·폴더를 양쪽에 놓은 비교는 다시 열 이유가 없다
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return [entry, .. list.Where(e => !SamePair(e, entry, comparison)).Take(Max - 1)];
    }

    /// <summary>
    /// 두 경로가 같은 파일·폴더를 가리키는가 (자기 자신과의 비교). 문자열만 본다 — <c>\</c>와 <c>/</c>는 같고, 끝의 구분자는 무시한다.
    /// 디스크를 만지지 않으므로 심볼릭 링크 같은 별칭까지는 알지 못한다.
    /// </summary>
    public static bool IsSamePath(string left, string right, bool ignoreCase)
    {
        static string Normalize(string path)
        {
            var s = path.Trim().Replace('\\', '/');
            return s.Length > 1 ? s.TrimEnd('/') : s;
        }
        return string.Equals(Normalize(left), Normalize(right), ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>이미 쌓인 기록에서 자기 자신과의 비교를 뺀다. 뺄 것이 없으면 같은 목록을 그대로 돌려준다 (설정을 괜히 새로 만들지 않게).</summary>
    public static IReadOnlyList<CompareHistoryEntry> WithoutSelfComparisons(IReadOnlyList<CompareHistoryEntry> list, bool ignoreCase)
    {
        if (!list.Any(e => IsSamePath(e.Left, e.Right, ignoreCase))) return list;
        return [.. list.Where(e => !IsSamePath(e.Left, e.Right, ignoreCase))];
    }

    /// <summary>한 줄을 목록에서 뺀다 (경로로 찾는다).</summary>
    public static IReadOnlyList<CompareHistoryEntry> Remove(IReadOnlyList<CompareHistoryEntry> list, CompareHistoryEntry entry, bool ignoreCase) =>
        [.. list.Where(e => !SamePair(e, entry, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))];

    static bool SamePair(CompareHistoryEntry a, CompareHistoryEntry b, StringComparison comparison) =>
        string.Equals(a.Left, b.Left, comparison) && string.Equals(a.Right, b.Right, comparison);
}
