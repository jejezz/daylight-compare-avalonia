// From daylight-commander-avalonia @ 4686eb7
using System.Text;

namespace DaylightCompare.Services;

/// <summary>
/// 읽어 들인 텍스트 파일 (원본 <c>EditorTextFile</c>). 저장할 때 BOM과 줄바꿈을 그대로 되돌리려고 함께 들고 있다.
/// <paramref name="Text"/>는 읽은 그대로의 본문이다 (줄바꿈 정규화 전).
/// </summary>
/// <param name="LineBreak"><c>\n</c> 또는 <c>\r\n</c>. 섞여 있으면 많은 쪽.</param>
/// <param name="Binary">
/// 바이너리이거나 어느 인코딩으로도 읽히지 않아서 바이트를 글자 하나씩(Latin-1) 그대로 보여 주는 파일.
/// 줄바꿈 정규화와 BOM 처리가 바이트를 바꿀 수 있어 읽기 전용으로만 연다.
/// </param>
/// <param name="Encoding">읽은 인코딩. 저장할 때 같은 인코딩으로 쓴다 (Daylight Compare에서 더함 — Commander는 UTF-8만 읽었다).</param>
/// <param name="DecodeErrors">
/// 그 인코딩으로 읽을 수 없어 �가 된 곳의 수. 0보다 크면 화면이 복구 안내(다른 인코딩으로 다시 열기 · 섞인 인코딩 복구)를 띄우고,
/// 그대로 저장하려 하면 원래 바이트가 사라진다고 경고한다.
/// </param>
public sealed record EditorTextFile(string Text, bool HasBom, string LineBreak, DateTime Modified, long Length, bool Binary,
    TextEncodingKind Encoding = TextEncodingKind.Utf8, int DecodeErrors = 0);

public enum EditorLoadFailure { TooLarge }

public sealed record EditorLoadResult(EditorTextFile? File, EditorLoadFailure? Failure)
{
    public static EditorLoadResult Ok(EditorTextFile file) => new(file, null);
    public static EditorLoadResult Failed(EditorLoadFailure failure) => new(null, failure);
}

/// <summary>내장 에디터(F4)가 텍스트 파일을 읽고 쓰는 규칙 (원본 <c>text_file.dart</c>).</summary>
public static class TextFiles
{
    /// <summary>에디터가 한 번에 열 수 있는 파일 크기 상한.</summary>
    public const int EditorMaxBytes = 5 * 1024 * 1024;

    /// <summary>앞 8KB 안에 NUL 바이트가 있으면 바이너리로 본다 (읽기 전용으로 연다). UTF-16 판정은 <see cref="TextEncodings.Detect"/>가 먼저 한다.</summary>
    public static bool LooksBinary(ReadOnlySpan<byte> bytes) => TextEncodings.LooksBinary(bytes);

