using DaylightCompare.Domain.Diff;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Binary;

/// <summary>
/// 바이너리 비교 결과의 구간 하나: 왼쪽 [<see cref="LeftStart"/>, +<see cref="LeftLength"/>)와 오른쪽 [<see cref="RightStart"/>, +<see cref="RightLength"/>) (바이트 위치).
/// <see cref="DiffBlockKind.Equal"/>은 두 길이가 같다. <see cref="DiffBlockKind.LeftOnly"/>는 오른쪽 길이가 0, <see cref="DiffBlockKind.RightOnly"/>는 왼쪽 길이가 0.
/// </summary>
public readonly record struct ByteBlock(DiffBlockKind Kind, long LeftStart, long LeftLength, long RightStart, long RightLength)
{
    public bool IsDifference => Kind is DiffBlockKind.Changed or DiffBlockKind.LeftOnly or DiffBlockKind.RightOnly;
    public long LeftEnd => LeftStart + LeftLength;
    public long RightEnd => RightStart + RightLength;
}

/// <summary>바이너리 비교 결과: 구간들 (빈틈 없이 두 파일 전체를 덮는다).</summary>
public sealed class BinaryDiffResult(IReadOnlyList<ByteBlock> blocks, long leftLength, long rightLength)
{
    public IReadOnlyList<ByteBlock> Blocks { get; } = blocks;
    public long LeftLength { get; } = leftLength;
    public long RightLength { get; } = rightLength;

    /// <summary>다른 구간의 수 (바뀜·한쪽에만 있음).</summary>
    public int DifferenceCount => Blocks.Count(b => b.IsDifference);

    public bool IsIdentical => DifferenceCount == 0 && LeftLength == RightLength;

    /// <summary>다른 구간들이 차지하는 바이트 수 (바뀐 구간은 긴 쪽).</summary>
    public long DifferentBytes => Blocks.Where(b => b.IsDifference).Sum(b => Math.Max(b.LeftLength, b.RightLength));
}

/// <summary>
/// 바이너리 비교 엔진 (ROADMAP §5, 5a): 큰 블록(64KB)을 통째로 비교해 같은 구간은 건너뛰고, 다른 블록만 바이트 단위로 본다.
/// <list type="bullet">
/// <item><see cref="BinaryAlignKind.Fixed"/>: 같은 위치끼리만 비교한다 (길이가 다르면 남은 꼬리는 한쪽에만 있는 구간). 펌웨어 이미지처럼 위치가 의미 있는 파일.</item>
/// <item><see cref="BinaryAlignKind.Insertions"/>: 어긋나면 rsync처럼 롤링 해시로 다시 맞춘다 — 사이에 바이트가 끼거나 빠져도 뒤가 모두 다르게 나오지 않는다.</item>
/// </list>
/// 무시 구간은 이 엔진 밖에서 <see cref="MaskedByteSource"/>로 가린다.
/// </summary>
public static class BinaryDiff
{
    /// <summary>같은 위치를 통째로 비교하는 블록 크기.</summary>
    public const int BlockSize = 64 * 1024;

    /// <summary>다시 맞출 때 같은지 보는 덩어리의 길이 (롤링 해시 창).</summary>
    public const int ChunkSize = 16;

    /// <summary>다시 맞출 자리를 찾는 거리: 가까운 곳부터 차례로 넓힌다 (대부분의 삽입은 아주 작으므로 앞 단계에서 끝난다).</summary>
    static readonly int[] ResyncWindows = [4 * 1024, 64 * 1024, 1024 * 1024];

    public static BinaryDiffResult Compare(IByteSource left, IByteSource right, BinaryAlignKind align = BinaryAlignKind.Fixed, CancellationToken ct = default)
    {
        var blocks = new List<ByteBlock>();
        if (align == BinaryAlignKind.Fixed) CompareFixed(left, right, blocks, ct);
        else CompareWithInsertions(left, right, blocks, ct);
        return new BinaryDiffResult(blocks, left.Length, right.Length);
    }

