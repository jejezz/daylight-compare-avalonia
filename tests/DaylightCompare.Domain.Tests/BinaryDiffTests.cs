using DaylightCompare.Domain.Binary;
using DaylightCompare.Domain.Diff;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Tests;

public class BinaryDiffTests
{
    static MemoryByteSource Src(params byte[] b) => new(b);

    static byte[] Random(int length, int seed)
    {
        var r = new System.Random(seed);
        var b = new byte[length];
        r.NextBytes(b);
        return b;
    }

    static string Describe(BinaryDiffResult r) =>
        string.Join(" ", r.Blocks.Select(b => $"{b.Kind.ToString()[0]}{b.LeftStart}+{b.LeftLength}/{b.RightStart}+{b.RightLength}"));

    [Fact]
    public void 같은_파일은_구간_하나이고_같다()
    {
        var data = Random(200_000, 1);
        var r = BinaryDiff.Compare(new MemoryByteSource(data), new MemoryByteSource((byte[])data.Clone()));
        Assert.True(r.IsIdentical);
        Assert.Single(r.Blocks);
        Assert.Equal(DiffBlockKind.Equal, r.Blocks[0].Kind);
        Assert.Equal(200_000, r.Blocks[0].LeftLength);
    }

    [Fact]
    public void 빈_파일끼리는_같고_구간이_없다()
    {
        var r = BinaryDiff.Compare(Src(), Src());
        Assert.True(r.IsIdentical);
        Assert.Empty(r.Blocks);
    }

    [Fact]
    public void 고정_위치는_바뀐_바이트만_다른_구간으로_나눈다()
    {
        var a = Random(300_000, 2);
        var b = (byte[])a.Clone();
        b[10] ^= 0xFF;
        b[11] ^= 0xFF;
        b[200_000] ^= 0x01;
        var r = BinaryDiff.Compare(new MemoryByteSource(a), new MemoryByteSource(b));
        Assert.Equal("E0+10/0+10 C10+2/10+2 E12+199988/12+199988 C200000+1/200000+1 E200001+99999/200001+99999", Describe(r));
        Assert.Equal(2, r.DifferenceCount);
        Assert.Equal(3, r.DifferentBytes);
    }

    [Fact]
    public void 고정_위치에서_길이가_다르면_꼬리는_한쪽에만_있다()
    {
        var a = Src(1, 2, 3, 4, 5);
        var b = Src(1, 2, 3);
        Assert.Equal("E0+3/0+3 L3+2/3+0", Describe(BinaryDiff.Compare(a, b)));
        Assert.Equal("E0+3/0+3 R3+0/3+2", Describe(BinaryDiff.Compare(b, a)));
    }

    [Fact]
    public void 삽입_감지는_끼어든_바이트만_한쪽에만_있는_구간으로_보인다()
    {
        var a = Random(100_000, 3);
        var b = a.Take(40_000).Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01 }).Concat(a.Skip(40_000)).ToArray();
        var r = BinaryDiff.Compare(new MemoryByteSource(a), new MemoryByteSource(b), BinaryAlignKind.Insertions);
        Assert.Equal("E0+40000/0+40000 R40000+0/40000+5 E40000+60000/40005+60000", Describe(r));
        // 고정 위치로는 뒤가 모두 다르게 나온다 (그래서 삽입 감지가 필요하다).
        var fixedResult = BinaryDiff.Compare(new MemoryByteSource(a), new MemoryByteSource(b));
        Assert.True(fixedResult.DifferentBytes > 50_000);
    }

    [Fact]
    public void 삽입_감지는_빠진_바이트와_바뀐_바이트도_맞춘다()
    {
        var a = Random(80_000, 4);
        // 왼쪽의 20000에서 7바이트가 빠지고, 그 뒤 (오른쪽 49990) 3바이트가 바뀐다
        var b = a.Take(20_000).Concat(a.Skip(20_007)).ToArray();
        b[49_990] ^= 0xFF; b[49_991] ^= 0xFF; b[49_992] ^= 0xFF;
        var r = BinaryDiff.Compare(new MemoryByteSource(a), new MemoryByteSource(b), BinaryAlignKind.Insertions);
        Assert.Equal("E0+20000/0+20000 L20000+7/20000+0 E20007+29990/20000+29990 C49997+3/49990+3 E50000+30000/49993+30000", Describe(r));
    }

    [Fact]
    public void 삽입_감지에서_파일_끝쪽_차이는_꼬리_구간이_된다()
    {
        var a = Random(5000, 5);
        var b = a.Concat(new byte[] { 1, 2, 3 }).ToArray();
        var r = BinaryDiff.Compare(new MemoryByteSource(a), new MemoryByteSource(b), BinaryAlignKind.Insertions);
        Assert.Equal("E0+5000/0+5000 R5000+0/5000+3", Describe(r));
    }

    [Fact]
    public void 아주_다른_파일도_멈추지_않고_모두_다른_구간이다()
    {
        var a = Random(300_000, 6);
        var b = Random(300_000, 7);
        var r = BinaryDiff.Compare(new MemoryByteSource(a), new MemoryByteSource(b), BinaryAlignKind.Insertions);
        Assert.Equal(300_000, r.Blocks.Sum(x => x.LeftLength));
        Assert.Equal(300_000, r.Blocks.Sum(x => x.RightLength));
        Assert.All(r.Blocks, x => Assert.Equal(DiffBlockKind.Changed, x.Kind));
    }

    [Fact]
    public void 구간은_두_파일_전체를_빈틈없이_덮는다()
    {
        var a = Random(150_000, 8);
        var b = a.Take(70_000).Concat(Random(33, 9)).Concat(a.Skip(70_021)).ToArray();
        foreach (var align in new[] { BinaryAlignKind.Fixed, BinaryAlignKind.Insertions })
        {
            var r = BinaryDiff.Compare(new MemoryByteSource(a), new MemoryByteSource(b), align);
            long l = 0, rr = 0;
            foreach (var blk in r.Blocks)
            {
                Assert.Equal(l, blk.LeftStart);
                Assert.Equal(rr, blk.RightStart);
                l += blk.LeftLength;
                rr += blk.RightLength;
            }
            Assert.Equal(a.Length, l);
            Assert.Equal(b.Length, rr);
        }
    }

    [Fact]
    public void 무시_구간은_가려서_같게_본다()
    {
        var a = Random(1000, 10);
        var b = (byte[])a.Clone();
        for (var i = 100; i < 108; i++) b[i] ^= 0xFF; // 헤더의 타임스탬프 자리
        b[500] ^= 0x01;                               // 진짜 차이
        IReadOnlyList<ByteRange> ignore = [new ByteRange(100, 8)];
        var r = BinaryDiff.Compare(new MaskedByteSource(new MemoryByteSource(a), ignore), new MaskedByteSource(new MemoryByteSource(b), ignore));
        Assert.Equal("E0+500/0+500 C500+1/500+1 E501+499/501+499", Describe(r));
        // 가리지 않으면 둘 다 보인다.
        Assert.Equal(2, BinaryDiff.Compare(new MemoryByteSource(a), new MemoryByteSource(b)).DifferenceCount);
    }

    [Fact]
    public void 취소하면_멈춘다()
    {
        var a = Random(400_000, 11);
        var b = Random(400_000, 12);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => BinaryDiff.Compare(new MemoryByteSource(a), new MemoryByteSource(b), BinaryAlignKind.Insertions, cts.Token));
    }
}
