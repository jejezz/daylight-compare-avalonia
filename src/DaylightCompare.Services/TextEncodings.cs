using System.Text;

namespace DaylightCompare.Services;

/// <summary>텍스트 파일 인코딩. 비교 화면의 상태 표시줄에 보이고, 사용자가 바꿀 수 있다 (ROADMAP 1단계).</summary>
public enum TextEncodingKind
{
    Utf8,
    Utf16Le,
    Utf16Be,
    /// <summary>한국어 Windows 코드 페이지 949 (EUC-KR의 확장).</summary>
    Cp949,
    /// <summary>일본어 Windows 코드 페이지 932.</summary>
    ShiftJis,
    /// <summary>어느 것으로도 읽히지 않을 때: 바이트 하나 = 글자 하나. 내용을 바꾸지 않고 보여 주기 위한 마지막 수단.</summary>
    Latin1,
}

/// <summary>
/// 인코딩 감지와 변환. Daylight Commander의 <c>TextFiles</c>는 UTF-8만 읽었다 — 한국어 소스(CP949)·UTF-16 파일 비교를 위해 넓힌 것.
/// <para>감지 순서: BOM → BOM 없는 UTF-16(NUL 바이트 위치) → 엄격한 UTF-8 → CP949 / Shift-JIS(둘 다 읽히면 그 언어 글자가 많은 쪽,
/// 같으면 CP949) → Latin-1.</para>
/// </summary>
public static class TextEncodings
{
    static TextEncodings() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>감지에 쓰는 앞부분 크기. 파일 전체를 다 보지 않아도 결론이 난다.</summary>
    const int SampleBytes = 64 * 1024;

    public static readonly IReadOnlyList<TextEncodingKind> All = Enum.GetValues<TextEncodingKind>();

    /// <summary>상태 표시줄·선택 목록에 쓰는 이름 (번역하지 않는 표준 이름).</summary>
    public static string DisplayName(TextEncodingKind kind, bool hasBom = false) => kind switch
    {
        TextEncodingKind.Utf8 => hasBom ? "UTF-8 BOM" : "UTF-8",
        TextEncodingKind.Utf16Le => hasBom ? "UTF-16 LE BOM" : "UTF-16 LE",
        TextEncodingKind.Utf16Be => hasBom ? "UTF-16 BE BOM" : "UTF-16 BE",
        TextEncodingKind.Cp949 => "CP949 (EUC-KR)",
        TextEncodingKind.ShiftJis => "Shift-JIS",
        _ => "Latin-1",
    };

