using System.Text;
using System.Text.RegularExpressions;

namespace DaylightCompare.Domain.Policy;

/// <summary>
/// 파일 마스크 목록 — <b>Beyond Compare의 필터 문법</b>(BC5 도움말 file_masks, BC_COMPAT.md §2). 필터 막대·<c>/filters=</c>·정책 파일이 같은 문법을 쓴다.
/// <list type="bullet">
/// <item><c>;</c>로 나누고, <c>-</c>로 시작하면 제외. 이름은 대소문자를 가리지 않는다.</item>
/// <item>와일드카드: <c>*</c> 0글자 이상, <c>?</c> 한 글자, <c>[az]</c> 그 중 하나, <c>[a-z]</c> 범위, <c>[!az]</c> 그 밖의 것, <c>[[]</c> 글자 <c>[</c>.</item>
/// <item><c>*.*</c>은 확장자 없는 이름도 포함. 마스크가 <c>.</c>로 끝나면 <c>*</c>·<c>?</c>가 마침표를 넘지 않는다(<c>*.</c> = 확장자 없음).</item>
/// <item>끝이 <c>\</c>(또는 <c>/</c>)이면 <b>폴더</b> 마스크(<c>bin\</c>, <c>-.git\</c>), 아니면 <b>파일</b> 마스크. 파일 마스크는 파일에만, 폴더 마스크는 폴더에만 쓴다.</item>
/// <item>상대 마스크: 구분자가 들어 있으면 기준 폴더에서의 상대 경로와 맞춘다 — <c>p\f</c>(p 안의 f), <c>.\f</c>(기준 폴더 바로 아래),
/// <c>...\f</c>(몇 단계 아래든). <c>\</c>와 <c>/</c>는 같다. (덧붙여 <c>**</c>도 "몇 단계든"으로 받는다.)</item>
/// <item>포함 마스크가 없는 쪽(파일·폴더 따로)은 "모두 포함"이다.</item>
/// </list>
/// </summary>
public sealed class FileMask
{
    sealed record Rule(Regex Regex, bool OnPath);

    readonly List<Rule> _includeFiles = [], _excludeFiles = [], _includeFolders = [], _excludeFolders = [];

    public FileMask(IEnumerable<string> patterns)
    {
        var list = patterns.ToList();
        foreach (var raw in list.SelectMany(p => p.Split(';')))
        {
            var p = raw.Trim();
            if (p.Length == 0) continue;
            var exclude = p.StartsWith('-');
            if (exclude) p = p[1..].Trim();
            p = p.Replace('\\', '/');
            var folder = p.EndsWith('/') && p.Length > 1;
            if (folder) p = p.TrimEnd('/');
            if (p.Length == 0) continue;
            var target = (folder, exclude) switch
            {
                (false, false) => _includeFiles,
                (false, true) => _excludeFiles,
                (true, false) => _includeFolders,
                (true, true) => _excludeFolders,
            };
            target.Add(ToRule(p));
        }
        Patterns = list;
    }

    public FileMask(string patterns) : this([patterns]) { }

    public static FileMask All { get; } = new([]);

    public IReadOnlyList<string> Patterns { get; }

    public bool IsEmpty => _includeFiles.Count == 0 && _excludeFiles.Count == 0 && _includeFolders.Count == 0 && _excludeFolders.Count == 0;

    /// <summary>파일이 들어오는가.</summary>
    /// <param name="name">파일 이름.</param>
    /// <param name="relativePath">기준 폴더에서의 상대 경로(<c>/</c> 또는 <c>\</c>로 나눔). 없으면 이름을 쓴다.</param>
    public bool Matches(string name, string? relativePath = null) => Test(_includeFiles, _excludeFiles, name, relativePath);

    /// <summary>폴더가 들어오는가 (들어오지 않으면 그 아래를 모두 뺀다).</summary>
    public bool MatchesFolder(string name, string? relativePath = null) => Test(_includeFolders, _excludeFolders, name, relativePath);

