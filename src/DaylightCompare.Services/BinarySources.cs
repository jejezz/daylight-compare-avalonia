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
/// <param name="BaseAddress">변환한 펌웨어 이미지의 시작 주소 (그냥 파일이면 0) — 화면의 오프셋 칸이 주소로 보인다.</param>
public sealed record BinarySource(string Path, IByteSource? Source, DateTime Modified, FileError? Error) : IDisposable
{
    public long BaseAddress { get; init; }

    /// <summary>변환했으면 그 종류 ("Intel HEX" · "S-Record")와 레코드 수·경고들.</summary>
    public string? ConvertedFrom { get; init; }
    public int Records { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    /// <summary>변환하지 못한 이유 (그때는 파일을 그대로 연다).</summary>
    public string? ConvertError { get; init; }

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

    /// <summary>
    /// 펌웨어 파일(Intel HEX·S-Record)을 이미지로 바꿔서 연다 (<paramref name="builtin"/> = <c>ihex-to-bin</c> · <c>srec-to-bin</c>, <paramref name="fill"/> = 주소 사이의 빈틈을 채울 값).
    /// 바꾸지 못하면(레코드가 없거나 모르는 변환) 파일을 그대로 열고 <see cref="BinarySource.ConvertError"/>에 이유를 남긴다.
    /// </summary>
    public static async Task<BinarySource> OpenConvertedAsync(string path, string builtin, byte fill, CancellationToken ct = default)
    {
        var raw = await OpenAsync(path, ct);
        if (!raw.Ok || raw.Source is not { } source) return raw;
        if (source.Length > FirmwareFormats.MaxImageBytes) return raw with { ConvertError = "파일이 너무 큽니다" };
        var bytes = new byte[source.Length];
        var read = await Task.Run(() => { var total = 0; while (total < bytes.Length) { var n = source.Read(total, bytes.AsSpan(total)); if (n <= 0) break; total += n; } return total; }, ct);
        var result = FirmwareFormats.Convert(builtin, bytes.AsSpan(0, read), fill);
        if (result.Image is not { } image) return raw with { ConvertError = result.Error };
        raw.Dispose();
        return new BinarySource(path, new MemoryByteSource(image.Data), raw.Modified, null)
        {
            BaseAddress = image.BaseAddress,
            ConvertedFrom = builtin == "srec-to-bin" ? "S-Record" : "Intel HEX",
            Records = image.Records,
            Warnings = image.Warnings,
        };
    }

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
