using DaylightCompare.Domain;

namespace DaylightCompare.Services;

/// <summary>비교할 한쪽을 읽은 결과.</summary>
public abstract record CompareSource(string Path)
{
    /// <summary>텍스트로 읽었다. <see cref="Bytes"/>는 원래 바이트 (인코딩 다시 열기·섞인 인코딩 복구에 쓴다).</summary>
    public sealed record Text(string Path, EditorTextFile File, byte[] Bytes) : CompareSource(Path);

    /// <summary>NUL 바이트가 있는 바이너리 — 텍스트 비교 대신 바이너리 비교(5단계)로 안내한다.</summary>
    public sealed record Binary(string Path, long Length) : CompareSource(Path);

    /// <summary>텍스트 비교로 열기에는 너무 크다.</summary>
    public sealed record TooLarge(string Path, long Length) : CompareSource(Path);

    public sealed record Failed(string Path, FileError Error) : CompareSource(Path);
}

/// <summary>
/// 비교 화면이 한쪽 파일을 읽는다: 로컬 파일 또는 압축 파일 안의 파일(<c>a.zip!/x</c>). 원격(SFTP 등)은 4단계에서 같은 자리에 붙는다.
/// </summary>
public static class CompareSources
{
    /// <summary>텍스트 비교로 여는 크기 상한 (편집기 상한 5MB보다 크게 — 로그·생성 파일 비교).</summary>
    public const int TextMaxBytes = 32 * 1024 * 1024;

    /// <summary>
    /// 한쪽을 읽는다. 파일 정보 확인(존재·크기)과 읽기는 <b>백그라운드에서</b> 한다 — 네트워크 드라이브에서는 이 호출만으로 UI가 멈출 수 있다.
    /// </summary>
    public static Task<CompareSource> LoadAsync(string path, TextEncodingKind? forced = null, CancellationToken ct = default) =>
        Task.Run(() => LoadCoreAsync(path, forced, ct), ct);

    static async Task<CompareSource> LoadCoreAsync(string path, TextEncodingKind? forced, CancellationToken ct)
    {
        try
        {
            byte[] bytes;
            DateTime modified;
            if (ArchivePaths.TryParse(path) is { } loc)
            {
                var fs = new ArchiveFileSystem(loc.Archive);
                var stat = await fs.StatAsync(path, ct);
                if (stat is null || stat.IsDirectory) return new CompareSource.Failed(path, new FileError.NotFound(Locations.NameOf(path)));
                if (stat.SizeBytes > TextMaxBytes) return new CompareSource.TooLarge(path, stat.SizeBytes ?? 0);
                await using var s = await fs.OpenReadAsync(path, ct);
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms, ct);
                bytes = ms.ToArray();
                modified = stat.ModifiedAt?.UtcDateTime ?? DateTime.MinValue;
            }
            else
            {
                var info = new FileInfo(path);
                if (!info.Exists) return new CompareSource.Failed(path, new FileError.NotFound(info.Name));
                if (info.Length > TextMaxBytes) return new CompareSource.TooLarge(path, info.Length);
                bytes = await File.ReadAllBytesAsync(path, ct);
                modified = info.LastWriteTimeUtc;
            }
            var result = TextFiles.Decode(bytes, modified, forced, TextMaxBytes);
            if (result.File is not { } file) return new CompareSource.TooLarge(path, bytes.Length);
            if (file.Binary) return new CompareSource.Binary(path, bytes.Length);
            return new CompareSource.Text(path, file with { Text = TextFiles.NormalizeLineBreaks(file.Text) }, bytes);
        }
        catch (FileOperationException e)
        {
            return new CompareSource.Failed(path, e.Error);
        }
        catch (UnauthorizedAccessException)
        {
            return new CompareSource.Failed(path, new FileError.AccessDenied(Locations.NameOf(path)));
        }
        catch (IOException e)
        {
            return new CompareSource.Failed(path, new FileError.Other(e.Message));
        }
    }
}