    /// <summary>폴더 포함 마스크가 있는가 — 있으면 맞지 않는 폴더라도 그 안에 들어온 파일이 있으면 보인다(BC: 포함된 파일을 담은 폴더는 포함).</summary>
    public bool HasFolderIncludes => _includeFolders.Count > 0;

    /// <summary>포함 패턴에 맞는가 (제외는 보지 않는다). 파일 형식 고르기에 쓴다 — 제외 패턴은 "이 형식에서 뺀다"는 뜻이라 따로 본다.</summary>
    public bool IncludesExplicitly(string name, string? relativePath = null) => _includeFiles.Count > 0 && Matches(name, relativePath);

    static bool Test(List<Rule> include, List<Rule> exclude, string name, string? relativePath)
    {
        var path = (relativePath ?? name).Replace('\\', '/').TrimStart('/');
        bool Hit(Rule r) => r.Regex.IsMatch(r.OnPath ? path : name);
        if (exclude.Any(Hit)) return false;
        return include.Count == 0 || include.Any(Hit);
    }

    static Rule ToRule(string pattern)
    {
        var onPath = pattern.Contains('/');
        var sb = new StringBuilder("^");
        if (pattern.StartsWith(".../", StringComparison.Ordinal)) { sb.Append("(?:.*/)?"); pattern = pattern[4..]; }
        else if (pattern.StartsWith("./", StringComparison.Ordinal)) pattern = pattern[2..];
        // 마스크가 마침표로 끝나면 와일드카드가 마침표를 넘지 않는다 (*. = 확장자 없음). "*.*"는 확장자가 없어도 맞는다.
        var noDots = pattern.EndsWith('.') && pattern != ".";
        if (noDots) pattern = pattern[..^1];
        var any = noDots ? "[^/.]" : "[^/]";
        for (var i = 0; i < pattern.Length; i++)
        {
            var ch = pattern[i];
            if (ch == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                sb.Append(".*");
                i++;
                if (i + 1 < pattern.Length && pattern[i + 1] == '/') i++; // **/ 는 0단계도 허용
            }
            else if (ch == '*' && pattern.AsSpan(i).StartsWith("*.*") && (i + 3 == pattern.Length || pattern[i + 3] == '/'))
            {
                sb.Append($"{any}*");
                i += 2;
            }
            else if (ch == '[' && ClassEnd(pattern, i) is { } end)
            {
                sb.Append(CharClass(pattern[(i + 1)..end]));
                i = end;
            }
            else sb.Append(ch switch
            {
                '*' => $"{any}*",
                '?' => any,
                _ => Regex.Escape(ch.ToString()),
            });
        }
        sb.Append('$');
        return new Rule(new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline), onPath);
    }

    /// <summary><c>[</c> 다음의 닫는 <c>]</c> 위치 (<c>[[]</c>처럼 첫 글자가 <c>]</c>가 아니면 그다음 <c>]</c>). 없으면 null(글자 <c>[</c>로 본다).</summary>
    static int? ClassEnd(string p, int open)
    {
        var start = open + 1;
        if (start < p.Length && p[start] == '!') start++;
        if (start >= p.Length) return null;
        var close = p.IndexOf(']', start + 1); // 첫 글자는 무엇이든 (그래서 [[] 는 글자 [)
        return close < 0 ? null : close;
    }

    static string CharClass(string body)
    {
        var negate = body.StartsWith('!');
        if (negate) body = body[1..];
        var sb = new StringBuilder(negate ? "[^/" : "[");
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c == '-' && i > 0 && i + 1 < body.Length) sb.Append('-');
            else sb.Append(c is '\\' or ']' or '[' or '^' or '-' ? "\\" + c : c.ToString());
        }
        return sb.Append(']').ToString();
    }

    public override string ToString() => string.Join(';', Patterns);
}