    /// <summary>파일 앞의 BOM 길이(0이면 없음)와 그 인코딩.</summary>
    public static (TextEncodingKind Kind, int Length)? Bom(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return (TextEncodingKind.Utf8, 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return (TextEncodingKind.Utf16Le, 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return (TextEncodingKind.Utf16Be, 2);
        return null;
    }

    /// <summary>
    /// BOM 없는 UTF-16 추정: 영문·숫자·기호가 많은 본문은 글자마다 0x00 바이트가 한쪽(LE는 홀수, BE는 짝수 위치)에 몰린다.
    /// 짝을 이룬 바이트 중 한쪽 위치의 NUL이 40% 넘고 다른 쪽은 거의 없으면 그 방향으로 본다.
    /// </summary>
    public static TextEncodingKind? GuessUtf16(ReadOnlySpan<byte> bytes)
    {
        var n = Math.Min(bytes.Length, SampleBytes) & ~1;
        if (n < 4) return null;
        int evenNul = 0, oddNul = 0;
        for (var i = 0; i < n; i += 2)
        {
            if (bytes[i] == 0) evenNul++;
            if (bytes[i + 1] == 0) oddNul++;
        }
        var pairs = n / 2;
        if (oddNul > pairs * 0.4 && evenNul < pairs * 0.05) return TextEncodingKind.Utf16Le;
        if (evenNul > pairs * 0.4 && oddNul < pairs * 0.05) return TextEncodingKind.Utf16Be;
        return null;
    }

    /// <summary>앞 8KB 안에 NUL 바이트가 있으면 바이너리로 본다 (UTF-16은 먼저 걸러 낸 뒤에 묻는다).</summary>
    public static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        var end = Math.Min(bytes.Length, 8192);
        for (var i = 0; i < end; i++)
            if (bytes[i] == 0) return true;
        return false;
    }

    /// <summary>인코딩을 고른다. BOM이 있으면 <c>HasBom</c>이 true다. 텍스트로 읽을 수 없으면 Latin-1. 자세한 결과는 <see cref="Analyze"/>.</summary>
    public static (TextEncodingKind Kind, bool HasBom) Detect(ReadOnlySpan<byte> bytes)
    {
        var a = Analyze(bytes);
        return (a.Kind, a.HasBom);
    }

    /// <summary>
    /// 인코딩 감지 + 오류 진단 (인코딩 오류 복구의 출발점).
    /// <list type="bullet">
    /// <item>BOM·UTF-16·바이너리는 예전과 같다.</item>
    /// <item>UTF-8 / CP949 / Shift-JIS 중 <b>오류 없이 읽히는 것</b>이 있으면 그중 하나(UTF-8 우선, 그다음 그 언어 글자가 많은 쪽).</item>
    /// <item>모두 오류가 있으면 <b>오류가 가장 적은 것</b>을 고르되 "손상된 텍스트"로 표시한다(<see cref="EncodingAnalysis.Errors"/> &gt; 0).
    /// 대부분 UTF-8인데 한 줄만 CP949로 붙여 넣은 파일이 여기에 해당한다 — 통째로 Latin-1/읽기 전용으로 보여 주지 않는다.
    /// 오류가 0x80 이상 바이트의 절반을 넘으면 어느 것도 아니라고 보고 Latin-1(서유럽 문서)로 읽는다 — 바이너리가 아니라 텍스트다.</item>
    /// </list>
    /// </summary>
    public static EncodingAnalysis Analyze(ReadOnlySpan<byte> bytes)
    {
        if (Bom(bytes) is { } bom) return Single(bom.Kind, true, bytes);
        if (GuessUtf16(bytes) is { } utf16) return Single(utf16, false, bytes);
        if (LooksBinary(bytes)) return new EncodingAnalysis(TextEncodingKind.Latin1, false, 0, [], IsBinary: true);

        var sample = bytes[..Math.Min(bytes.Length, SampleBytes)];
        // 표본을 잘라서 멀티바이트 글자가 중간에 끊겼을 수 있다 → 끝의 몇 바이트는 오류로 보지 않는다.
        var trimmed = bytes.Length > SampleBytes ? sample[..^4] : sample;
        var candidates = new List<EncodingCandidate>
        {
            new(TextEncodingKind.Utf8, CountErrors(trimmed, TextEncodingKind.Utf8), Utf8Multibyte(trimmed)),
            new(TextEncodingKind.Cp949, CountErrors(trimmed, TextEncodingKind.Cp949), HangulPairs(trimmed)),
            new(TextEncodingKind.ShiftJis, CountErrors(trimmed, TextEncodingKind.ShiftJis), KanaPairs(trimmed)),
        };
        // 오류 없는 것이 있으면 그중 UTF-8 → 그 언어 글자가 많은 쪽 → CP949.
        // 모두 오류가 있으면 "그럴듯한 글자 − 오류×2"가 큰 쪽: 오류 수만 보면 UTF-8 한글 바이트가 CP949 확장 영역으로 "읽혀서"
        // CP949가 이기는 일이 생긴다. 그럴듯한 글자 = UTF-8은 정상 멀티바이트 글자, CP949는 완성형 한글, Shift-JIS는 가나.
        candidates.Sort((a, b) =>
        {
            if ((a.Errors == 0) != (b.Errors == 0)) return a.Errors == 0 ? -1 : 1;
            if (a.Errors > 0)
            {
                var (sa, sb) = (a.ScriptScore - a.Errors * 2, b.ScriptScore - b.Errors * 2);
                if (sa != sb) return sb.CompareTo(sa);
            }
            if (a.Kind == TextEncodingKind.Utf8 || b.Kind == TextEncodingKind.Utf8) return a.Kind == TextEncodingKind.Utf8 ? -1 : 1;
            if (a.ScriptScore != b.ScriptScore) return b.ScriptScore.CompareTo(a.ScriptScore);
            return a.Kind == TextEncodingKind.Cp949 ? -1 : 1;
        });
        var best = candidates[0];
        // 오류가 0x80 이상 바이트의 절반을 넘으면 그 인코딩이 아니다 → 서유럽 문서(Latin-1/Windows-1252)로 본다. 이것도 텍스트다(편집·저장 가능).
        var high = 0;
        foreach (var b in trimmed) if (b >= 0x80) high++;
        if (best.Errors * 2 > high) return new EncodingAnalysis(TextEncodingKind.Latin1, false, 0, candidates);
        // 표본에 오류가 없어도 표본 밖에 있을 수 있다 → 파일이 표본보다 크면 전체를 다시 센다.
        var errors = bytes.Length > SampleBytes ? CountErrors(bytes, best.Kind) : best.Errors;
        return new EncodingAnalysis(best.Kind, false, errors, candidates);
    }

    static EncodingAnalysis Single(TextEncodingKind kind, bool hasBom, ReadOnlySpan<byte> bytes) =>
        new(kind, hasBom, CountErrors(Bom(bytes) is { } b && b.Kind == kind ? bytes[b.Length..] : bytes, kind), []);

    /// <summary><paramref name="kind"/>로 읽을 수 없는 바이트 묶음의 수 (각각 �가 된다).</summary>
    public static int CountErrors(ReadOnlySpan<byte> bytes, TextEncodingKind kind)
    {
        if (kind == TextEncodingKind.Latin1) return 0;
        var fallback = new RecordingFallback(0);
        WithFallback(kind, fallback).GetCharCount(bytes);
        return fallback.Count;
    }

    /// <summary>
    /// 읽을 수 없는 바이트가 있는 곳 (화면의 "오류 n곳" 안내와 그 줄로 이동). 줄 번호는 1부터, 최대 <paramref name="max"/>곳.
    /// </summary>
    public static IReadOnlyList<DecodeError> FindErrors(ReadOnlySpan<byte> bytes, TextEncodingKind kind, int max = 100)
    {
        if (kind == TextEncodingKind.Latin1) return [];
        var fallback = new RecordingFallback(max);
        WithFallback(kind, fallback).GetCharCount(bytes);
        var errors = new List<DecodeError>(fallback.Offsets.Count);
        int line = 1, scanned = 0;
        foreach (var offset in fallback.Offsets)
        {
            for (; scanned < offset && scanned < bytes.Length; scanned++)
                if (bytes[scanned] == (byte)'\n') line++;
            errors.Add(new DecodeError(offset, line));
        }
        return errors;
    }

    /// <summary>
    /// 섞인 인코딩 복구: 줄마다 <paramref name="primary"/>로 먼저 읽고, 그 줄이 깨지면 <paramref name="secondary"/>로 읽는다.
    /// 둘 다 안 되면 그 줄은 <paramref name="primary"/>로 읽고(� 포함) 복구하지 못한 줄로 남긴다.
    /// 여러 출처를 이어 붙인 로그·소스(UTF-8 파일에 CP949 한 줄)를 고친다. 결과는 한 인코딩으로 저장하면 된다(보통 UTF-8).
    /// UTF-16은 줄을 바이트로 나눌 수 없어 다루지 않는다.
    /// </summary>
    public static MixedRepair RepairMixed(ReadOnlySpan<byte> bytes, TextEncodingKind primary, TextEncodingKind secondary)
    {
        if (primary is TextEncodingKind.Utf16Le or TextEncodingKind.Utf16Be || secondary is TextEncodingKind.Utf16Le or TextEncodingKind.Utf16Be)
            throw new ArgumentException("UTF-16은 줄 단위 복구를 하지 않는다");
        if (Bom(bytes) is { } bom && bom.Kind == primary) bytes = bytes[bom.Length..];
        var text = new StringBuilder(bytes.Length);
        var repaired = new List<int>();
        var broken = new List<int>();
        var line = 1;
        while (true)
        {
            var nl = bytes.IndexOf((byte)'\n');
            var chunk = nl < 0 ? bytes : bytes[..(nl + 1)];
            if (CountErrors(chunk, primary) == 0) text.Append(Lenient(primary).GetString(chunk));
            else if (CountErrors(chunk, secondary) == 0)
            {
                text.Append(Lenient(secondary).GetString(chunk));
                repaired.Add(line);
            }
            else
            {
                text.Append(Lenient(primary).GetString(chunk));
                broken.Add(line);
            }
            if (nl < 0) break;
            bytes = bytes[(nl + 1)..];
            line++;
        }
        return new MixedRepair(text.ToString(), repaired, broken);
    }

    /// <summary>
    /// "다른 인코딩으로 다시 열기" 메뉴의 후보: 오류가 적은 순으로, 각 후보로 읽은 앞부분 미리보기를 붙인다.
    /// 사용자가 미리보기를 보고 고른다 (자동 감지가 틀렸을 때의 복구 경로).
    /// </summary>
    public static IReadOnlyList<EncodingPreview> Previews(ReadOnlySpan<byte> bytes, int previewChars = 200)
    {
        var sample = bytes[..Math.Min(bytes.Length, SampleBytes)];
        var list = new List<EncodingPreview>();
        foreach (var kind in All)
        {
            var errors = CountErrors(sample, kind);
            var text = Decode(sample, kind);
            list.Add(new EncodingPreview(kind, errors, text.Length > previewChars ? text[..previewChars] : text));
        }
        return list.OrderBy(p => p.Errors).ThenBy(p => p.Kind == TextEncodingKind.Latin1 ? 1 : 0).ToList();
    }

    /// <summary><paramref name="text"/>를 <paramref name="kind"/>로 저장하면 사라지는 글자들 (저장 전 경고에 보여 준다). 최대 <paramref name="max"/>개.</summary>
    public static IReadOnlyList<(int Index, string Text)> Unencodable(string text, TextEncodingKind kind, int max = 20)
    {
        var result = new List<(int, string)>();
        var strict = Strict(kind);
        for (var i = 0; i < text.Length && result.Count < max; i++)
        {
            var len = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
            var piece = text.Substring(i, len);
            try { strict.GetByteCount(piece); }
            catch (EncoderFallbackException) { result.Add((i, piece)); }
            i += len - 1;
        }
        return result;
    }

    /// <summary>바이트를 <paramref name="kind"/>로 읽는다. 읽을 수 없는 바이트는 대체 문자(�)가 된다 (사용자가 고른 인코딩이 틀렸을 때).</summary>
    public static string Decode(ReadOnlySpan<byte> bytes, TextEncodingKind kind)
    {
        if (Bom(bytes) is { } bom && bom.Kind == kind) bytes = bytes[bom.Length..];
        if (kind is not (TextEncodingKind.Cp949 or TextEncodingKind.ShiftJis)) return Lenient(kind).GetString(bytes);
        // 2바이트 코드 페이지 디코더는 깨진 첫 바이트 뒤의 줄바꿈(0x0A)까지 한 글자로 삼켜 줄이 합쳐진다 → 줄마다 따로 읽는다.
        var sb = new StringBuilder(bytes.Length);
        var encoding = Lenient(kind);
        while (true)
        {
            var nl = bytes.IndexOf((byte)'\n');
            if (nl < 0) { sb.Append(encoding.GetString(bytes)); break; }
            sb.Append(encoding.GetString(bytes[..nl])).Append('\n');
            bytes = bytes[(nl + 1)..];
        }
        return sb.ToString();
    }

    /// <summary>글자를 <paramref name="kind"/>의 바이트로. <paramref name="hasBom"/>이면 BOM을 앞에 붙인다 (UTF-8/16만).</summary>
    public static byte[] Encode(string text, TextEncodingKind kind, bool hasBom)
    {
        var body = Lenient(kind).GetBytes(text);
        if (!hasBom) return body;
        byte[] bom = kind switch
        {
            TextEncodingKind.Utf8 => [0xEF, 0xBB, 0xBF],
            TextEncodingKind.Utf16Le => [0xFF, 0xFE],
            TextEncodingKind.Utf16Be => [0xFE, 0xFF],
            _ => [],
        };
        return [.. bom, .. body];
    }

    /// <summary><paramref name="text"/>를 손실 없이 <paramref name="kind"/>로 저장할 수 있는가 (CP949에 없는 글자 등).</summary>
    public static bool CanEncode(string text, TextEncodingKind kind)
    {
        try
        {
            Strict(kind).GetByteCount(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    static int CodePage(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Cp949 => 949,
        TextEncodingKind.ShiftJis => 932,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    static Encoding Strict(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf8 => new UTF8Encoding(false, throwOnInvalidBytes: true),
        TextEncodingKind.Utf16Le => new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true),
        TextEncodingKind.Utf16Be => new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true),
        TextEncodingKind.Latin1 => Encoding.GetEncoding("ISO-8859-1", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        _ => Encoding.GetEncoding(CodePage(kind), EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
    };

    static Encoding Lenient(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf8 => new UTF8Encoding(false),
        TextEncodingKind.Utf16Le => new UnicodeEncoding(bigEndian: false, byteOrderMark: false),
        TextEncodingKind.Utf16Be => new UnicodeEncoding(bigEndian: true, byteOrderMark: false),
        TextEncodingKind.Latin1 => Encoding.Latin1,
        _ => Encoding.GetEncoding(CodePage(kind)),
    };

    static Encoding WithFallback(TextEncodingKind kind, DecoderFallback fallback) => kind switch
    {
        TextEncodingKind.Utf8 => Encoding.GetEncoding("utf-8", EncoderFallback.ReplacementFallback, fallback),
        TextEncodingKind.Utf16Le => Encoding.GetEncoding("utf-16", EncoderFallback.ReplacementFallback, fallback),
        TextEncodingKind.Utf16Be => Encoding.GetEncoding("utf-16BE", EncoderFallback.ReplacementFallback, fallback),
        TextEncodingKind.Latin1 => Encoding.Latin1,
        _ => Encoding.GetEncoding(CodePage(kind), EncoderFallback.ReplacementFallback, fallback),
    };

    /// <summary>읽을 수 없는 바이트를 �로 바꾸면서 몇 번, 어디서였는지 적는다.</summary>
    sealed class RecordingFallback(int keep) : DecoderFallback
    {
        public int Count;
        public readonly List<int> Offsets = [];
        public override int MaxCharCount => 1;
        public override DecoderFallbackBuffer CreateFallbackBuffer() => new Buffer(this, keep);

        sealed class Buffer(RecordingFallback owner, int keep) : DecoderFallbackBuffer
        {
            bool _pending;
            public override int Remaining => _pending ? 1 : 0;

            public override bool Fallback(byte[] bytesUnknown, int index)
            {
                owner.Count++;
                if (owner.Offsets.Count < keep) owner.Offsets.Add(index);
                _pending = true;
                return true;
            }

            public override char GetNextChar()
            {
                if (!_pending) return '\0';
                _pending = false;
                return '\uFFFD';
            }

            public override bool MovePrevious() => false;
        }
    }

    static bool IsValid(ReadOnlySpan<byte> bytes, Encoding strict)
    {
        try
        {
            strict.GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>UTF-8로 읽었을 때 정상인 멀티바이트 글자(U+0080 이상, � 제외)의 수.</summary>
    static int Utf8Multibyte(ReadOnlySpan<byte> b)
    {
        var count = 0;
        foreach (var c in Lenient(TextEncodingKind.Utf8).GetString(b))
            if (c > '\u007F' && c != '\uFFFD') count++;
        return count;
    }

    /// <summary>
    /// CP949로 읽었을 때 KS X 1001 완성형 한글(첫 바이트 B0–C8, 둘째 A1–FE)인 글자 수. 일본어 Shift-JIS 바이트도 CP949 확장 영역으로는
    /// "읽히기" 때문에, 디코딩 결과의 한글 수가 아니라 자주 쓰는 완성형 영역만 센다.
    /// </summary>
    static int HangulPairs(ReadOnlySpan<byte> b)
    {
        var count = 0;
        for (var i = 0; i < b.Length;)
        {
            if (b[i] >= 0x81 && i + 1 < b.Length)
            {
                if (b[i] is >= 0xB0 and <= 0xC8 && b[i + 1] is >= 0xA1 and <= 0xFE) count++;
                i += 2;
            }
            else i++;
        }
        return count;
    }

    /// <summary>Shift-JIS로 읽었을 때 히라가나(82 9F–F1)·가타카나(83 40–96)인 글자 수. 한국어 CP949 바이트에는 거의 나오지 않는다.</summary>
    static int KanaPairs(ReadOnlySpan<byte> b)
    {
        var count = 0;
        for (var i = 0; i < b.Length;)
        {
            var lead = b[i];
            if ((lead is >= 0x81 and <= 0x9F || lead is >= 0xE0 and <= 0xFC) && i + 1 < b.Length)
            {
                var trail = b[i + 1];
                if ((lead == 0x82 && trail is >= 0x9F and <= 0xF1) || (lead == 0x83 && trail is >= 0x40 and <= 0x96)) count++;
                i += 2;
            }
            else i++;
        }
        return count;
    }
}

/// <summary>감지 후보 하나: 그 인코딩으로 읽었을 때의 오류 수와 그 언어 글자 점수.</summary>
public sealed record EncodingCandidate(TextEncodingKind Kind, int Errors, int ScriptScore);

/// <summary>
/// 감지 결과. <see cref="Errors"/>가 0보다 크면 "고른 인코딩으로도 깨진 곳이 있는 텍스트"다 → 화면은 복구 안내(다른 인코딩·섞인 인코딩 복구)를 띄운다.
/// </summary>
/// <param name="IsBinary">NUL 바이트가 있어 텍스트가 아니다 (Latin-1로 바이트를 그대로 보이고 읽기 전용).</param>
public sealed record EncodingAnalysis(TextEncodingKind Kind, bool HasBom, int Errors, IReadOnlyList<EncodingCandidate> Candidates, bool IsBinary = false)
{
    public bool IsDamaged => Errors > 0;
}

/// <summary>읽을 수 없는 바이트의 위치. <see cref="Offset"/>은 파일 안 바이트 위치, <see cref="Line"/>은 1부터.</summary>
public sealed record DecodeError(int Offset, int Line);

/// <summary>섞인 인코딩 복구 결과. 줄 번호는 1부터.</summary>
public sealed record MixedRepair(string Text, IReadOnlyList<int> RepairedLines, IReadOnlyList<int> BrokenLines);

/// <summary>다른 인코딩으로 다시 열기의 후보 하나 (미리보기 포함).</summary>
public sealed record EncodingPreview(TextEncodingKind Kind, int Errors, string Preview);
