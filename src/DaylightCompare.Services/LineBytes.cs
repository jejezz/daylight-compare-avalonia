using DaylightCompare.Domain.Diff;

namespace DaylightCompare.Services;

/// <summary>
/// 텍스트 파일의 줄마다 원래 바이트 위치를 나눠 둔 표 (BC 텍스트 비교 아래쪽의 16진수 줄 — "눈에는 같은데 왜 다르다고 나오지?"를 풀어 준다).
/// 줄 끝(<c>\r\n</c>·<c>\n</c>·단독 <c>\r</c> — 편집기가 줄로 세는 것과 같다)까지가 한 줄이고 줄 끝 바이트도 그 줄에 든다. 첫 줄에는 BOM이 든다.
/// UTF-16은 두 바이트 단위로 찾는다. 나머지 인코딩(UTF-8·CP949·Shift-JIS·Latin-1)은 줄 끝 바이트가 다른 글자의 일부가 될 수 없어 바이트 단위로 찾는다.
/// </summary>
public sealed class LineByteMap
{
    readonly byte[] _bytes;
    readonly int[] _starts; // 줄 시작 위치. 마지막 값 = 끝

    LineByteMap(byte[] bytes, int[] starts, int bomLength)
    {
        _bytes = bytes;
        _starts = starts;
        BomLength = bomLength;
    }

    public int LineCount => _starts.Length - 1;
    /// <summary>첫 줄 맨 앞의 BOM 바이트 수 (없으면 0).</summary>
    public int BomLength { get; }

    /// <summary>줄 <paramref name="line"/>(0부터)의 바이트 범위. 범위 밖이면 빈 줄.</summary>
    public (int Offset, int Length) Range(int line) =>
        line < 0 || line >= LineCount ? (_bytes.Length, 0) : (_starts[line], _starts[line + 1] - _starts[line]);

    public ReadOnlyMemory<byte> Line(int line)
    {
        var (o, n) = Range(line);
        return _bytes.AsMemory(o, n);
    }

    public static LineByteMap Build(byte[] bytes, TextEncodingKind kind, bool hasBom)
    {
        var bom = hasBom ? TextEncodings.Bom(bytes)?.Length ?? 0 : 0;
        var wide = kind is TextEncodingKind.Utf16Le or TextEncodingKind.Utf16Be;
        var unit = wide ? 2 : 1;
        var starts = new List<int> { 0 };
        var i = bom;
        var lineStart = 0;
        bool Is(int at, byte ch) => at + unit <= bytes.Length && (wide
            ? kind == TextEncodingKind.Utf16Le ? bytes[at] == ch && bytes[at + 1] == 0 : bytes[at] == 0 && bytes[at + 1] == ch
            : bytes[at] == ch);
        while (i + unit <= bytes.Length)
        {
            if (Is(i, 0x0A)) { i += unit; starts.Add(i); lineStart = i; }
            else if (Is(i, 0x0D))
            {
                i += unit;
                if (Is(i, 0x0A)) i += unit; // \r\n
                starts.Add(i);
                lineStart = i;
            }
            else i += unit;
        }
        // 마지막 줄 끝이 없는 글(또는 홀수 바이트 꼬리): 남은 바이트가 있으면 한 줄이다.
        if (lineStart < bytes.Length) starts.Add(bytes.Length);
        return new LineByteMap(bytes, [.. starts], bom);
    }
}

/// <summary>두 줄의 바이트 중 어디가 다른가 (LCS 기준 — 하나가 끼어들어도 뒤가 모두 다르게 보이지 않는다).</summary>
public static class ByteDiff
{
    /// <summary>길이가 이보다 길면 앞부분만 짝짓고 나머지는 다른 것으로 친다 (한 줄짜리 거대 파일에서 멈추지 않게).</summary>
    public const int MaxCompared = 8192;

    public static (bool[] Left, bool[] Right) Differences(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var l = new bool[left.Length];
        var r = new bool[right.Length];
        Array.Fill(l, true);
        Array.Fill(r, true);
        var n = Math.Min(left.Length, MaxCompared);
        var m = Math.Min(right.Length, MaxCompared);
        var a = new int[n];
        var b = new int[m];
        for (var i = 0; i < n; i++) a[i] = left[i];
        for (var i = 0; i < m; i++) b[i] = right[i];
        foreach (var (x, y) in SequenceDiff.Match(a, b))
        {
            l[x] = false;
            r[y] = false;
        }
        return (l, r);
    }
}
