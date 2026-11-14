using DaylightCompare.Domain;
using DaylightCompare.Domain.Binary;
using Microsoft.Win32.SafeHandles;

namespace DaylightCompare.Services;

/// <summary>
/// 로컬 파일을 위치로 읽는 원천 (큰 파일도 통째로 메모리에 올리지 않는다). <see cref="RandomAccess"/>는 위치를 인자로 받으므로
/// 비교(백그라운드)와 그리기(UI)가 같은 핸들을 동시에 읽어도 된다.
/// </summary>
public sealed class FileByteSource : IByteSource, IDisposable
{
    readonly SafeFileHandle _handle;

    public FileByteSource(string path)
    {
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
        Length = RandomAccess.GetLength(_handle);
    }

    public long Length { get; }

    public int Read(long offset, Span<byte> destination)
    {
        if (offset < 0 || offset >= Length || destination.IsEmpty) return 0;
        try { return RandomAccess.Read(_handle, destination, offset); }
        catch (ObjectDisposedException) { return 0; }
    }

    public void Dispose() => _handle.Dispose();
}

/// <summary>바이너리 비교로 연 한쪽: 원천 + 파일 정보 (읽지 못했으면 <see cref="Error"/>).</summary>
public sealed record BinarySource(string Path, IByteSource? Source, DateTime Modified, FileError? Error) : IDisposable
{
    public long Length => Source?.Length ?? 0;
    public bool Ok => Source is not null;

    /// <summary>쓰고 난 원천을 닫는다 (파일 핸들).</summary>
    public void Dispose() => (Source as IDisposable)?.Dispose();
}

/// <summary>바이너리 비교가 한쪽 파일을 연다: 로컬 파일은 위치로 읽고, 압축 파일 안 파일은 메모리로 읽는다. 열기·크기 확인은 백그라운드에서 한다.</summary>
public static class BinarySources
{
    public static Task<BinarySource> OpenAsync(string path, CancellationToken ct = default) =>
        Task.Run(() => OpenCoreAsync(path, ct), ct);

    static async Task<BinarySource> OpenCoreAsync(string path, CancellationToken ct)
    {
        try
        {
            if (ArchivePaths.TryParse(path) is { } loc)
            {
                var fs = new ArchiveFileSystem(loc.Archive);
                var stat = await fs.StatAsync(path, ct);
                if (stat is null || stat.IsDirectory) return new BinarySource(path, null, DateTime.MinValue, new FileError.NotFound(Locations.NameOf(path)));
                await using var s = await fs.OpenReadAsync(path, ct);
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms, ct);
                return new BinarySource(path, new MemoryByteSource(ms.ToArray()), stat.ModifiedAt?.UtcDateTime ?? DateTime.MinValue, null);
            }
            var info = new FileInfo(path);
            if (!info.Exists) return new BinarySource(path, null, DateTime.MinValue, new FileError.NotFound(info.Name));
            return new BinarySource(path, new FileByteSource(path), info.LastWriteTimeUtc, null);
        }
        catch (FileOperationException e)
        {
            return new BinarySource(path, null, DateTime.MinValue, e.Error);
        }
        catch (UnauthorizedAccessException)
        {
            return new BinarySource(path, null, DateTime.MinValue, new FileError.AccessDenied(Locations.NameOf(path)));
        }
        catch (IOException e)
        {
            return new BinarySource(path, null, DateTime.MinValue, new FileError.Other(e.Message));
        }
    }
}
