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
    /// 좌우를 바꾼 쌍은 다른 비교로 본다(BC와 같이). 경로가 비어 있으면 넣지 않는다.
    /// </summary>
    /// <param name="ignoreCase">경로의 대소문자를 가리지 않는다 (Windows·macOS).</param>
    public static IReadOnlyList<CompareHistoryEntry> Record(IReadOnlyList<CompareHistoryEntry> list, CompareHistoryEntry entry, bool ignoreCase)
    {
        if (string.IsNullOrWhiteSpace(entry.Left) || string.IsNullOrWhiteSpace(entry.Right)) return list;
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return [entry, .. list.Where(e => !SamePair(e, entry, comparison)).Take(Max - 1)];
    }

    /// <summary>한 줄을 목록에서 뺀다 (경로로 찾는다).</summary>
    public static IReadOnlyList<CompareHistoryEntry> Remove(IReadOnlyList<CompareHistoryEntry> list, CompareHistoryEntry entry, bool ignoreCase) =>
        [.. list.Where(e => !SamePair(e, entry, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))];

    static bool SamePair(CompareHistoryEntry a, CompareHistoryEntry b, StringComparison comparison) =>
        string.Equals(a.Left, b.Left, comparison) && string.Equals(a.Right, b.Right, comparison);
}