    /// <summary>블록 목록에 더한다: 앞 구간과 같은 종류이고 이어 붙어 있으면 합친다.</summary>
    static void Add(List<ByteBlock> blocks, DiffBlockKind kind, long ls, long ll, long rs, long rl)
    {
        if (ll == 0 && rl == 0) return;
        if (blocks.Count > 0)
        {
            var last = blocks[^1];
            if (last.Kind == kind && last.LeftEnd == ls && last.RightEnd == rs)
            {
                blocks[^1] = last with { LeftLength = last.LeftLength + ll, RightLength = last.RightLength + rl };
                return;
            }
        }
        blocks.Add(new ByteBlock(kind, ls, ll, rs, rl));
    }

    // --- 고정 위치 ---

    static void CompareFixed(IByteSource left, IByteSource right, List<ByteBlock> blocks, CancellationToken ct)
    {
        var common = Math.Min(left.Length, right.Length);
        var a = new byte[BlockSize];
        var b = new byte[BlockSize];
        for (long pos = 0; pos < common;)
        {
            ct.ThrowIfCancellationRequested();
            var want = (int)Math.Min(BlockSize, common - pos);
            var na = ReadFully(left, pos, a.AsSpan(0, want));
            var nb = ReadFully(right, pos, b.AsSpan(0, want));
            var n = Math.Min(na, nb);
            if (n <= 0) break;
            if (a.AsSpan(0, n).SequenceEqual(b.AsSpan(0, n)))
            {
                Add(blocks, DiffBlockKind.Equal, pos, n, pos, n); // 같은 블록은 건너뛴다
            }
            else
            {
                // 다른 블록만 바이트 단위로: 같은 런과 다른 런으로 나눈다.
                var i = 0;
                while (i < n)
                {
                    var same = a[i] == b[i];
                    var j = i + 1;
                    while (j < n && (a[j] == b[j]) == same) j++;
                    Add(blocks, same ? DiffBlockKind.Equal : DiffBlockKind.Changed, pos + i, j - i, pos + i, j - i);
                    i = j;
                }
            }
            pos += n;
        }
        if (left.Length > common) Add(blocks, DiffBlockKind.LeftOnly, common, left.Length - common, common, 0);
        else if (right.Length > common) Add(blocks, DiffBlockKind.RightOnly, common, 0, common, right.Length - common);
    }

