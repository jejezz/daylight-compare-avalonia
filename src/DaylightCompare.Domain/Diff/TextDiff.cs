using System.Collections;
using System.Text;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Diff;

/// <summary>diff 결과의 구간 종류.</summary>
public enum DiffBlockKind
{
    /// <summary>글자까지 같다.</summary>
    Equal,

    /// <summary>다르지만 정책상 중요하지 않은 차이뿐이다 (주석·공백·대소문자·치환). 회색으로 보이고 "같다" 판정에 들어간다.</summary>
    Unimportant,

    /// <summary>양쪽 줄이 다르다.</summary>
    Changed,

    /// <summary>왼쪽에만 있는 줄.</summary>
    LeftOnly,

    /// <summary>오른쪽에만 있는 줄.</summary>
    RightOnly,
}

/// <summary>diff 결과의 한 구간: 왼쪽 [<see cref="LeftStart"/>, +<see cref="LeftCount"/>)와 오른쪽 [<see cref="RightStart"/>, +<see cref="RightCount"/>) (줄 번호 0부터).</summary>
public sealed record DiffBlock(DiffBlockKind Kind, int LeftStart, int LeftCount, int RightStart, int RightCount)
{
    public bool IsDifference => Kind is DiffBlockKind.Changed or DiffBlockKind.LeftOnly or DiffBlockKind.RightOnly;
}

/// <summary>
/// 비교할 본문 하나: 줄, 문법 토큰, "중요하지 않은 글자" 표시, 줄마다의 비교 열쇠(정규화한 글).
/// 본문은 LF로 정규화되어 있어야 한다 (<c>TextFiles.NormalizeLineBreaks</c>).
/// </summary>
public sealed class TextDocument
{
    public TextDocument(string text, TextRules rules, Grammar? grammar)
    {
        Text = text;
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n') starts.Add(i + 1);
        // 끝의 줄바꿈 뒤에 빈 줄을 하나 더 만들지 않는다 ("a\n"은 한 줄).
        if (starts.Count > 1 && starts[^1] == text.Length) starts.RemoveAt(starts.Count - 1);
        if (text.Length == 0) starts.Clear();
        LineStarts = starts.ToArray();

        Tokens = grammar is null ? [] : GrammarTokenizer.Tokenize(text, grammar);
        Unimportant = new BitArray(text.Length);
        WhitespaceToken = new BitArray(text.Length);
        foreach (var t in Tokens)
        {
            var isWs = t.Kind == "whitespace";
            if (!rules.Unimportant.Contains(t.Kind)) continue;
            for (var i = t.Start; i < t.End; i++)
            {
                Unimportant[i] = true;
                if (isWs) WhitespaceToken[i] = true;
            }
        }
        Keys = new string[LineStarts.Length];
        for (var line = 0; line < LineStarts.Length; line++) Keys[line] = MakeKey(line, rules);
    }

    public string Text { get; }

    /// <summary>줄의 시작 위치 (본문 안 글자 위치).</summary>
    public int[] LineStarts { get; }

    public int LineCount => LineStarts.Length;

    public IReadOnlyList<Token> Tokens { get; }

    /// <summary>글자마다: 중요하지 않은 토큰(정책의 <c>importance.unimportant</c>) 안에 있는가.</summary>
    public BitArray Unimportant { get; }

    BitArray WhitespaceToken { get; }

    /// <summary>줄마다의 비교 열쇠. 열쇠가 같으면 "같은 줄"이다 (글자까지 같은지는 따로 본다).</summary>
    public string[] Keys { get; }

    public int LineStart(int line) => LineStarts[line];

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>줄 끝(줄바꿈 제외).</summary>
    public int LineEnd(int line)
    {
        var end = line + 1 < LineStarts.Length ? LineStarts[line + 1] - 1 : Text.Length;
        if (end > LineStarts[line] && end - 1 < Text.Length && Text[end - 1] == '\r') end--; // 혹시 남은 CR
        if (line + 1 >= LineStarts.Length && end > LineStarts[line] && Text[end - 1] == '\n') end--;
        return end;
    }

    public string Line(int line) => Text[LineStart(line)..LineEnd(line)];

    /// <summary>
    /// 비교 열쇠: 중요하지 않은 토큰을 빼고(공백 토큰은 빈칸 하나로 줄임), 정규화(뒤 공백·모든 공백·대소문자)와 치환을 한다.
    /// </summary>
    string MakeKey(int line, TextRules rules)
    {
        int start = LineStart(line), end = LineEnd(line);
        var sb = new StringBuilder(end - start);
        // 공백이 중요하지 않으면: 공백을 빼되, 낱말 글자 사이의 공백은 빈칸 하나로 남긴다 ("int  y = 2" == "int y=2", 그러나 "int x" != "intx").
        var whitespaceUnimportant = rules.IgnoreAllWhitespace || rules.Unimportant.Contains("whitespace");
        var pendingSpace = false;
        for (var i = start; i < end; i++)
        {
            var c = Text[i];
            var isSpace = char.IsWhiteSpace(c);
            if (Unimportant[i] || (whitespaceUnimportant && isSpace))
            {
                if (WhitespaceToken[i] || isSpace) pendingSpace = true;
                continue;
            }
            if (pendingSpace && sb.Length > 0 && IsWordChar(sb[^1]) && IsWordChar(c)) sb.Append(' ');
            pendingSpace = false;
            sb.Append(c);
        }
        var key = sb.ToString();
        if (!whitespaceUnimportant && rules.IgnoreTrailingWhitespace) key = key.TrimEnd();
        foreach (var r in rules.Replacements)
        {
            try { key = r.Pattern.Replace(key, r.With); }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { }
        }
        if (rules.IgnoreCase) key = key.ToLowerInvariant();
        return key;
    }
}

