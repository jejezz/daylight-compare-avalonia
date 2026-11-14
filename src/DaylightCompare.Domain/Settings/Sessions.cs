using System.Text.Json.Serialization;

namespace DaylightCompare.Domain.Settings;

/// <summary>
/// 비교 창의 탭 하나를 다시 열 수 있게 적어 둔 것 (BC Sessions의 한 비교): 무엇을 비교하나(경로)와 그 비교의 필터·보기 설정.
/// 폴더 비교와 파일 비교가 한 모양을 쓰고, 해당 없는 칸은 비워 둔다 (모르는 키는 읽을 때 무시하므로 칸을 더해도 예전 파일이 읽힌다).
/// </summary>
public sealed record SessionTab
{
    public const string Folder = "folder";
    public const string File = "file";
    public const string Binary = "binary";

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = File;

    [JsonPropertyName("left")]
    public string Left { get; init; } = "";

    [JsonPropertyName("right")]
    public string Right { get; init; } = "";

    // --- 폴더 비교 ---

    /// <summary>이름 필터: 포함·제외 (BC Name Filters 문법 — 폴더 비교의 필터 칸에 적힌 글 그대로).</summary>
    [JsonPropertyName("include")]
    public string Include { get; init; } = "";

    [JsonPropertyName("exclude")]
    public string Exclude { get; init; } = "";

    /// <summary>보기 필터 (<c>FolderShow</c> 비트: 같음·다름·왼쪽이 새것·오른쪽이 새것·왼쪽만·오른쪽만).</summary>
    [JsonPropertyName("show")]
    public int Show { get; init; } = 63;

    [JsonPropertyName("always_folders")]
    public bool AlwaysFolders { get; init; }

    // --- 파일(텍스트) 비교 ---

    /// <summary>보기 필터: <c>all</c> · <c>differences</c> · <c>same</c> · <c>context</c>.</summary>
    [JsonPropertyName("view_filter")]
    public string ViewFilter { get; init; } = "all";

    [JsonPropertyName("context_lines")]
    public int ContextLines { get; init; } = AppSettings.DefaultContextLines;

    [JsonPropertyName("ignore_unimportant")]
    public bool IgnoreUnimportant { get; init; }

    /// <summary>고정한 파일 형식 id (null이면 이름으로 찾는다, 빈 글이면 형식 없음).</summary>
    [JsonPropertyName("format_id")]
    public string? FormatId { get; init; }

    /// <summary>바이너리 비교의 정렬: <c>fixed</c> · <c>insertions</c> (비었으면 정책을 따른다).</summary>
    [JsonPropertyName("align")]
    public string Align { get; init; } = "";

    public bool IsFolder => Kind == Folder;

    public static string ViewFilterName(CompareViewFilter f) => f switch
    {
        CompareViewFilter.Differences => "differences",
        CompareViewFilter.Same => "same",
        CompareViewFilter.Context => "context",
        _ => "all",
    };

    public static CompareViewFilter ViewFilterOf(string? name) => name switch
    {
        "differences" => CompareViewFilter.Differences,
        "same" => CompareViewFilter.Same,
        "context" => CompareViewFilter.Context,
        _ => CompareViewFilter.All,
    };
}

/// <summary>비교 창 하나: 열려 있던 탭들과 그중 보고 있던 탭.</summary>
public sealed record SessionWindow
{
    [JsonPropertyName("tabs")]
    public IReadOnlyList<SessionTab> Tabs { get; init; } = [];

    [JsonPropertyName("active")]
    public int Active { get; init; }
}

/// <summary>이름을 붙여 저장한 세션 (BC의 Saved Session) — 저장할 때의 비교 창 하나.</summary>
public sealed record SavedSession
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("saved_at")]
    public DateTimeOffset SavedAt { get; init; }

    [JsonPropertyName("window")]
    public SessionWindow Window { get; init; } = new();
}

/// <summary>저장한 세션 목록의 순수한 갱신 규칙 (UI·IO 없음). 이름은 대소문자를 가리지 않고 앞뒤 공백을 뗀 것으로 같은지 본다.</summary>
public static class SessionList
{
    public const int Max = 200;

    public static string Clean(string? name) => (name ?? "").Trim();

    static bool SameName(string a, string b) => string.Equals(Clean(a), Clean(b), StringComparison.CurrentCultureIgnoreCase);

    public static bool Contains(IReadOnlyList<SavedSession> list, string name) => list.Any(s => SameName(s.Name, name));

    /// <summary>
    /// 저장한다. 같은 이름이 있으면 그 자리의 것을 바꾸고(순서 유지), 없으면 맨 앞에 넣는다 (가장 최근에 저장한 것이 위).
    /// 이름이 비었거나 탭이 하나도 없으면 목록을 그대로 돌려준다.
    /// </summary>
    public static IReadOnlyList<SavedSession> Save(IReadOnlyList<SavedSession> list, SavedSession session)
    {
        var name = Clean(session.Name);
        if (name.Length == 0 || session.Window.Tabs.Count == 0) return list;
        var saved = session with { Name = name };
        if (Contains(list, name)) return [.. list.Select(s => SameName(s.Name, name) ? saved : s)];
        return [saved, .. list.Take(Max - 1)];
    }

    public static IReadOnlyList<SavedSession> Remove(IReadOnlyList<SavedSession> list, string name) =>
        [.. list.Where(s => !SameName(s.Name, name))];

    /// <summary>이름을 바꾼다. 새 이름이 비었거나 (자기 자신이 아닌) 다른 세션과 같으면 목록을 그대로 돌려준다.</summary>
    public static IReadOnlyList<SavedSession> Rename(IReadOnlyList<SavedSession> list, string from, string to)
    {
        var newName = Clean(to);
        if (newName.Length == 0 || !Contains(list, from)) return list;
        if (!SameName(from, newName) && Contains(list, newName)) return list;
        return [.. list.Select(s => SameName(s.Name, from) ? s with { Name = newName } : s)];
    }
}
