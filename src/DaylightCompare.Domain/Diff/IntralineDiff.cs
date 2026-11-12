namespace DaylightCompare.Domain.Diff;

/// <summary>줄 안의 다른 부분 하나 (줄 시작 기준 글자 위치). <see cref="Unimportant"/>이면 정책상 중요하지 않은 부분만 다르다.</summary>
public readonly record struct DiffSegment(int Start, int Length, bool Unimportant)
{
    public int End => Start + Length;
}

/// <summary>바뀐 줄 한 쌍의 줄 안 차이: 왼쪽 줄과 오른쪽 줄에서 강조할 부분.</summary>
public sealed record IntralineDiff(IReadOnlyList<DiffSegment> Left, IReadOnlyList<DiffSegment> Right)
{
    /// <summary>
    /// 단어 단위로 짝짓고(글자·숫자·_ 묶음, 공백 묶음, 그 밖의 글자 하나씩), 짝 없는 단어를 강조한다. 이웃한 강조는 합친다.
    /// 바뀐 단어가 양쪽 하나씩이면 그 안에서 글자 단위로 한 번 더 좁힌다 (<c>count</c> → <c>counts</c>는 <c>s</c>만).
    /// 강조 부분의 글자가 모두 중요하지 않은 토큰(주석 등) 안이거나 공백뿐이면 <see cref="DiffSegment.Unimportant"/>.
    /// </summary>
    public static IntralineDiff Compute(TextDocument left, int leftLine, TextDocument right, int rightLine)
    {
        var lText = left.Line(leftLine);
        var rText = right.Line(rightLine);
        var (lSegs, rSegs) = Segments(lText, rText);
        return new IntralineDiff(
            Mark(lSegs, left, left.LineStart(leftLine), lText),
            Mark(rSegs, right, right.LineStart(rightLine), rText));
    }

    /// <summary>문서 없이 두 줄만 비교 (중요도 판정 없음).</summary>
    public static IntralineDiff Compute(string leftText, string rightText)
    {
        var (l, r) = Segments(leftText, rightText);
        return new IntralineDiff(l.Select(s => new DiffSegment(s.Start, s.Length, false)).ToList(),
            r.Select(s => new DiffSegment(s.Start, s.Length, false)).ToList());
    }

    static List<DiffSegment> Mark(List<(int Start, int Length)> segs, TextDocument doc, int lineStart, string line)
    {
        var result = new List<DiffSegment>(segs.Count);
        foreach (var (start, length) in segs)
        {
            var unimportant = true;
            for (var i = start; i < start + length && unimportant; i++)
                unimportant = doc.Unimportant[lineStart + i] || char.IsWhiteSpace(line[i]);
            result.Add(new DiffSegment(start, length, unimportant));
        }
        return result;
    }

    static (List<(int Start, int Length)> Left, List<(int Start, int Length)> Right) Segments(string a, string b)
    {
        var at = Words(a);
        var bt = Words(b);
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int[] Intern(string s, List<(int Start, int Length)> words) =>
            words.Select(w => { var k = s.Substring(w.Start, w.Length); return ids.TryGetValue(k, out var id) ? id : ids[k] = ids.Count; }).ToArray();
        var pairs = SequenceDiff.Match(Intern(a, at), Intern(b, bt), Policy.AlignmentKind.Myers);

        var left = new List<(int Start, int Length)>();
        var right = new List<(int Start, int Length)>();
        int ai = 0, bi = 0;
        void Gap(int ae, int be)
        {
            var la = Span(at, ai, ae);
            var lb = Span(bt, bi, be);
            // 양쪽 모두 단어 하나씩 바뀌었으면 글자 단위로 좁힌다.
            if (ae - ai == 1 && be - bi == 1 && la is { } wa && lb is { } wb)
            {
                var (ca, cb) = Chars(a.Substring(wa.Start, wa.Length), b.Substring(wb.Start, wb.Length));
                left.AddRange(ca.Select(c => (c.Start + wa.Start, c.Length)));
                right.AddRange(cb.Select(c => (c.Start + wb.Start, c.Length)));
                return;
            }
            if (la is { } x) left.Add(x);
            if (lb is { } y) right.Add(y);
        }
        foreach (var (pa, pb) in pairs)
        {
            Gap(pa, pb);
            ai = pa + 1;
            bi = pb + 1;
        }
        Gap(at.Count, bt.Count);
        return (Merge(left), Merge(right));
    }

    /// <summary>글자 단위 diff. 공통 부분이 단어의 절반에 못 미치면 단어 전체를 강조한다 (조각조각 강조는 읽기 어렵다).</summary>
    static (List<(int Start, int Length)>, List<(int Start, int Length)>) Chars(string a, string b)
    {
        var pairs = SequenceDiff.Match(a.Select(c => (int)c).ToArray(), b.Select(c => (int)c).ToArray(), Policy.AlignmentKind.Myers);
        if (pairs.Count * 2 < Math.Max(a.Length, b.Length)) return ([(0, a.Length)], [(0, b.Length)]);
        var left = new List<(int Start, int Length)>();
        var right = new List<(int Start, int Length)>();
        int ai = 0, bi = 0;
        foreach (var (pa, pb) in pairs)
        {
            if (pa > ai) left.Add((ai, pa - ai));
            if (pb > bi) right.Add((bi, pb - bi));
            ai = pa + 1;
            bi = pb + 1;
        }
        if (a.Length > ai) left.Add((ai, a.Length - ai));
        if (b.Length > bi) right.Add((bi, b.Length - bi));
        return (left, right);
    }

    static (int Start, int Length)? Span(List<(int Start, int Length)> words, int from, int to) =>
        to <= from ? null : (words[from].Start, words[to - 1].Start + words[to - 1].Length - words[from].Start);

    static List<(int Start, int Length)> Merge(List<(int Start, int Length)> segs)
    {
        segs.Sort((x, y) => x.Start.CompareTo(y.Start));
        var merged = new List<(int Start, int Length)>();
        foreach (var s in segs)
        {
            if (s.Length <= 0) continue;
            if (merged.Count > 0 && merged[^1].Start + merged[^1].Length >= s.Start)
            {
                var last = merged[^1];
                merged[^1] = (last.Start, Math.Max(last.Start + last.Length, s.Start + s.Length) - last.Start);
            }
            else merged.Add(s);
        }
        return merged.Select(m => (m.Start, m.Length)).ToList();
    }

    /// <summary>단어로 나누기: 글자·숫자·_ 묶음 / 공백 묶음 / 그 밖의 글자 하나.</summary>
    static List<(int Start, int Length)> Words(string s)
    {
        var words = new List<(int, int)>();
        var i = 0;
        while (i < s.Length)
        {
            var start = i;
            var c = s[i];
            if (char.IsLetterOrDigit(c) || c == '_')
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
            else if (char.IsWhiteSpace(c))
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            else
                i += char.IsHighSurrogate(c) && i + 1 < s.Length ? 2 : 1;
            words.Add((start, i - start));
        }
        return words;
    }
}
