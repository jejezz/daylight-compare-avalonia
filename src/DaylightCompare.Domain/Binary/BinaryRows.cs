using DaylightCompare.Domain.Diff;

namespace DaylightCompare.Domain.Binary;

/// <summary>
/// 화면 한 행: 구간(<see cref="Block"/>) 안의 <see cref="IndexInBlock"/>번째 줄. 왼쪽은 <see cref="LeftOffset"/>부터 <see cref="LeftCount"/>바이트,
/// 오른쪽은 <see cref="RightOffset"/>부터 <see cref="RightCount"/>바이트 (한쪽에만 있는 구간이면 다른 쪽 개수는 0 — 화면에서 채움 줄).
/// </summary>
public readonly record struct BinaryRow(int Block, DiffBlockKind Kind, long LeftOffset, int LeftCount, long RightOffset, int RightCount);

/// <summary>
/// 바이너리 비교의 행 모양: 구간마다 16바이트씩 행으로 자르고(구간의 시작에서 시작) 양쪽을 같은 행에 맞춘다.
/// 같은 구간은 양쪽이 나란히, 한쪽에만 있는 구간은 반대쪽이 비고, 바뀐 구간은 길이가 다르면 짧은 쪽이 먼저 끝난다.
/// 행 수는 수천만이 될 수 있어(GB 파일) 행을 만들어 두지 않고 구간별 시작 행 표로 <see cref="At"/>이 계산한다.
/// </summary>
public sealed class BinaryRows
{
    public const int Width = 16;

    readonly IReadOnlyList<ByteBlock> _blocks;
    readonly long[] _firstRow; // 구간마다 첫 행 번호 (마지막 값 = 전체 행 수)

    public BinaryRows(BinaryDiffResult result)
    {
        _blocks = result.Blocks;
        _firstRow = new long[_blocks.Count + 1];
        for (var i = 0; i < _blocks.Count; i++)
            _firstRow[i + 1] = _firstRow[i] + RowsOf(_blocks[i]);
    }

    static long RowsOf(ByteBlock b) => (Math.Max(b.LeftLength, b.RightLength) + Width - 1) / Width;

    public long RowCount => _firstRow[^1];

    public IReadOnlyList<ByteBlock> Blocks => _blocks;

    /// <summary>행 <paramref name="row"/>의 내용.</summary>
    public BinaryRow At(long row)
    {
        var block = BlockOfRow(row);
        var b = _blocks[block];
        var k = row - _firstRow[block];
        long start = k * Width;
        return new BinaryRow(block, b.Kind,
            b.LeftStart + start, (int)Math.Clamp(b.LeftLength - start, 0, Width),
            b.RightStart + start, (int)Math.Clamp(b.RightLength - start, 0, Width));
    }

    /// <summary>행이 속한 구간 번호 (이진 탐색).</summary>
    public int BlockOfRow(long row)
    {
        var lo = 0;
        var hi = _blocks.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (_firstRow[mid] <= row) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>구간의 첫 행.</summary>
    public long FirstRowOf(int block) => _firstRow[block];

    /// <summary>구간 안 행 번호와 바이트 번호(0~15)로 바이트의 왼쪽·오른쪽 위치를 구한다 (없으면 null).</summary>
    public (long? Left, long? Right) OffsetsAt(long row, int column)
    {
        var r = At(row);
        return (column < r.LeftCount ? r.LeftOffset + column : null, column < r.RightCount ? r.RightOffset + column : null);
    }

    // --- 차이 이동 ---

    /// <summary><paramref name="row"/> 뒤(또는 앞)의 다음 다른 구간의 첫 행. 없으면 null.</summary>
    public long? NextDifferenceRow(long row, bool forward)
    {
        if (_blocks.Count == 0) return null;
        var current = BlockOfRow(Math.Clamp(row, 0, Math.Max(0, RowCount - 1)));
        if (forward)
        {
            for (var i = current + 1; i < _blocks.Count; i++) if (_blocks[i].IsDifference) return _firstRow[i];
            return null;
        }
        // 뒤로: 지금 구간이 다른 구간이고 첫 행이 아니면 그 첫 행이 "앞의 차이"가 아니라 현재 구간의 시작이므로 한 구간 앞에서 찾는다.
        for (var i = current - 1; i >= 0; i--) if (_blocks[i].IsDifference) return _firstRow[i];
        return null;
    }

    /// <summary>왼쪽 위치(바이트)를 가진 행 (위치가 어느 구간에도 없으면 마지막 행).</summary>
    public long RowOfLeftOffset(long offset) => RowOfOffset(offset, left: true);

    public long RowOfRightOffset(long offset) => RowOfOffset(offset, left: false);

    long RowOfOffset(long offset, bool left)
    {
        if (_blocks.Count == 0) return 0;
        // 구간은 위치 순서로 이어지므로 시작 위치로 이진 탐색한다 (길이 0인 구간은 건너뛴다).
        var lo = 0;
        var hi = _blocks.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            var start = left ? _blocks[mid].LeftStart : _blocks[mid].RightStart;
            if (start <= offset) lo = mid; else hi = mid - 1;
        }
        var b = _blocks[lo];
        var bs = left ? b.LeftStart : b.RightStart;
        var inRow = (offset - bs) / Width;
        return Math.Min(_firstRow[lo] + Math.Max(0, inRow), Math.Max(0, RowCount - 1));
    }
}