/// <summary>두 본문의 줄 단위 비교 결과.</summary>
public sealed class TextDiffResult(TextDocument left, TextDocument right, IReadOnlyList<DiffBlock> blocks)
{
    public TextDocument Left { get; } = left;
    public TextDocument Right { get; } = right;
    public IReadOnlyList<DiffBlock> Blocks { get; } = blocks;

    /// <summary>중요한 차이 구간의 수 (다음/이전 차이 이동의 대상).</summary>
    public int DifferenceCount => Blocks.Count(b => b.IsDifference);

    /// <summary>글자까지 같다.</summary>
    public bool IsIdentical => Blocks.All(b => b.Kind == DiffBlockKind.Equal);

    /// <summary>중요한 차이가 없다 (중요하지 않은 차이는 있을 수 있음) — 폴더 비교의 "내용" 판정이 이것을 "같다"로 본다.</summary>
    public bool IsEquivalent => Blocks.All(b => !b.IsDifference);

    /// <summary>바뀐 줄 한 쌍의 줄 안 차이 (화면이 보이는 줄만 부른다).</summary>
    public IntralineDiff Intraline(int leftLine, int rightLine) => IntralineDiff.Compute(Left, leftLine, Right, rightLine);
}

/// <summary>
/// 텍스트 비교 엔진 (ROADMAP 3단계, §5). 줄마다 비교 열쇠를 만들어 같은 열쇠끼리 같은 번호를 붙이고, <see cref="SequenceDiff"/>로 짝지은 뒤 구간으로 묶는다.
/// <list type="bullet">
/// <item>짝지어진 줄은 글자까지 같으면 <see cref="DiffBlockKind.Equal"/>, 열쇠만 같으면 <see cref="DiffBlockKind.Unimportant"/>.</item>
/// <item>짝이 없는 줄들은 바뀜·한쪽에만 있음이다. 다만 양쪽의 열쇠를 이어 붙인 것이 같으면(주석만 있는 줄이 늘었다, 줄을 나눴다) 중요하지 않은 차이로 본다.</item>
/// </list>
/// </summary>
public static class TextDiff
{
    public static TextDiffResult Compare(string left, string right, TextRules rules, Grammar? grammar = null)
    {
        var l = new TextDocument(left, rules, grammar);
        var r = new TextDocument(right, rules, grammar);
        return new TextDiffResult(l, r, Blocks(l, r, rules.Alignment));
    }

    public static TextDiffResult Compare(string left, string right, EffectivePolicy policy) => Compare(left, right, policy.Text, policy.Grammar);

    static List<DiffBlock> Blocks(TextDocument l, TextDocument r, AlignmentKind algorithm)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int[] Intern(string[] keys) => keys.Select(k => ids.TryGetValue(k, out var id) ? id : ids[k] = ids.Count).ToArray();
        var a = Intern(l.Keys);
        var b = Intern(r.Keys);
        var pairs = SequenceDiff.Match(a, b, algorithm);

        var blocks = new List<DiffBlock>();
        void Add(DiffBlockKind kind, int ls, int lc, int rs, int rc)
        {
            if (lc == 0 && rc == 0) return;
            if (blocks.Count > 0 && blocks[^1] is { } last && last.Kind == kind
                && last.LeftStart + last.LeftCount == ls && last.RightStart + last.RightCount == rs)
                blocks[^1] = last with { LeftCount = last.LeftCount + lc, RightCount = last.RightCount + rc };
            else blocks.Add(new DiffBlock(kind, ls, lc, rs, rc));
        }
        void Gap(int ls, int le, int rs, int re)
        {
            int lc = le - ls, rc = re - rs;
            if (lc == 0 && rc == 0) return;
            var leftKey = string.Concat(l.Keys[ls..le]);
            var rightKey = string.Concat(r.Keys[rs..re]);
            var kind = leftKey == rightKey ? DiffBlockKind.Unimportant
                : lc == 0 ? DiffBlockKind.RightOnly
                : rc == 0 ? DiffBlockKind.LeftOnly
                : DiffBlockKind.Changed;
            // 빈 열쇠 줄(주석만·공백만 있는 줄)이 한쪽에 더 있는 것은 중요하지 않은 차이다 — 위에서 같다고 판정된다.
            blocks.Add(new DiffBlock(kind, ls, lc, rs, rc));
        }

        int li = 0, ri = 0;
        foreach (var (pa, pb) in pairs)
        {
            Gap(li, pa, ri, pb);
            Add(l.Line(pa) == r.Line(pb) ? DiffBlockKind.Equal : DiffBlockKind.Unimportant, pa, 1, pb, 1);
            li = pa + 1;
            ri = pb + 1;
        }
        Gap(li, l.LineCount, ri, r.LineCount);
        return blocks;
    }
}