    /// <summary>본문에서 줄바꿈 종류를 센다. CRLF가 LF 단독보다 많으면 <c>\r\n</c>.</summary>
    public static string DetectLineBreak(string text)
    {
        int crlf = 0, lf = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            if (i > 0 && text[i - 1] == '\r') crlf++; else lf++;
        }
        return crlf > lf ? "\r\n" : "\n";
    }

    /// <summary>CRLF·CR을 모두 LF로 맞춘다 (편집 화면은 LF만 쓰고, 저장할 때 원래 줄바꿈으로 되돌린다).</summary>
    public static string NormalizeLineBreaks(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>LF로 정규화된 본문을 <paramref name="lineBreak"/>로 이어 붙인다.</summary>
    public static string ApplyLineBreak(string text, string lineBreak) =>
        lineBreak == "\n" ? text : text.Replace("\n", lineBreak);

    /// <summary>
    /// 바이트를 본문으로. <paramref name="forced"/>가 없으면 인코딩을 감지하고(<see cref="TextEncodings.Detect"/>), 있으면 그 인코딩으로 읽는다
    /// (사용자가 상태 표시줄에서 고른 경우 — 틀린 바이트는 �가 된다). 어느 것으로도 읽히지 않으면 Latin-1로 보여 주고 읽기 전용(Binary)이다.
    /// </summary>
    public static EditorLoadResult Decode(byte[] bytes, DateTime modified, TextEncodingKind? forced = null, int maxBytes = EditorMaxBytes)
    {
        if (bytes.Length > maxBytes) return EditorLoadResult.Failed(EditorLoadFailure.TooLarge);
        var analysis = TextEncodings.Analyze(bytes);
        var (kind, hasBom, errors) = (analysis.Kind, analysis.HasBom, analysis.Errors);
        var binary = analysis.IsBinary;
        if (forced is { } chosen)
        {
            hasBom = TextEncodings.Bom(bytes) is { } bom && bom.Kind == chosen;
            kind = chosen;
            binary = chosen == TextEncodingKind.Latin1 && TextEncodings.LooksBinary(bytes);
            errors = TextEncodings.CountErrors(hasBom ? bytes.AsSpan(TextEncodings.Bom(bytes)!.Value.Length) : bytes, chosen);
        }
        var text = TextEncodings.Decode(bytes, kind);
        return EditorLoadResult.Ok(new EditorTextFile(
            text, HasBom: hasBom && !binary, DetectLineBreak(text), modified, bytes.Length, binary, kind, errors));
    }

    /// <summary>
    /// 섞인 인코딩 복구로 다시 읽는다 (<see cref="TextEncodings.RepairMixed"/>). 결과는 UTF-8로 저장하도록 표시한다 — 원래처럼 섞어서 쓰지 않는다.
    /// 복구하지 못한 줄이 남으면 <see cref="EditorTextFile.DecodeErrors"/>에 그 줄 수를 둔다.
    /// </summary>
    public static (EditorTextFile File, MixedRepair Repair) DecodeMixed(byte[] bytes, DateTime modified, TextEncodingKind primary, TextEncodingKind secondary)
    {
        var repair = TextEncodings.RepairMixed(bytes, primary, secondary);
        var file = new EditorTextFile(repair.Text, HasBom: false, DetectLineBreak(repair.Text), modified, bytes.Length, Binary: false,
            TextEncodingKind.Utf8, repair.BrokenLines.Count);
        return (file, repair);
    }

    public static async Task<EditorLoadResult> LoadAsync(string path, CancellationToken ct = default, TextEncodingKind? forced = null)
    {
        var info = new FileInfo(path);
        if (info.Length > EditorMaxBytes) return EditorLoadResult.Failed(EditorLoadFailure.TooLarge);
        return Decode(await File.ReadAllBytesAsync(path, ct), info.LastWriteTimeUtc, forced);
    }

    /// <summary>저장할 바이트. <paramref name="text"/>는 이미 원래 줄바꿈으로 이어 붙인 본문이다.</summary>
    public static byte[] Encode(string text, bool hasBom, TextEncodingKind encoding = TextEncodingKind.Utf8) =>
        TextEncodings.Encode(text, encoding, hasBom);

    /// <summary>열었을 때와 디스크의 수정 시각·크기가 달라졌는가. 파일이 없으면 false (새로 쓰면 되므로).</summary>
    public static bool ChangedOnDisk(string path, EditorTextFile opened)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return false;
        return info.LastWriteTimeUtc != opened.Modified || info.Length != opened.Length;
    }

    /// <summary>
    /// <paramref name="path"/>에 제자리로 쓰고(권한 비트를 보존하려고 임시 파일 교체 대신) 새 기준값(수정 시각·크기)을 가진
    /// <see cref="EditorTextFile"/>을 돌려준다. <paramref name="text"/>는 LF로 정규화된 편집 본문이다.
    /// </summary>
    public static async Task<EditorTextFile> SaveAsync(string path, string text, EditorTextFile opened, CancellationToken ct = default)
    {
        var bytes = Encode(ApplyLineBreak(text, opened.LineBreak), opened.HasBom, opened.Encoding);
        await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            await stream.WriteAsync(bytes, ct);
            await stream.FlushAsync(ct);
        }
        var info = new FileInfo(path);
        return opened with { Text = text, Modified = info.LastWriteTimeUtc, Length = info.Length };
    }
}
