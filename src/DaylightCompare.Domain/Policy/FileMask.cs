using System.Text;
using System.Text.RegularExpressions;

namespace DaylightCompare.Domain.Policy;

/// <summary>
/// 파일 마스크 목록: <c>*.c;*.h;-*.bak</c>처럼 <c>;</c>로 나누고, <c>-</c>로 시작하면 제외. <c>*</c> 여러 글자, <c>?</c> 한 글자,
/// 대소문자 무시. <c>/</c>가 들어 있는 패턴은 비교 루트 기준 상대 경로 전체와, 아니면 이름과 맞춘다 (<c>**</c>는 폴더 여러 단계).
/// 포함 패턴이 하나도 없으면 "모두 포함"이다.
/// </summary>
public sealed class FileMask
{
    readonly List<(Regex Regex, bool OnPath)> _include = [];
    readonly List<(Regex Regex, bool OnPath)> _exclude = [];

    public FileMask(IEnumerable<string> patterns)
    {
        foreach (var raw in patterns.SelectMany(p => p.Split(';')))
        {
            var p = raw.Trim();
            if (p.Length == 0) continue;
            var exclude = p.StartsWith('-');
            if (exclude) p = p[1..].Trim();
            if (p.Length == 0) continue;
            p = p.Replace('\\', '/');
            (exclude ? _exclude : _include).Add((ToRegex(p), p.Contains('/')));
        }
        Patterns = patterns.ToList();
    }

    public FileMask(string patterns) : this([patterns]) { }

    public static FileMask All { get; } = new([]);

    public IReadOnlyList<string> Patterns { get; }

    public bool IsEmpty => _include.Count == 0 && _exclude.Count == 0;

    /// <param name="name">파일·폴더 이름.</param>
    /// <param name="relativePath">비교 루트 기준 상대 경로(<c>/</c>로 나눔). 없으면 이름을 쓴다.</param>
    public bool Matches(string name, string? relativePath = null)
    {
        var path = (relativePath ?? name).Replace('\\', '/');
        bool Hit((Regex Regex, bool OnPath) m) => m.Regex.IsMatch(m.OnPath ? path : name);
        if (_exclude.Any(Hit)) return false;
        return _include.Count == 0 || _include.Any(Hit);
    }

    /// <summary>포함 패턴에 맞는가 (제외는 보지 않는다). 파일 형식 고르기에 쓴다 — 제외 패턴은 "이 형식에서 뺀다"는 뜻이라 따로 본다.</summary>
    public bool IncludesExplicitly(string name, string? relativePath = null) => _include.Count > 0 && Matches(name, relativePath);

    static Regex ToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var ch = pattern[i];
            if (ch == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                sb.Append(".*");
                i++;
                if (i + 1 < pattern.Length && pattern[i + 1] == '/') i++; // **/ 는 0단계도 허용
            }
            else sb.Append(ch switch
            {
                '*' => "[^/]*",
                '?' => "[^/]",
                _ => Regex.Escape(ch.ToString()),
            });
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }

    public override string ToString() => string.Join(';', Patterns);
}