    static int ReadFully(IByteSource source, long offset, Span<byte> destination)
    {
        var total = 0;
        while (total < destination.Length)
        {
            var n = source.Read(offset + total, destination[total..]);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    // --- 삽입 감지 ---

    static void CompareWithInsertions(IByteSource left, IByteSource right, List<ByteBlock> blocks, CancellationToken ct)
    {
        long i = 0, j = 0;
        long la = left.Length, lb = right.Length;
        var a = new byte[BlockSize];
        var b = new byte[BlockSize];
        while (i < la && j < lb)
        {
            ct.ThrowIfCancellationRequested();
            // 1) 같은 구간을 블록 단위로 건너뛴다.
            var equal = CommonPrefix(left, right, i, j, Math.Min(la - i, lb - j), a, b);
            if (equal > 0)
            {
                Add(blocks, DiffBlockKind.Equal, i, equal, j, equal);
                i += equal;
                j += equal;
                continue;
            }
            // 2) 어긋난 자리: 가장 가까운 다시 맞출 자리를 찾는다 (왼쪽 y바이트·오른쪽 x바이트를 건너뛰면 같은 덩어리가 나오는 자리, y+x가 가장 작은 것).
            if (FindResync(left, right, i, j, out var y, out var x))
            {
                Add(blocks, y > 0 && x > 0 ? DiffBlockKind.Changed : y > 0 ? DiffBlockKind.LeftOnly : DiffBlockKind.RightOnly, i, y, j, x);
                i += y;
                j += x;
                continue;
            }
            // 3) 가까운 곳에서 맞출 자리가 없다: 남은 만큼을 통째로 다른 구간으로 본다 (파일 끝이 가깝거나 아주 많이 다르다).
            var rest = Math.Min(la - i, lb - j);
            if (rest > ChunkSize * 4 && la - i > BlockSize && lb - j > BlockSize)
            {
                // 아직 멀다: 한 블록을 다른 것으로 치고 앞으로 가서 다시 시도한다 (다른 구간이 한없이 이어져도 멈추지 않는다).
                Add(blocks, DiffBlockKind.Changed, i, BlockSize, j, BlockSize);
                i += BlockSize;
                j += BlockSize;
            }
            else
            {
                Add(blocks, DiffBlockKind.Changed, i, la - i, j, lb - j);
                i = la;
                j = lb;
            }
        }
        if (i < la) Add(blocks, DiffBlockKind.LeftOnly, i, la - i, j, 0);
        else if (j < lb) Add(blocks, DiffBlockKind.RightOnly, i, 0, j, lb - j);
    }

    /// <summary>두 위치에서 시작하는 같은 바이트의 길이 (최대 <paramref name="max"/>).</summary>
    static long CommonPrefix(IByteSource left, IByteSource right, long i, long j, long max, byte[] a, byte[] b)
    {
        long total = 0;
        while (total < max)
        {
            var want = (int)Math.Min(BlockSize, max - total);
            var na = ReadFully(left, i + total, a.AsSpan(0, want));
            var nb = ReadFully(right, j + total, b.AsSpan(0, want));
            var n = Math.Min(na, nb);
            if (n <= 0) break;
            var same = a.AsSpan(0, n).CommonPrefixLength(b.AsSpan(0, n));
            total += same;
            if (same < n) break;
        }
        return total;
    }

    /// <summary>
    /// (i, j)에서 어긋났을 때 가장 가까운 다시 맞출 자리: 왼쪽에서 y, 오른쪽에서 x바이트 뒤에서 <see cref="ChunkSize"/>바이트가 같아지는 (y, x) 중 y+x가 가장 작은 것.
    /// 오른쪽의 창 안 모든 덩어리를 해시 표에 넣고 왼쪽 덩어리를 하나씩 밀며 찾는다 (롤링 해시, 해시가 맞으면 바이트로 다시 확인).
    /// </summary>
    static bool FindResync(IByteSource left, IByteSource right, long i, long j, out long y, out long x)
    {
        y = x = 0;
        foreach (var window in ResyncWindows)
        {
            var la = (int)Math.Min(left.Length - i, window + ChunkSize);
            var lb = (int)Math.Min(right.Length - j, window + ChunkSize);
            if (la < ChunkSize || lb < ChunkSize) return false;
            var a = new byte[la];
            var b = new byte[lb];
            la = ReadFully(left, i, a);
            lb = ReadFully(right, j, b);
            if (la < ChunkSize || lb < ChunkSize) return false;

            var table = new Dictionary<uint, int>();
            var hash = Hash(b.AsSpan(0, ChunkSize));
            table.TryAdd(hash, 0);
            for (var p = 1; p + ChunkSize <= lb; p++)
            {
                hash = Roll(hash, b[p - 1], b[p + ChunkSize - 1]);
                table.TryAdd(hash, p); // 같은 해시는 가장 가까운 것만 둔다
            }

            long best = long.MaxValue;
            hash = Hash(a.AsSpan(0, ChunkSize));
            for (var q = 0; q + ChunkSize <= la && q < best; q++)
            {
                if (q > 0) hash = Roll(hash, a[q - 1], a[q + ChunkSize - 1]);
                if (!table.TryGetValue(hash, out var p)) continue;
                if (q + p >= best) continue;
                if (!a.AsSpan(q, ChunkSize).SequenceEqual(b.AsSpan(p, ChunkSize))) continue;
                best = q + p;
                (y, x) = (q, p);
            }
            // 찾았고, 창 밖에 더 가까운 자리가 있을 수 없으면(y+x가 창 이내) 끝. 더 넓은 창이 남았으면 넓혀 본다.
            var last = window == ResyncWindows[^1];
            var exhausted = la < window + ChunkSize && lb < window + ChunkSize; // 파일 끝까지 다 봤다
            if (best != long.MaxValue && (best <= window || last || exhausted)) return true;
        }
        return false;
    }

    const uint Base = 257;
    static readonly uint BasePow = PowBase(ChunkSize - 1);

    static uint PowBase(int n)
    {
        uint r = 1;
        for (var k = 0; k < n; k++) r = unchecked(r * Base);
        return r;
    }

    static uint Hash(ReadOnlySpan<byte> s)
    {
        uint h = 0;
        foreach (var v in s) h = unchecked(h * Base + v);
        return h;
    }

    /// <summary>창이 한 칸 밀렸을 때의 해시: 앞 바이트를 빼고 새 바이트를 더한다.</summary>
    static uint Roll(uint hash, byte outgoing, byte incoming) =>
        unchecked((hash - outgoing * BasePow) * Base + incoming);
}
