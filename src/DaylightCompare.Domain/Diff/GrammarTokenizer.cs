using System.Text.RegularExpressions;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Diff;

/// <summary>본문 안의 토큰 하나: [<see cref="Start"/>, <see cref="Start"/>+<see cref="Length"/>)가 <see cref="Kind"/>(예: <c>comment</c>).</summary>
public readonly record struct Token(int Start, int Length, string Kind)
{
    public int End => Start + Length;
}

/// <summary>
/// 문법(<see cref="Grammar"/>)으로 본문 전체를 한 번에 훑어 토큰을 찾는다. 줄마다가 아니라 전체를 보는 이유는 여러 줄 주석(<c>/* … */</c>) 때문이다.
/// 한 위치에서 여러 종류가 맞으면 <b>먼저 시작하는 것</b>, 같으면 <b>긴 것</b>이 이긴다 — 그래서 문자열 안의 <c>//</c>는 주석이 아니다.
/// 토큰과 토큰 사이(문법에 없는 글자)는 토큰이 아니다.
/// </summary>
public static class GrammarTokenizer
{
    public static List<Token> Tokenize(string text, Grammar grammar)
    {
        var tokens = new List<Token>();
        var rules = grammar.Tokens.SelectMany(kv => kv.Value.Select(r => (Kind: kv.Key, Regex: r))).ToArray();
        if (rules.Length == 0) return tokens;
        var next = new Match?[rules.Length]; // 규칙마다 pos 이후의 다음 맞음 (다시 찾지 않게 기억)
        var pos = 0;
        while (pos < text.Length)
        {
            var bestIndex = -1;
            Match? best = null;
            for (var i = 0; i < rules.Length; i++)
            {
                var m = next[i];
                if (m is null || (m.Success && m.Index < pos))
                {
                    m = Find(rules[i].Regex, text, pos);
                    next[i] = m;
                }
                if (!m.Success) continue;
                if (best is null || m.Index < best.Index || (m.Index == best.Index && m.Length > best.Length))
                {
                    best = m;
                    bestIndex = i;
                }
            }
            if (best is null) break;
            if (best.Length == 0)
            {
                // 빈 맞음은 토큰이 아니다 — 그 규칙만 한 글자 뒤에서 다시 찾게 한다.
                next[bestIndex] = Find(rules[bestIndex].Regex, text, best.Index + 1);
                continue;
            }
            tokens.Add(new Token(best.Index, best.Length, rules[bestIndex].Kind));
            pos = best.Index + best.Length;
        }
        return tokens;
    }

    static Match Find(Regex regex, string text, int start)
    {
        if (start > text.Length) return Match.Empty;
        try { return regex.Match(text, start); }
        catch (RegexMatchTimeoutException) { return Match.Empty; } // 사용자 정규식이 너무 느리면 그 규칙은 없는 것으로 본다
    }
}
