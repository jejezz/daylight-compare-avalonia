using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Binary;

/// <summary>
/// 바이트를 읽는 원천 (UI·IO 없는 Domain의 비교 엔진이 파일을 모르고도 쓰도록). 메모리·메모리 맵 파일·압축 안 파일이 같은 모양이다.
/// 구현은 <see cref="Read"/>를 여러 스레드에서 동시에 불러도 되게 만든다 (비교는 백그라운드, 그리기는 UI에서 읽는다).
/// </summary>
public interface IByteSource
{
    long Length { get; }

    /// <summary><paramref name="offset"/>부터 <paramref name="destination"/>을 채운다. 읽은 바이트 수 (끝을 넘으면 적게, 0이면 끝).</summary>
    int Read(long offset, Span<byte> destination);
}

public sealed class MemoryByteSource(byte[] data) : IByteSource
{
    /// <summary>안에 든 바이트 (읽기용 — 고치지 않는다).</summary>
    public byte[] Data => data;

    public long Length => data.Length;

    public int Read(long offset, Span<byte> destination)
    {
        if (offset < 0 || offset >= data.Length) return 0;
        var n = (int)Math.Min(destination.Length, data.Length - offset);
        data.AsSpan((int)offset, n).CopyTo(destination);
        return n;
    }
}

/// <summary>
/// 무시 구간(헤더의 타임스탬프·CRC 자리 등)의 바이트를 <paramref name="fill"/>로 가려서 보이는 원천 — 비교 엔진이 이것을 읽으면 그 구간은 늘 같다.
/// 화면은 원래 원천을 읽고 무시 구간만 흐리게 칠한다.
/// </summary>
public sealed class MaskedByteSource : IByteSource
{
    readonly IByteSource _inner;
    readonly ByteRange[] _ranges;
    readonly byte _fill;

    public MaskedByteSource(IByteSource inner, IReadOnlyList<ByteRange> ignore, byte fill = 0)
    {
        _inner = inner;
        _ranges = [.. ignore.OrderBy(r => r.Start)];
        _fill = fill;
    }

    public long Length => _inner.Length;

    public int Read(long offset, Span<byte> destination)
    {
        var n = _inner.Read(offset, destination);
        if (n <= 0 || _ranges.Length == 0) return n;
        var end = offset + n;
        foreach (var r in _ranges)
        {
            if (r.Start >= end) break;
            var from = Math.Max(r.Start, offset);
            var to = Math.Min(r.Start + r.Length, end);
            if (from < to) destination.Slice((int)(from - offset), (int)(to - from)).Fill(_fill);
        }
        return n;
    }
}
