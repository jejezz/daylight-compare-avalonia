using DaylightCompare.Domain.Binary;
using DaylightCompare.Domain.Diff;

namespace DaylightCompare.Domain.Tests;

public class BinaryRowsTests
{
    static BinaryDiffResult R(long left, long right, params ByteBlock[] blocks) => new(blocks, left, right);

    [Fact]
    public void 구간마다_열여섯_바이트씩_행으로_자르고_양쪽을_같은_행에_맞춘다()
    {
        // 같은 40바이트, 오른쪽에만 5바이트 끼어듦, 같은 20바이트
        var result = R(60, 65,
            new ByteBlock(DiffBlockKind.Equal, 0, 40, 0, 40),
            new ByteBlock(DiffBlockKind.RightOnly, 40, 0, 40, 5),
            new ByteBlock(DiffBlockKind.Equal, 40, 20, 45, 20));
        var rows = new BinaryRows(result);
        Assert.Equal(3 + 1 + 2, rows.RowCount);

        Assert.Equal(new BinaryRow(0, DiffBlockKind.Equal, 0, 16, 0, 16), rows.At(0));
        Assert.Equal(new BinaryRow(0, DiffBlockKind.Equal, 32, 8, 32, 8), rows.At(2));       // 마지막 행은 짧다
        Assert.Equal(new BinaryRow(1, DiffBlockKind.RightOnly, 40, 0, 40, 5), rows.At(3));   // 왼쪽은 채움 줄
        Assert.Equal(new BinaryRow(2, DiffBlockKind.Equal, 40, 16, 45, 16), rows.At(4));
        Assert.Equal(new BinaryRow(2, DiffBlockKind.Equal, 56, 4, 61, 4), rows.At(5));
        Assert.Equal(2, rows.BlockOfRow(5));
        Assert.Equal(3, rows.FirstRowOf(1));
    }

    [Fact]
    public void 바뀐_구간은_길이가_달라도_긴_쪽만큼_행이_있다()
    {
        var rows = new BinaryRows(R(20, 5, new ByteBlock(DiffBlockKind.Changed, 0, 20, 0, 5)));
        Assert.Equal(2, rows.RowCount);
        Assert.Equal(new BinaryRow(0, DiffBlockKind.Changed, 0, 16, 0, 5), rows.At(0));
        Assert.Equal(new BinaryRow(0, DiffBlockKind.Changed, 16, 4, 16, 0), rows.At(1));
    }

    [Fact]
    public void 다음_이전_차이_행과_위치로_행_찾기()
    {
        var result = R(100, 100,
            new ByteBlock(DiffBlockKind.Equal, 0, 32, 0, 32),
            new ByteBlock(DiffBlockKind.Changed, 32, 4, 32, 4),
            new ByteBlock(DiffBlockKind.Equal, 36, 30, 36, 30),
            new ByteBlock(DiffBlockKind.Changed, 66, 2, 66, 2),
            new ByteBlock(DiffBlockKind.Equal, 68, 32, 68, 32));
        var rows = new BinaryRows(result);
        var firstDiff = rows.FirstRowOf(1);
        var secondDiff = rows.FirstRowOf(3);
        Assert.Equal(firstDiff, rows.NextDifferenceRow(0, forward: true));
        Assert.Equal(secondDiff, rows.NextDifferenceRow(firstDiff, forward: true));
        Assert.Null(rows.NextDifferenceRow(secondDiff, forward: true));
        Assert.Equal(firstDiff, rows.NextDifferenceRow(secondDiff, forward: false));
        Assert.Null(rows.NextDifferenceRow(firstDiff, forward: false));
        // 위치 → 행
        Assert.Equal(0, rows.RowOfLeftOffset(0));
        Assert.Equal(1, rows.RowOfLeftOffset(20));
        Assert.Equal(firstDiff, rows.RowOfLeftOffset(33));
        Assert.Equal(rows.RowCount - 1, rows.RowOfRightOffset(99));
    }

    [Fact]
    public void 큰_파일도_행을_만들어_두지_않고_계산한다()
    {
        long size = 4L * 1024 * 1024 * 1024; // 4GB
        var rows = new BinaryRows(R(size, size, new ByteBlock(DiffBlockKind.Equal, 0, size, 0, size)));
        Assert.Equal(size / 16, rows.RowCount);
        Assert.Equal(size - 16, rows.At(rows.RowCount - 1).LeftOffset);
    }
}
