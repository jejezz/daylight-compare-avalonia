namespace DaylightCompare.Services;

/// <summary>저장 전에 사용자에게 물어야 할 것들.</summary>
/// <param name="ChangedOnDisk">연 뒤에 다른 프로그램이 파일을 바꿨다 (덮어쓰면 그 변경이 사라진다).</param>
/// <param name="Lost">지금 인코딩으로 저장하면 사라지는 글자 (최대 20개).</param>
/// <param name="Damaged">읽을 때 깨져서 �로 바뀐 곳의 수 (그대로 저장하면 원래 바이트를 잃는다).</param>
public sealed record SaveCheck(bool ChangedOnDisk, IReadOnlyList<(int Index, string Text)> Lost, int Damaged)
{
    public bool NeedsConfirmation => ChangedOnDisk || Lost.Count > 0 || Damaged > 0;
}

/// <summary>
/// 비교 창의 저장 (ROADMAP §6-1 ② 저장 전 손실 확인, ③ 덮어쓰기 전 자동 백업).
/// 쓰기 규칙은 <see cref="TextFiles.SaveAsync"/>(읽은 인코딩·BOM·줄바꿈 그대로, 제자리 쓰기)를 따른다.
/// </summary>
public static class CompareSave
{
    /// <summary>백업을 남겨 두는 기간.</summary>
    public static readonly TimeSpan KeepBackups = TimeSpan.FromDays(30);

    /// <param name="text">LF로 정규화된 실제 글.</param>
    public static SaveCheck Check(string path, EditorTextFile opened, string text) => new(
        TextFiles.ChangedOnDisk(path, opened),
        TextEncodings.Unencodable(text, opened.Encoding),
        text.Count(c => c == '�') > 0 ? opened.DecodeErrors : 0);

    /// <summary>
    /// 저장한다. 파일이 이미 있으면 먼저 <paramref name="backupRoot"/>에 사본을 남긴다(<see cref="Backup"/>).
    /// <paramref name="encoding"/>을 주면 그 인코딩으로 바꿔 저장한다 (손실 경고에서 [UTF-8로 저장]을 고른 경우 — UTF-8은 BOM 없이).
    /// 새 기준값(수정 시각·크기·인코딩)을 가진 <see cref="EditorTextFile"/>을 돌려준다.
    /// </summary>
    public static async Task<(EditorTextFile File, string? BackupPath)> SaveAsync(
        string path, string text, EditorTextFile opened, string backupRoot, TextEncodingKind? encoding = null, DateTime? now = null)
    {
        var backup = File.Exists(path) ? Backup(path, backupRoot, now ?? DateTime.Now) : null;
        var target = encoding is { } e && e != opened.Encoding
            ? opened with { Encoding = e, HasBom = e is TextEncodingKind.Utf16Le or TextEncodingKind.Utf16Be, DecodeErrors = 0 }
            : opened;
        var saved = await TextFiles.SaveAsync(path, text, target);
        return (saved with { DecodeErrors = 0 }, backup);
    }

    /// <summary>
    /// 덮어쓰기 전 사본: <c>&lt;backupRoot&gt;/yyyyMMdd/&lt;이름&gt;.HHmmss[.n].bak</c>. 같은 초에 여러 번이면 번호를 붙인다.
    /// 백업을 못 남겨도 저장은 막지 않는다 (null). 남길 때마다 <see cref="KeepBackups"/>보다 오래된 날짜 폴더를 지운다.
    /// </summary>
    public static string? Backup(string path, string backupRoot, DateTime now)
    {
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(backupRoot, now.ToString("yyyyMMdd"))).FullName;
            var name = Path.GetFileName(path);
            var target = Path.Combine(dir, $"{name}.{now:HHmmss}.bak");
            for (var n = 1; File.Exists(target); n++) target = Path.Combine(dir, $"{name}.{now:HHmmss}.{n}.bak");
            File.Copy(path, target);
            Prune(backupRoot, now);
            return target;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>날짜 폴더 이름이 <see cref="KeepBackups"/>보다 오래된 것을 지운다. 이름이 날짜가 아닌 폴더는 건드리지 않는다.</summary>
    public static void Prune(string backupRoot, DateTime now)
    {
        if (!Directory.Exists(backupRoot)) return;
        foreach (var dir in Directory.EnumerateDirectories(backupRoot))
        {
            if (!DateTime.TryParseExact(Path.GetFileName(dir), "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var day)) continue;
            if (now.Date - day > KeepBackups)
                try { Directory.Delete(dir, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
