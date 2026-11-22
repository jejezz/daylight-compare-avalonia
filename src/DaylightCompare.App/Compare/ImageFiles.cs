using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Imaging;
using DaylightCompare.Services;

namespace DaylightCompare.App.Compare;

/// <summary>그림 비교로 연 한쪽: 비교용 픽셀 + 화면에 그릴 비트맵 (읽지 못했으면 <see cref="Error"/>·<see cref="DecodeError"/>).</summary>
sealed record LoadedImage(string Path, PixelImage? Pixels, WriteableBitmap? Bitmap, DateTime Modified, long Length, FileError? Error, string? DecodeError) : IDisposable
{
    public bool Ok => Pixels is not null && Bitmap is not null;

    public void Dispose() => Bitmap?.Dispose();
}

/// <summary>그림 파일을 읽는다 (디스크·디코딩은 모두 백그라운드 — UI 스레드는 건드리지 않는다).</summary>
static class ImageFiles
{
    /// <summary>파일이 이보다 크면 읽지 않는다.</summary>
    public const long MaxFileBytes = 256L * 1024 * 1024;

    public static Task<LoadedImage> LoadAsync(string path, CancellationToken ct = default) => Task.Run(async () =>
    {
        using var source = await BinarySources.OpenAsync(path, ct);
        if (!source.Ok || source.Source is not { } bytesSource)
            return new LoadedImage(path, null, null, DateTime.MinValue, 0, source.Error, null);
        var length = bytesSource.Length;
        if (length > MaxFileBytes)
            return new LoadedImage(path, null, null, source.Modified, length, null, Strings.Current.ImageTooLarge);
        var bytes = new byte[length];
        var total = 0;
        while (total < bytes.Length)
        {
            var n = bytesSource.Read(total, bytes.AsSpan(total));
            if (n <= 0) break;
            total += n;
        }
        try
        {
            var (pixels, bitmap) = Decode(bytes.AsSpan(0, total).ToArray());
            return new LoadedImage(path, pixels, bitmap, source.Modified, length, null, null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new LoadedImage(path, null, null, source.Modified, length, null, e.Message);
        }
    }, ct);

    /// <summary>그림 바이트를 풀어 BGRA 직선 알파 픽셀과 같은 내용의 비트맵(96dpi, 1픽셀=1단위)을 만든다.</summary>
    internal static (PixelImage Pixels, WriteableBitmap Bitmap) Decode(byte[] encoded)
    {
        using var stream = new MemoryStream(encoded);
        using var decoded = new Bitmap(stream);
        var size = decoded.PixelSize;
        if (size.Width <= 0 || size.Height <= 0 || (long)size.Width * size.Height > ImageDiff.MaxCanvasPixels)
            throw new InvalidDataException(Strings.Current.ImageTooLarge);
        var bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        var pixels = new byte[size.Width * size.Height * 4];
        using (var fb = bitmap.Lock())
        {
            decoded.CopyPixels(fb);
            var row = size.Width * 4;
            for (var y = 0; y < size.Height; y++)
                Marshal.Copy(fb.Address + y * fb.RowBytes, pixels, y * row, row);
        }
        return (new PixelImage(size.Width, size.Height, pixels), bitmap);
    }

    /// <summary>다른 픽셀 마스크를 붉은 비트맵으로 (다르면 불투명한 붉은색, 같으면 투명).</summary>
    public static WriteableBitmap MaskBitmap(ImageDiffResult result)
    {
        var w = result.Layout.Width;
        var h = result.Layout.Height;
        var bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var fb = bitmap.Lock();
        var row = new byte[w * 4];
        for (var y = 0; y < h; y++)
        {
            Array.Clear(row);
            for (var x = 0; x < w; x++)
            {
                if (result.Mask[y * w + x] == 0) continue;
                row[x * 4 + 2] = 255; // R
                row[x * 4 + 3] = 255; // A
            }
            Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
        }
        return bitmap;
    }
}
